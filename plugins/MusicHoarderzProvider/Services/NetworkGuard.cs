using System;
using System.Net;
using System.Net.Sockets;

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

        return !IsBlockedHostLiteral(uri.Host);
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

        var normalized = host.Trim().TrimEnd('.');
        if (string.Equals(normalized, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (normalized.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith(".internal", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith(".invalid", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (IPAddress.TryParse(normalized, out var address) && IsPrivateOrLoopback(address))
        {
            return true;
        }

        return false;
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

        var allZero = true;
        for (var i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] != 0)
            {
                allZero = false;
                break;
            }
        }

        if (allZero)
        {
            return true;
        }

        // fc00::/7 (unique local).
        if ((bytes[0] & 0xFE) == 0xFC)
        {
            return true;
        }

        // fe80::/10 (link local).
        if (bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0x80)
        {
            return true;
        }

        return false;
    }
}
