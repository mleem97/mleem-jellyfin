using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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
    private readonly ICoverImageDecoder _decoder;
    private readonly Func<string, CancellationToken, Task<IPAddress[]>> _hostAddressResolver;

    /// <summary>
    /// Initializes a new instance of the <see cref="CoverImageValidator"/> class.
    /// </summary>
    /// <param name="httpClientFactory">HTTP client factory.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="decoder">Mandatory real pixel decoder (mocked only in offline tests).</param>
    /// <param name="hostAddressResolver">Optional DNS resolver override (used by tests).</param>
    public CoverImageValidator(
        IHttpClientFactory httpClientFactory,
        ILogger<CoverImageValidator> logger,
        ICoverImageDecoder decoder,
        Func<string, CancellationToken, Task<IPAddress[]>>? hostAddressResolver = null)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _decoder = decoder;
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

        using var client = _httpClientFactory.CreateClient(HttpClientName);
        client.Timeout = Timeout.InfiniteTimeSpan;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));
        var requestToken = timeout.Token;

        try
        {
            var current = uri;
            for (var hop = 0; hop <= MaxRedirectHops; hop++)
            {
                requestToken.ThrowIfCancellationRequested();
                if (!await IsHostPublicAsync(current.IdnHost, requestToken).ConfigureAwait(false))
                {
                    LogHostBlocked(_logger);
                    return null;
                }

                using var request = new HttpRequestMessage(HttpMethod.Get, current);
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, requestToken).ConfigureAwait(false);
                if (IsRedirect(response.StatusCode) && response.Headers.Location is not null)
                {
                    var next = new Uri(current, response.Headers.Location);
                    if (!NetworkGuard.IsPublicHttps(next))
                    {
                        LogRedirectBlocked(_logger);
                        return null;
                    }

                    current = next;
                    continue;
                }

                return await ReadValidatedAsync(response, current.ToString(), minimumWidth, minimumHeight, requestToken).ConfigureAwait(false);
            }

            LogRedirectBlocked(_logger);
            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            LogDownloadTimeout(_logger);
            return null;
        }
        catch (HttpRequestException)
        {
            LogDownloadFailed(_logger);
            return null;
        }
        catch (IOException)
        {
            LogDownloadFailed(_logger);
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
            bytes = await BoundedHttpContent.ReadAsync(response.Content, MaxImageBytes, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidDataException)
        {
            LogTooLarge(_logger, MaxImageBytes + 1L);
            LogDownloadFailed(_logger);
            return null;
        }

        if (!string.Equals(contentType, IdentifyMimeType(bytes), StringComparison.Ordinal))
        {
            LogSniffRejected(_logger);
            return null;
        }

        if (!IsCompleteContainer(bytes, contentType) || !TryGetDimensions(bytes, out var width, out var height))
        {
            LogDimensionsUnknown(_logger);
            return null;
        }

        if (width > 16384 || height > 16384 || ((long)width * height) > 64000000)
        {
            LogTooLarge(_logger, bytes.Length);
            return null;
        }

        if (width < Math.Max(1, minimumWidth) || height < Math.Max(1, minimumHeight))
        {
            LogTooSmall(_logger, width, height);
            return null;
        }

        if (!await _decoder.CanDecodeAsync(bytes, contentType, cancellationToken).ConfigureAwait(false))
        {
            LogSniffRejected(_logger);
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
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (System.Net.Sockets.SocketException)
        {
            LogDnsFailed(_logger);
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

    private static string? IdentifyMimeType(byte[] bytes)
    {
        if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF
            && bytes[^2] == 0xFF && bytes[^1] == 0xD9)
        {
            return "image/jpeg";
        }

        if (bytes.Length >= 33
            && bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })
            && bytes.AsSpan(8, 8).SequenceEqual(new byte[] { 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R' }))
        {
            return "image/png";
        }

        if (bytes.Length >= 14 && bytes[0] == 'G' && bytes[1] == 'I' && bytes[2] == 'F'
            && bytes[3] == '8' && (bytes[4] == '7' || bytes[4] == '9') && bytes[5] == 'a'
            && bytes[^1] == 0x3B)
        {
            return "image/gif";
        }

        if (bytes.Length >= 25 && bytes[0] == 'R' && bytes[1] == 'I' && bytes[2] == 'F' && bytes[3] == 'F'
            && bytes[8] == 'W' && bytes[9] == 'E' && bytes[10] == 'B' && bytes[11] == 'P'
            && System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4, 4)) == bytes.Length - 8)
        {
            return "image/webp";
        }

        return null;
    }

    private static bool IsCompleteContainer(byte[] bytes, string mime)
    {
        if (mime == "image/png")
        {
            var hasData = false;
            var offset = 8;
            while (offset + 12 <= bytes.Length)
            {
                var length = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4));
                if (length > bytes.Length - offset - 12)
                {
                    return false;
                }

                var count = (int)length;
                var crc = uint.MaxValue;
                for (var index = offset + 4; index < offset + count + 8; index++)
                {
                    crc ^= bytes[index];
                    for (var bit = 0; bit < 8; bit++)
                    {
                        crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xEDB88320U);
                    }
                }

                if (~crc != System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset + count + 8, 4)))
                {
                    return false;
                }

                var type = System.Text.Encoding.ASCII.GetString(bytes, offset + 4, 4);
                if (type == "acTL")
                {
                    return false; // Animated PNG needs per-frame validation outside this MVP.
                }

                hasData |= type == "IDAT" && count > 0;
                offset += count + 12;
                if (type == "IEND")
                {
                    return count == 0 && hasData && offset == bytes.Length;
                }
            }

            return false;
        }

        if (mime == "image/webp")
        {
            var offset = 12;
            var hasFrame = false;
            while (offset + 8 <= bytes.Length)
            {
                var length = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + 4, 4));
                if (length > bytes.Length - offset - 8)
                {
                    return false;
                }

                var type = System.Text.Encoding.ASCII.GetString(bytes, offset, 4);
                if (type == "ANIM" || type == "ANMF")
                {
                    return false;
                }

                hasFrame |= (type == "VP8 " || type == "VP8L") && length > 0;
                offset += 8 + (int)length + ((int)length & 1);
            }

            return hasFrame && offset == bytes.Length;
        }

        if (mime == "image/gif")
        {
            return IsCompleteGif(bytes);
        }

        // A JPEG must include a start-of-scan segment in addition to SOF and EOI.
        for (var index = 2; index + 3 < bytes.Length;)
        {
            if (bytes[index] != 0xFF)
            {
                return false;
            }

            var marker = bytes[index + 1];
            if (marker == 0xFF)
            {
                index++;
                continue;
            }

            var length = (bytes[index + 2] << 8) | bytes[index + 3];
            if (length < 2 || index + length + 2 > bytes.Length)
            {
                return false;
            }

            if (marker == 0xDA)
            {
                return length >= 6 && index + length + 2 < bytes.Length - 2;
            }

            index += length + 2;
        }

        return false;
    }

    private static bool IsCompleteGif(byte[] bytes)
    {
        var offset = 13 + ((bytes[10] & 0x80) != 0 ? 3 * (1 << ((bytes[10] & 7) + 1)) : 0);
        var hasFrame = false;
        while (offset < bytes.Length)
        {
            var marker = bytes[offset++];
            if (marker == 0x3B)
            {
                return hasFrame && offset == bytes.Length;
            }

            if (marker == 0x21)
            {
                offset++; // Extension label.
            }
            else if (marker == 0x2C)
            {
                if (offset + 9 >= bytes.Length)
                {
                    return false;
                }

                var width = bytes[offset + 4] | (bytes[offset + 5] << 8);
                var height = bytes[offset + 6] | (bytes[offset + 7] << 8);
                var left = bytes[offset] | (bytes[offset + 1] << 8);
                var top = bytes[offset + 2] | (bytes[offset + 3] << 8);
                var screenWidth = bytes[6] | (bytes[7] << 8);
                var screenHeight = bytes[8] | (bytes[9] << 8);
                if (hasFrame || width == 0 || height == 0 || left + width > screenWidth || top + height > screenHeight)
                {
                    return false;
                }

                var packed = bytes[offset + 8];
                offset += 9 + ((packed & 0x80) != 0 ? 3 * (1 << ((packed & 7) + 1)) : 0);
                if (offset >= bytes.Length || bytes[offset] < 2 || bytes[offset] > 8)
                {
                    return false;
                }

                offset++; // LZW minimum code size.
                hasFrame = true;
            }
            else
            {
                return false;
            }

            var ended = false;
            while (offset < bytes.Length)
            {
                var length = bytes[offset++];
                if (length == 0)
                {
                    ended = true;
                    break;
                }

                offset += length;
            }

            if (!ended)
            {
                return false;
            }
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
            if (length < 2 || index + 2 + length > bytes.Length)
            {
                return false;
            }

            if (IsJpegSof(marker))
            {
                if (length < 8 || index + 8 >= bytes.Length)
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
        if (bytes.Length < 25)
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
    private static partial void LogDownloadFailed(ILogger logger);

    [LoggerMessage(EventId = 11, Level = LogLevel.Warning, Message = "Cover image download timed out.")]
    private static partial void LogDownloadTimeout(ILogger logger);

    [LoggerMessage(EventId = 12, Level = LogLevel.Warning, Message = "Cover image host resolution failed.")]
    private static partial void LogDnsFailed(ILogger logger);
}
