using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MusicHoarderzProvider.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MusicHoarderzProvider.Services;

/// <summary>
/// HTTP cover client for the MusicHoarderz/COV API with manual redirect handling,
/// response size limits and a small in-memory cache.
/// </summary>
public sealed partial class MusicHoarderzHttpClient : ICoverProvider, IAlbumMetadataProvider
{
    /// <summary>
    /// Default base URL used when no configuration is available.
    /// </summary>
    public const string DefaultBaseUrl = "https://covers.musichoarders.xyz";

    private const string HttpClientName = "musichoarderz";
    private const int TimeoutSeconds = 20;
    private const int MaxResponseBytes = 2 * 1024 * 1024;
    private const int MaxRedirectHops = 3;
    private const int CacheMinutes = 10;
    private const int MaxCacheEntries = 200;
    private const int MaxCacheBytes = 8 * 1024 * 1024;
    private const int MaxRecentErrors = 10;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<MusicHoarderzHttpClient> _logger;
    private readonly string? _baseUrlOverride;
    private readonly string? _countryOverride;
    private readonly Func<string, CancellationToken, Task<IPAddress[]>> _hostAddressResolver;
    private readonly ConcurrentDictionary<string, CoverCacheEntry> _cache = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<ProviderError> _recentErrors = new();
    private readonly ConcurrentDictionary<string, AlbumSnapshot> _albums = new(StringComparer.Ordinal);
    private ProviderHealth _health = new(false, new ProviderError("NotChecked", "No lookup has completed."));

    /// <summary>
    /// Initializes a new instance of the <see cref="MusicHoarderzHttpClient"/> class.
    /// </summary>
    /// <param name="httpClientFactory">HTTP client factory.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="baseUrlOverride">Optional base URL override (used by tests).</param>
    /// <param name="countryOverride">Optional country override (used by tests).</param>
    /// <param name="hostAddressResolver">Optional DNS resolver for offline tests.</param>
    public MusicHoarderzHttpClient(
        IHttpClientFactory httpClientFactory,
        ILogger<MusicHoarderzHttpClient> logger,
        string? baseUrlOverride = null,
        string? countryOverride = null,
        Func<string, CancellationToken, Task<IPAddress[]>>? hostAddressResolver = null)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _baseUrlOverride = baseUrlOverride;
        _countryOverride = countryOverride;
        _hostAddressResolver = hostAddressResolver ?? Dns.GetHostAddressesAsync;
    }

    /// <summary>
    /// Gets a snapshot of recent structured errors (newest last, bounded).
    /// </summary>
    public IReadOnlyList<ProviderError> RecentErrors => _recentErrors.ToArray();

    /// <summary>Gets the last completed operation status.</summary>
    public ProviderHealth Health => Volatile.Read(ref _health);

    /// <inheritdoc />
    public async Task<IReadOnlyList<NormalizedCoverResult>> SearchAsync(CoverSearchQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(query.Title) && string.IsNullOrWhiteSpace(query.Artist))
        {
            return Array.Empty<NormalizedCoverResult>();
        }

        if (query.Title.Length > 1024 || query.Artist.Length > 1024)
        {
            RecordError(new ProviderError("InvalidQuery", "Search input exceeds the length limit."));
            return Array.Empty<NormalizedCoverResult>();
        }

        var configuration = Plugin.Instance?.Configuration;
        if (_baseUrlOverride is null && configuration is not null
            && (!configuration.Enabled || !configuration.MusicHoarderz.Enabled || !configuration.AllowManualSearch))
        {
            RecordError(new ProviderError("Disabled", "Provider search is disabled."));
            return Array.Empty<NormalizedCoverResult>();
        }

        var baseUrl = ResolveBaseUrl();
        if (!IsAllowedBaseUrl(baseUrl))
        {
            RecordError(new ProviderError("InvalidBaseUrl", "Configured MusicHoarderz base URL is not an allowed https endpoint."));
            LogBaseUrlRejected(_logger);
            return Array.Empty<NormalizedCoverResult>();
        }

        if (IsOfficialCovHost(new Uri(baseUrl)))
        {
            RecordError(new ProviderError("ManualPickerRequired", "Use the official COV website to choose artwork. Its internal API is not used."));
            return Array.Empty<NormalizedCoverResult>();
        }

        var country = ResolveCountry(query).ToUpperInvariant();
        if (country.Length != 2 || country.Any(static letter => letter < 'A' || letter > 'Z'))
        {
            RecordError(new ProviderError("InvalidCountry", "Country must be a two-letter ISO country code."));
            return Array.Empty<NormalizedCoverResult>();
        }

        var cacheKey = BuildCacheKey(query, baseUrl, country);
        if (_cache.TryGetValue(cacheKey, out var cached) && cached.ExpiresAtUtc > DateTimeOffset.UtcNow)
        {
            Volatile.Write(ref _health, new ProviderHealth(true, null));
            return cached.Results;
        }
        else if (cached is not null)
        {
            _ = _cache.TryRemove(cacheKey, out _);
        }

        var results = await FetchAsync(baseUrl, query, country, cancellationToken).ConfigureAwait(false);
        if (results is not null)
        {
            Volatile.Write(ref _health, new ProviderHealth(true, null));
            StoreInCache(cacheKey, results);
            return results;
        }

        return Array.Empty<NormalizedCoverResult>();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<NormalizedAlbumResult>> SearchAlbumsAsync(CoverSearchQuery query, CancellationToken cancellationToken)
    {
        var covers = await SearchAsync(query, cancellationToken).ConfigureAwait(false);
        var albums = covers.Select(cover => new NormalizedAlbumResult(
            AlbumIdentifier(cover),
            cover.Title,
            cover.Artist,
            cover.Year,
            cover.ProviderIds ?? new Dictionary<string, string>(),
            cover.Url)).ToArray();
        lock (_albums)
        {
            foreach (var expired in _albums.Where(static pair => pair.Value.Expires <= DateTimeOffset.UtcNow))
            {
                _ = _albums.TryRemove(expired.Key, out _);
            }

            foreach (var album in albums)
            {
                var size = JsonSerializer.SerializeToUtf8Bytes(album).Length * 2;
                if (size > MaxCacheBytes)
                {
                    continue;
                }

                _ = _albums.TryRemove(album.Id, out _);
                while (_albums.Count >= 500 || _albums.Values.Sum(static snapshot => snapshot.SizeBytes) + size > MaxCacheBytes)
                {
                    _ = _albums.TryRemove(_albums.OrderBy(static pair => pair.Value.Expires).First().Key, out _);
                }

                _albums[album.Id] = new AlbumSnapshot(album, DateTimeOffset.UtcNow.AddMinutes(CacheMinutes), size);
            }
        }

        return albums;
    }

    /// <inheritdoc />
    public Task<NormalizedAlbumResult?> GetAlbumAsync(string id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_albums.TryGetValue(id, out var snapshot) && snapshot.Expires > DateTimeOffset.UtcNow)
        {
            return Task.FromResult<NormalizedAlbumResult?>(snapshot.Album);
        }

        _ = _albums.TryRemove(id, out _);
        return Task.FromResult<NormalizedAlbumResult?>(null);
    }

    private string AlbumIdentifier(NormalizedCoverResult cover)
    {
        var identity = JsonSerializer.SerializeToUtf8Bytes(new
        {
            Endpoint = ResolveBaseUrl(),
            cover.Url,
            cover.Source,
            cover.Title,
            cover.Artist,
            cover.Year,
            ProviderIds = cover.ProviderIds?.OrderBy(static pair => pair.Key, StringComparer.Ordinal).ToArray(),
        });
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(identity));
    }

    private string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(_baseUrlOverride))
        {
            return _baseUrlOverride.Trim();
        }

        return Plugin.Instance?.Configuration?.MusicHoarderz?.BaseUrl?.Trim() ?? DefaultBaseUrl;
    }

    private string ResolveCountry(CoverSearchQuery query)
    {
        if (!string.IsNullOrWhiteSpace(query.Country))
        {
            return query.Country.Trim();
        }

        if (!string.IsNullOrWhiteSpace(_countryOverride))
        {
            return _countryOverride.Trim();
        }

        return Plugin.Instance?.Configuration?.MusicHoarderz?.Country?.Trim() ?? "DE";
    }

    private static string BuildCacheKey(CoverSearchQuery query, string baseUrl, string country)
    {
        return string.Join(
            "\u0000",
            baseUrl,
            query.Title.Trim().ToLowerInvariant(),
            query.Artist.Trim().ToLowerInvariant(),
            query.Year?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            country.ToLowerInvariant());
    }

    private static bool IsAllowedBaseUrl(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return NetworkGuard.IsPublicHttps(uri) && string.IsNullOrEmpty(uri.Query);
    }

    private async Task<IReadOnlyList<NormalizedCoverResult>?> FetchAsync(
        string baseUrl,
        CoverSearchQuery query,
        string country,
        CancellationToken cancellationToken)
    {
        var requestUri = BuildSearchUri(baseUrl, query, country);
        if (requestUri is null)
        {
            RecordError(new ProviderError("InvalidBaseUrl", "Configured MusicHoarderz base URL is not an allowed https endpoint."));
            return null;
        }

        using var client = _httpClientFactory.CreateClient(HttpClientName);
        client.Timeout = Timeout.InfiniteTimeSpan;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));
        var requestToken = timeout.Token;

        try
        {
            var current = requestUri;
            for (var hop = 0; hop <= MaxRedirectHops; hop++)
            {
                requestToken.ThrowIfCancellationRequested();
                var addresses = await _hostAddressResolver(current.IdnHost, requestToken).ConfigureAwait(false);
                if (addresses.Length == 0 || addresses.Any(NetworkGuard.IsPrivateOrLoopback))
                {
                    RecordError(new ProviderError("HostBlocked", "Remote endpoint was blocked by network policy."));
                    return null;
                }

                using var request = new HttpRequestMessage(HttpMethod.Get, current);
                request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, requestToken).ConfigureAwait(false);
                if (IsRedirect(response.StatusCode) && response.Headers.Location is not null)
                {
                    var next = new Uri(current, response.Headers.Location);
                    if (!NetworkGuard.IsPublicHttps(next) || IsOfficialCovHost(next))
                    {
                        RecordError(new ProviderError("RedirectBlocked", "MusicHoarderz redirect target was blocked."));
                        LogRedirectBlocked(_logger);
                        return null;
                    }

                    current = next;
                    continue;
                }

                return await ReadResultsAsync(response, requestToken).ConfigureAwait(false);
            }

            RecordError(new ProviderError("RedirectBlocked", "MusicHoarderz redirect chain exceeded the hop limit."));
            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            RecordError(new ProviderError("Timeout", "MusicHoarderz request timed out."));
            LogRequestTimeout(_logger);
            return null;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or System.Net.Sockets.SocketException)
        {
            RecordError(new ProviderError("NetworkError", "MusicHoarderz request failed."));
            LogRequestFailed(_logger);
            return null;
        }
    }

    private async Task<IReadOnlyList<NormalizedCoverResult>?> ReadResultsAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (response.StatusCode == (HttpStatusCode)429)
        {
            RecordError(new ProviderError("RateLimited", "MusicHoarderz rate limit hit."));
            LogRateLimited(_logger);
            return null;
        }

        if ((int)response.StatusCode >= 500)
        {
            RecordError(new ProviderError("ServerError", "MusicHoarderz server error."));
            LogServerError(_logger, (int)response.StatusCode);
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            RecordError(new ProviderError("HttpError", "MusicHoarderz request failed."));
            LogHttpError(_logger, (int)response.StatusCode);
            return null;
        }

        if (response.Content.Headers.ContentLength > MaxResponseBytes)
        {
            RecordError(new ProviderError("ResponseTooLarge", "MusicHoarderz response exceeded the size limit."));
            LogResponseTooLarge(_logger, MaxResponseBytes + 1);
            return null;
        }

        var mime = response.Content.Headers.ContentType?.MediaType;
        if (mime is null || !(string.Equals(mime, "application/json", StringComparison.OrdinalIgnoreCase)
            || mime.EndsWith("+json", StringComparison.OrdinalIgnoreCase)))
        {
            RecordError(new ProviderError("InvalidPayload", "MusicHoarderz response was not JSON."));
            return null;
        }

        byte[] body;
        try
        {
            body = await BoundedHttpContent.ReadAsync(response.Content, MaxResponseBytes, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidDataException)
        {
            RecordError(new ProviderError("ResponseTooLarge", "MusicHoarderz response exceeded the size limit."));
            LogResponseTooLarge(_logger, MaxResponseBytes + 1);
            return null;
        }

        try
        {
            return ParseResults(body);
        }
        catch (JsonException)
        {
            RecordError(new ProviderError("InvalidPayload", "MusicHoarderz response was not valid JSON."));
            LogInvalidPayload(_logger);
            return null;
        }
        catch (InvalidDataException)
        {
            RecordError(new ProviderError("InvalidPayload", "MusicHoarderz response had an unexpected shape."));
            LogInvalidPayload(_logger);
            return null;
        }
    }

    private static Uri? BuildSearchUri(string baseUrl, CoverSearchQuery query, string country)
    {
        var trimmed = baseUrl.TrimEnd('/');
        var builder = new StringBuilder(trimmed);
        _ = builder.Append("/api/covers/search?title=").Append(Uri.EscapeDataString(query.Title.Trim()));
        _ = builder.Append("&artist=").Append(Uri.EscapeDataString(query.Artist.Trim()));
        _ = builder.Append("&country=").Append(Uri.EscapeDataString(country.Trim()));
        if (query.Year.HasValue)
        {
            _ = builder.Append("&year=").Append(query.Year.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (!Uri.TryCreate(builder.ToString(), UriKind.Absolute, out var uri))
        {
            return null;
        }

        return NetworkGuard.IsPublicHttps(uri) ? uri : null;
    }

    private static bool IsRedirect(HttpStatusCode statusCode)
    {
        return statusCode == HttpStatusCode.MovedPermanently
            || statusCode == HttpStatusCode.Found
            || statusCode == HttpStatusCode.SeeOther
            || statusCode == HttpStatusCode.TemporaryRedirect
            || (int)statusCode == 308;
    }

    private static List<NormalizedCoverResult> ParseResults(byte[] body)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var items = root;
        if (root.ValueKind == JsonValueKind.Object)
        {
            items = default;
            foreach (var key in new[] { "results", "covers", "items", "data" })
            {
                if (root.TryGetProperty(key, out var array) && array.ValueKind == JsonValueKind.Array)
                {
                    items = array;
                    break;
                }
            }

            if (items.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException("Missing result array.");
            }
        }
        else if (root.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("Missing result array.");
        }

        var results = new List<NormalizedCoverResult>();
        var urls = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items.EnumerateArray().Take(100))
        {
            var parsed = ParseItem(item);
            if (parsed is not null && urls.Add(parsed.Url))
            {
                results.Add(parsed);
            }
        }

        return results;
    }

    private static NormalizedCoverResult? ParseItem(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var url = ReadString(item, "url", "coverUrl", "cover_url", "imageUrl", "image_url", "thumbnailUrl", "thumbnail_url");
        if (string.IsNullOrWhiteSpace(url)
            || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
            || !NetworkGuard.IsPublicHttps(uri))
        {
            return null;
        }

        return new NormalizedCoverResult(
            url.Trim(),
            ReadInt(item, "width", "w"),
            ReadInt(item, "height", "h"),
            ReadString(item, "source", "provider") is string source && !string.IsNullOrWhiteSpace(source) ? source.Trim() : "musichoarderz",
            ReadString(item, "mimeType", "contentType", "mime"),
            0,
            "unscored",
            ReadString(item, "title", "name", "album"),
            ReadString(item, "artist", "albumArtist", "artistName"),
            ReadInt(item, "year", "releaseYear"),
            ReadProviderIds(item),
            ReadInt(item, "sizeBytes", "size"));
    }

    private static Dictionary<string, string> ReadProviderIds(JsonElement item)
    {
        var ids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in item.EnumerateObject())
        {
            if (!(string.Equals(property.Name, "providerIds", StringComparison.OrdinalIgnoreCase)
                || string.Equals(property.Name, "externalIds", StringComparison.OrdinalIgnoreCase))
                || property.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            foreach (var id in property.Value.EnumerateObject().Take(16))
            {
                if (id.Name.Length <= 64 && id.Value.ValueKind == JsonValueKind.String
                    && id.Value.GetString() is string value && !string.IsNullOrWhiteSpace(value) && value.Length <= 512)
                {
                    ids[id.Name] = value.Trim();
                }
            }
        }

        return ids;
    }

    private static string? ReadString(JsonElement item, params string[] names)
    {
        foreach (var name in names)
        {
            if (item.TryGetProperty(name, out var value))
            {
                if (value.ValueKind == JsonValueKind.String)
                {
                    var text = value.GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text;
                    }
                }
            }
            else
            {
                foreach (var property in item.EnumerateObject())
                {
                    if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)
                        && property.Value.ValueKind == JsonValueKind.String)
                    {
                        var text = property.Value.GetString();
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            return text;
                        }
                    }
                }
            }
        }

        return null;
    }

    private static int? ReadInt(JsonElement item, params string[] names)
    {
        foreach (var name in names)
        {
            var found = false;
            JsonElement value = default;
            if (item.TryGetProperty(name, out var direct))
            {
                value = direct;
                found = true;
            }
            else
            {
                foreach (var property in item.EnumerateObject())
                {
                    if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        value = property.Value;
                        found = true;
                        break;
                    }
                }
            }

            if (!found)
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
            {
                return number;
            }

            if (value.ValueKind == JsonValueKind.String
                && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private void RecordError(ProviderError error)
    {
        Volatile.Write(ref _health, new ProviderHealth(false, error));
        _recentErrors.Enqueue(error);
        while (_recentErrors.Count > MaxRecentErrors && _recentErrors.TryDequeue(out _))
        {
        }
    }

    private void StoreInCache(string key, IReadOnlyList<NormalizedCoverResult> results)
    {
        lock (_cache)
        {
            foreach (var pair in _cache.Where(static pair => pair.Value.ExpiresAtUtc <= DateTimeOffset.UtcNow))
            {
                _ = _cache.TryRemove(pair.Key, out _);
            }

            var entry = new CoverCacheEntry(results, DateTimeOffset.UtcNow.AddMinutes(CacheMinutes));
            if (entry.SizeBytes > MaxCacheBytes)
            {
                return;
            }

            _ = _cache.TryRemove(key, out _);
            while (_cache.Count >= MaxCacheEntries || _cache.Values.Sum(static cached => cached.SizeBytes) + entry.SizeBytes > MaxCacheBytes)
            {
                var oldest = _cache.OrderBy(static pair => pair.Value.ExpiresAtUtc).First();
                _ = _cache.TryRemove(oldest.Key, out _);
            }

            _cache[key] = entry;
        }
    }

    /// <summary>Identifies the official picker, which has no supported server search API.</summary>
    /// <param name="uri">An absolute endpoint.</param>
    /// <returns>Whether the endpoint belongs to the official COV website.</returns>
    public static bool IsOfficialCovHost(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        var host = uri.IdnHost.TrimEnd('.');
        return string.Equals(host, "covers.musichoarders.xyz", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".covers.musichoarders.xyz", StringComparison.OrdinalIgnoreCase);
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "MusicHoarderz base URL rejected: not an allowed https endpoint.")]
    private static partial void LogBaseUrlRejected(ILogger logger);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "MusicHoarderz redirect blocked.")]
    private static partial void LogRedirectBlocked(ILogger logger);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "MusicHoarderz rate limit hit.")]
    private static partial void LogRateLimited(ILogger logger);

    [LoggerMessage(EventId = 4, Level = LogLevel.Warning, Message = "MusicHoarderz server error {StatusCode}.")]
    private static partial void LogServerError(ILogger logger, int statusCode);

    [LoggerMessage(EventId = 5, Level = LogLevel.Warning, Message = "MusicHoarderz HTTP error {StatusCode}.")]
    private static partial void LogHttpError(ILogger logger, int statusCode);

    [LoggerMessage(EventId = 6, Level = LogLevel.Warning, Message = "MusicHoarderz response exceeded the size limit ({Bytes} bytes).")]
    private static partial void LogResponseTooLarge(ILogger logger, int bytes);

    [LoggerMessage(EventId = 7, Level = LogLevel.Warning, Message = "MusicHoarderz request failed.")]
    private static partial void LogRequestFailed(ILogger logger);

    [LoggerMessage(EventId = 8, Level = LogLevel.Warning, Message = "MusicHoarderz request timed out.")]
    private static partial void LogRequestTimeout(ILogger logger);

    [LoggerMessage(EventId = 9, Level = LogLevel.Warning, Message = "MusicHoarderz response payload invalid.")]
    private static partial void LogInvalidPayload(ILogger logger);

    private sealed record AlbumSnapshot(NormalizedAlbumResult Album, DateTimeOffset Expires, int SizeBytes);

    private sealed class CoverCacheEntry
    {
        public CoverCacheEntry(IReadOnlyList<NormalizedCoverResult> results, DateTimeOffset expiresAtUtc)
        {
            Results = results;
            ExpiresAtUtc = expiresAtUtc;
            SizeBytes = JsonSerializer.SerializeToUtf8Bytes(results).Length * 2;
        }

        public IReadOnlyList<NormalizedCoverResult> Results { get; }

        public int SizeBytes { get; }

        public DateTimeOffset ExpiresAtUtc { get; }
    }
}
