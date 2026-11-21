using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.MusicHoarderzProvider.Services;

/// <summary>
/// Pure network safety checks shared by the HTTP client and the image validator.
/// </summary>
public static class NetworkGuard
{
    /// <summary>
    /// Checks whether the URI uses https with a non-blocked literal host.
    /// Literal checks only (no DNS); the validator additionally resolves DNS.
    /// </summary>
    /// <param name="uri">URI to check.</param>
    /// <returns>True when the URI is https and the host is not blocked literally.</returns>
    public static bool IsPublicHttps(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri)
        {
            return false;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return string.IsNullOrEmpty(uri.UserInfo)
            && string.IsNullOrEmpty(uri.Fragment)
            && !IsBlockedHostLiteral(uri.IdnHost);
    }

    /// <summary>
    /// Checks a literal hostname for blocked values without DNS resolution.
    /// </summary>
    /// <param name="host">Hostname or IP literal.</param>
    /// <returns>True when the host literal is blocked.</returns>
    public static bool IsBlockedHostLiteral(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return true;
        }

        var normalized = host.Trim().Trim('[', ']').TrimEnd('.');
        if (string.Equals(normalized, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (normalized.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith(".internal", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith(".home.arpa", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith(".invalid", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (IPAddress.TryParse(normalized, out var address))
        {
            return IsPrivateOrLoopback(address);
        }

        // Single-label names are local DNS names, not public internet endpoints.
        return !normalized.Contains('.', StringComparison.Ordinal);
    }

    /// <summary>
    /// Checks whether an IP address is loopback, private, link-local or otherwise non-routable.
    /// </summary>
    /// <param name="address">IP address to check.</param>
    /// <returns>True when the address must not be contacted.</returns>
    public static bool IsPrivateOrLoopback(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv4MappedToIPv6)
            {
                address = address.MapToIPv4();
            }
            else
            {
                return IsBlockedIPv6(address.GetAddressBytes());
            }
        }

        var bytes = address.GetAddressBytes();
        if (bytes.Length != 4)
        {
            return true;
        }

        // 0.0.0.0/8 (unspecified).
        if (bytes[0] == 0)
        {
            return true;
        }

        // 10.0.0.0/8 (private).
        if (bytes[0] == 10)
        {
            return true;
        }

        // 172.16.0.0/12 (private).
        if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
        {
            return true;
        }

        // 192.168.0.0/16 (private).
        if (bytes[0] == 192 && bytes[1] == 168)
        {
            return true;
        }

        // 169.254.0.0/16 (link-local).
        if (bytes[0] == 169 && bytes[1] == 254)
        {
            return true;
        }

        // 100.64.0.0/10 (carrier-grade NAT).
        if (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127)
        {
            return true;
        }

        // Documentation ranges that never route publicly.
        if ((bytes[0] == 192 && bytes[1] == 0 && bytes[2] == 0)
            || (bytes[0] == 192 && bytes[1] == 88 && bytes[2] == 99)
            || (bytes[0] == 198 && (bytes[1] == 18 || bytes[1] == 19)))
        {
            return true;
        }

        if (bytes[0] == 192 && bytes[1] == 0 && bytes[2] == 2)
        {
            return true;
        }

        if (bytes[0] == 198 && bytes[1] == 51 && bytes[2] == 100)
        {
            return true;
        }

        if (bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113)
        {
            return true;
        }

        // 224.0.0.0/4 (multicast) and 240.0.0.0/4 (reserved).
        if (bytes[0] >= 224)
        {
            return true;
        }

        return false;
    }

    private static bool IsBlockedIPv6(byte[] bytes)
    {
        if (bytes.Length != 16)
        {
            return true;
        }

        // Only global unicast 2000::/3; excludes loopback, local, multicast,
        // IPv4-compatible and NAT64 addresses that could reach internal IPv4 hosts.
        return (bytes[0] & 0xE0) != 0x20
            || (bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] < 2)
            || (bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0D && bytes[3] == 0xB8)
            || (bytes[0] == 0x20 && bytes[1] == 0x02)
            || (bytes[0] == 0x3F && (bytes[1] & 0xF0) == 0xF0);
    }

    /// <summary>
    /// Creates the transport used for untrusted provider URLs. DNS is validated at
    /// connection time and the socket uses that exact IP, preventing DNS rebinding.
    /// Redirects are handled and revalidated by the caller; proxy DNS is disabled.
    /// </summary>
    /// <returns>A fail-closed HTTP transport.</returns>
    public static SocketsHttpHandler CreateHandler()
    {
        return new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            UseCookies = false,
            ConnectTimeout = TimeSpan.FromSeconds(20),
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            MaxResponseHeadersLength = 32,
            ConnectCallback = ConnectPublicAsync,
        };
    }

    private static async ValueTask<Stream> ConnectPublicAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var host = context.DnsEndPoint.Host;
        if (IsBlockedHostLiteral(host))
        {
            throw new HttpRequestException("Remote endpoint was blocked by network policy.");
        }

        var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
        if (addresses.Length == 0 || Array.Exists(addresses, IsPrivateOrLoopback))
        {
            throw new HttpRequestException("Remote endpoint was blocked by network policy.");
        }

        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                // Never reconnect by hostname after validation: TLS still uses the
                // request's original hostname for SNI and certificate verification.
                await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), cancellationToken).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (SocketException)
            {
                socket.Dispose();
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        throw new HttpRequestException("Could not connect to the remote endpoint.");
    }
}
