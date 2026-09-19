using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MusicHoarderzProvider.Services;

/// <summary>
/// Validated cover image: fully loaded and checked before bytes are returned (atomic).
/// </summary>
/// <param name="Url">Final image URL.</param>
/// <param name="ContentType">Normalized image MIME type.</param>
/// <param name="Width">Image width in pixels.</param>
/// <param name="Height">Image height in pixels.</param>
/// <param name="Content">Validated image bytes.</param>
public sealed record ValidatedCoverImage(string Url, string ContentType, int Width, int Height, ReadOnlyMemory<byte> Content);

/// <summary>
/// Validates remote cover images: https only, MIME allowlist, magic-byte sniffing,
/// size limits, minimum dimensions via header parsing, and DNS block for
/// loopback/private addresses. Atomic: bytes are returned only after all checks pass.
/// </summary>
public sealed partial class CoverImageValidator
{
    /// <summary>
    /// Maximum accepted image size (15 MB).
    /// </summary>
    public const int MaxImageBytes = 15 * 1024 * 1024;

    private const string HttpClientName = "musichoarderz-images";
    private const int TimeoutSeconds = 20;
    private const int MaxRedirectHops = 3;

    private static readonly HashSet<string> AllowedMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png",
        "image/webp",
        "image/gif",
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<CoverImageValidator> _logger;
    private readonly Func<string, CancellationToken, Task<IPAddress[]>> _hostAddressResolver;

    /// <summary>
    /// Initializes a new instance of the <see cref="CoverImageValidator"/> class.
    /// </summary>
    /// <param name="httpClientFactory">HTTP client factory.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="hostAddressResolver">Optional DNS resolver override (used by tests).</param>
    public CoverImageValidator(
        IHttpClientFactory httpClientFactory,
        ILogger<CoverImageValidator> logger,
        Func<string, CancellationToken, Task<IPAddress[]>>? hostAddressResolver = null)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _hostAddressResolver = hostAddressResolver ?? System.Net.Dns.GetHostAddressesAsync;
    }

    /// <summary>
    /// Downloads and validates a cover image atomically.
    /// </summary>
    /// <param name="url">Candidate image URL.</param>
    /// <param name="minimumWidth">Configured minimum width.</param>
    /// <param name="minimumHeight">Configured minimum height.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Validated image, or null when invalid (fail-closed).</returns>
    public async Task<ValidatedCoverImage?> ValidateAsync(
        string url,
        int minimumWidth,
        int minimumHeight,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || !NetworkGuard.IsPublicHttps(uri))
        {
            LogUrlRejected(_logger);
            return null;
        }

        if (!await IsHostPublicAsync(uri.Host, cancellationToken).ConfigureAwait(false))
        {
            LogHostBlocked(_logger);
            return null;
        }

        var client = _httpClientFactory.CreateClient(HttpClientName);
        client.Timeout = TimeSpan.FromSeconds(TimeoutSeconds);

        try
        {
            var current = uri;
            for (var hop = 0; hop <= MaxRedirectHops; hop++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var request = new HttpRequestMessage(HttpMethod.Get, current);
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                if (IsRedirect(response.StatusCode) && response.Headers.Location is not null)
                {
                    var next = new Uri(current, response.Headers.Location);
                    if (!NetworkGuard.IsPublicHttps(next)
                        || !await IsHostPublicAsync(next.Host, cancellationToken).ConfigureAwait(false))
                    {
                        LogRedirectBlocked(_logger);
                        return null;
                    }

                    current = next;
                    continue;
                }

                return await ReadValidatedAsync(response, current.ToString(), minimumWidth, minimumHeight, cancellationToken).ConfigureAwait(false);
            }

            LogRedirectBlocked(_logger);
            return null;
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            LogDownloadTimeout(_logger, ex);
            return null;
        }
        catch (HttpRequestException ex)
        {
            LogDownloadFailed(_logger, ex);
            return null;
        }
        catch (IOException ex)
        {
            LogDownloadFailed(_logger, ex);
            return null;
        }
    }

    private async Task<ValidatedCoverImage?> ReadValidatedAsync(
        HttpResponseMessage response,
        string finalUrl,
        int minimumWidth,
        int minimumHeight,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (!response.IsSuccessStatusCode)
        {
            LogHttpError(_logger, (int)response.StatusCode);
            return null;
        }

        var contentType = NormalizeMimeType(response.Content.Headers.ContentType?.MediaType);
        if (contentType is null)
        {
            LogMimeRejected(_logger);
            return null;
        }

        if (response.Content.Headers.ContentLength.HasValue
            && response.Content.Headers.ContentLength.Value > MaxImageBytes)
        {
            LogTooLarge(_logger, response.Content.Headers.ContentLength.Value);
            return null;
        }

        byte[] bytes;
        try
        {
            bytes = await ReadCappedAsync(response.Content, MaxImageBytes, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidDataException ex)
        {
            LogTooLarge(_logger, MaxImageBytes + 1L);
            LogDownloadFailed(_logger, ex);
            return null;
        }

        if (bytes.Length == 0 || !HasKnownImageMagic(bytes))
        {
            LogSniffRejected(_logger);
            return null;
        }

        if (!TryGetDimensions(bytes, out var width, out var height))
        {
            LogDimensionsUnknown(_logger);
            return null;
        }

        if (width < minimumWidth || height < minimumHeight)
        {
            LogTooSmall(_logger, width, height);
            return null;
        }

        return new ValidatedCoverImage(finalUrl, contentType, width, height, new ReadOnlyMemory<byte>(bytes));
    }

    private async Task<bool> IsHostPublicAsync(string host, CancellationToken cancellationToken)
    {
        IPAddress[] addresses;
        try
        {
            addresses = await _hostAddressResolver(host, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogDnsFailed(_logger, ex);
            return false;
        }

        if (addresses.Length == 0)
        {
            return false;
        }

        foreach (var address in addresses)
        {
            if (address is null || NetworkGuard.IsPrivateOrLoopback(address))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsRedirect(HttpStatusCode statusCode)
    {
        return statusCode == HttpStatusCode.MovedPermanently
            || statusCode == HttpStatusCode.Found
            || statusCode == HttpStatusCode.SeeOther
            || statusCode == HttpStatusCode.TemporaryRedirect
            || (int)statusCode == 308;
    }

    private static string? NormalizeMimeType(string? mediaType)
    {
        if (string.IsNullOrWhiteSpace(mediaType))
        {
            return null;
        }

        var mime = mediaType.Split(';', StringSplitOptions.RemoveEmptyEntries)[0].Trim();
        if (string.Equals(mime, "image/jpg", StringComparison.OrdinalIgnoreCase))
        {
            mime = "image/jpeg";
        }

        return AllowedMimeTypes.Contains(mime) ? mime.ToLowerInvariant() : null;
    }

    private static async Task<byte[]> ReadCappedAsync(HttpContent content, int maxBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        var total = 0;
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            total += read;
            if (total > maxBytes)
            {
                throw new InvalidDataException("Response exceeded the size limit.");
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        return buffer.ToArray();
    }

    private static bool HasKnownImageMagic(byte[] bytes)
    {
        var index = 0;
        while (index < bytes.Length && (bytes[index] == 0x20 || bytes[index] == 0x09 || bytes[index] == 0x0A || bytes[index] == 0x0D))
        {
            index++;
        }

        var remaining = bytes.Length - index;
        if (remaining <= 0)
        {
            return false;
        }

        // Reject HTML/JSON/text payloads served as images.
        var first = bytes[index];
        if (first == (byte)'<' || first == (byte)'{' || first == (byte)'[')
        {
            return false;
        }

        // JPEG: FF D8 FF.
        if (remaining >= 3 && bytes[index] == 0xFF && bytes[index + 1] == 0xD8 && bytes[index + 2] == 0xFF)
        {
            return true;
        }

        // PNG: 89 50 4E 47 0D 0A 1A 0A.
        if (remaining >= 8 && bytes[index] == 0x89 && bytes[index + 1] == 0x50 && bytes[index + 2] == 0x4E && bytes[index + 3] == 0x47)
        {
            return true;
        }

        // GIF: GIF87a / GIF89a.
        if (remaining >= 6 && bytes[index] == 0x47 && bytes[index + 1] == 0x49 && bytes[index + 2] == 0x46)
        {
            return true;
        }

        // WebP: RIFF....WEBP.
        if (remaining >= 12
            && bytes[index] == 0x52 && bytes[index + 1] == 0x49 && bytes[index + 2] == 0x46 && bytes[index + 3] == 0x46
            && bytes[index + 8] == 0x57 && bytes[index + 9] == 0x45 && bytes[index + 10] == 0x42 && bytes[index + 11] == 0x50)
        {
            return true;
        }

        return false;
    }

    private static bool TryGetDimensions(byte[] bytes, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (bytes.Length < 10)
        {
            return false;
        }

        if (bytes[0] == 0xFF && bytes[1] == 0xD8)
        {
            return TryGetJpegDimensions(bytes, out width, out height);
        }

        if (bytes.Length >= 24 && bytes[0] == 0x89 && bytes[1] == 0x50)
        {
            width = (bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19];
            height = (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23];
            return width > 0 && height > 0;
        }

        if (bytes.Length >= 10 && bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46)
        {
            width = bytes[6] | (bytes[7] << 8);
            height = bytes[8] | (bytes[9] << 8);
            return width > 0 && height > 0;
        }

        if (bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46)
        {
            return TryGetWebPDimensions(bytes, out width, out height);
        }

        return false;
    }

    private static bool TryGetJpegDimensions(byte[] bytes, out int width, out int height)
    {
        width = 0;
        height = 0;
        var index = 2;
        while (index + 3 < bytes.Length)
        {
            if (bytes[index] != 0xFF)
            {
                return false;
            }

            var marker = bytes[index + 1];
            while (marker == 0xFF && index + 2 < bytes.Length)
            {
                index++;
                marker = bytes[index + 1];
            }

            if (marker == 0xD8 || marker == 0xD9 || (marker >= 0xD0 && marker <= 0xD7) || marker == 0x01)
            {
                index += 2;
                continue;
            }

            if (index + 3 >= bytes.Length)
            {
                return false;
            }

            var length = (bytes[index + 2] << 8) | bytes[index + 3];
            if (length < 2 || index + length + 1 >= bytes.Length + 1)
            {
                return false;
            }

            if (IsJpegSof(marker))
            {
                if (length < 7)
                {
                    return false;
                }

                height = (bytes[index + 5] << 8) | bytes[index + 6];
                width = (bytes[index + 7] << 8) | bytes[index + 8];
                return width > 0 && height > 0;
            }

            index += 2 + length;
        }

        return false;
    }

    private static bool IsJpegSof(int marker)
    {
        return (marker >= 0xC0 && marker <= 0xC3)
            || (marker >= 0xC5 && marker <= 0xC7)
            || (marker >= 0xC9 && marker <= 0xCB)
            || (marker >= 0xCD && marker <= 0xCF);
    }

    private static bool TryGetWebPDimensions(byte[] bytes, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (bytes.Length < 30)
        {
            return false;
        }

        var fourCc = string.Create(
            CultureInfo.InvariantCulture,
            $"{(char)bytes[12]}{(char)bytes[13]}{(char)bytes[14]}{(char)bytes[15]}");
        if (string.Equals(fourCc, "VP8 ", StringComparison.Ordinal))
        {
            if (bytes.Length < 30 || bytes[23] != 0x9D || bytes[24] != 0x01 || bytes[25] != 0x2A)
            {
                return false;
            }

            width = ((bytes[27] << 8) | bytes[26]) & 0x3FFF;
            height = ((bytes[29] << 8) | bytes[28]) & 0x3FFF;
            return width > 0 && height > 0;
        }

        if (string.Equals(fourCc, "VP8L", StringComparison.Ordinal))
        {
            if (bytes.Length < 25 || bytes[20] != 0x2F)
            {
                return false;
            }

            var b0 = bytes[21];
            var b1 = bytes[22];
            var b2 = bytes[23];
            var b3 = bytes[24];
            width = 1 + (((b1 & 0x3F) << 8) | b0);
            height = 1 + (((b3 & 0x0F) << 10) | (b2 << 2) | ((b1 & 0xC0) >> 6));
            return width > 0 && height > 0;
        }

        if (string.Equals(fourCc, "VP8X", StringComparison.Ordinal))
        {
            if (bytes.Length < 30)
            {
                return false;
            }

            width = 1 + (bytes[24] | (bytes[25] << 8) | (bytes[26] << 16));
            height = 1 + (bytes[27] | (bytes[28] << 8) | (bytes[29] << 16));
            return width > 0 && height > 0;
        }

        return false;
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Cover image URL rejected: not an allowed https endpoint.")]
    private static partial void LogUrlRejected(ILogger logger);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "Cover image host blocked by DNS policy.")]
    private static partial void LogHostBlocked(ILogger logger);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "Cover image redirect blocked.")]
    private static partial void LogRedirectBlocked(ILogger logger);

    [LoggerMessage(EventId = 4, Level = LogLevel.Warning, Message = "Cover image HTTP error {StatusCode}.")]
    private static partial void LogHttpError(ILogger logger, int statusCode);

    [LoggerMessage(EventId = 5, Level = LogLevel.Warning, Message = "Cover image MIME type rejected.")]
    private static partial void LogMimeRejected(ILogger logger);

    [LoggerMessage(EventId = 6, Level = LogLevel.Warning, Message = "Cover image exceeded the size limit ({Bytes} bytes).")]
    private static partial void LogTooLarge(ILogger logger, long bytes);

    [LoggerMessage(EventId = 7, Level = LogLevel.Warning, Message = "Cover image magic bytes rejected.")]
    private static partial void LogSniffRejected(ILogger logger);

    [LoggerMessage(EventId = 8, Level = LogLevel.Warning, Message = "Cover image dimensions unknown.")]
    private static partial void LogDimensionsUnknown(ILogger logger);

    [LoggerMessage(EventId = 9, Level = LogLevel.Information, Message = "Cover image below minimum dimensions ({Width}x{Height}).")]
    private static partial void LogTooSmall(ILogger logger, int width, int height);

    [LoggerMessage(EventId = 10, Level = LogLevel.Warning, Message = "Cover image download failed.")]
    private static partial void LogDownloadFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 11, Level = LogLevel.Warning, Message = "Cover image download timed out.")]
    private static partial void LogDownloadTimeout(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 12, Level = LogLevel.Warning, Message = "Cover image host resolution failed.")]
    private static partial void LogDnsFailed(ILogger logger, Exception exception);
}
