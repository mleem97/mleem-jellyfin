using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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
public sealed partial class MusicHoarderzHttpClient : ICoverProvider
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
    private const int MaxRecentErrors = 10;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<MusicHoarderzHttpClient> _logger;
    private readonly string? _baseUrlOverride;
    private readonly string? _countryOverride;
    private readonly ConcurrentDictionary<string, CoverCacheEntry> _cache = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<ProviderError> _recentErrors = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="MusicHoarderzHttpClient"/> class.
    /// </summary>
    /// <param name="httpClientFactory">HTTP client factory.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="baseUrlOverride">Optional base URL override (used by tests).</param>
    /// <param name="countryOverride">Optional country override (used by tests).</param>
    public MusicHoarderzHttpClient(
        IHttpClientFactory httpClientFactory,
        ILogger<MusicHoarderzHttpClient> logger,
        string? baseUrlOverride = null,
        string? countryOverride = null)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _baseUrlOverride = baseUrlOverride;
        _countryOverride = countryOverride;
    }

    /// <summary>
    /// Gets a snapshot of recent structured errors (newest last, bounded).
    /// </summary>
    public IReadOnlyList<ProviderError> RecentErrors => _recentErrors.ToArray();

    /// <inheritdoc />
    public async Task<IReadOnlyList<NormalizedCoverResult>> SearchAsync(CoverSearchQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var baseUrl = ResolveBaseUrl();
        if (!IsAllowedBaseUrl(baseUrl))
        {
            RecordError(new ProviderError("InvalidBaseUrl", "Configured MusicHoarderz base URL is not an allowed https endpoint."));
            LogBaseUrlRejected(_logger);
            return Array.Empty<NormalizedCoverResult>();
        }

        var country = ResolveCountry(query);
        var cacheKey = BuildCacheKey(query, baseUrl, country);
        if (_cache.TryGetValue(cacheKey, out var cached) && cached.ExpiresAtUtc > DateTimeOffset.UtcNow)
        {
            return cached.Results;
        }
        else if (cached is not null)
        {
            _ = _cache.TryRemove(cacheKey, out _);
        }

        var results = await FetchAsync(baseUrl, query, country, cancellationToken).ConfigureAwait(false);
        if (results is not null)
        {
            StoreInCache(cacheKey, results);
            return results;
        }

        return Array.Empty<NormalizedCoverResult>();
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
            baseUrl.ToLowerInvariant(),
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

        return NetworkGuard.IsPublicHttps(uri);
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

        var client = _httpClientFactory.CreateClient(HttpClientName);
        client.Timeout = TimeSpan.FromSeconds(TimeoutSeconds);

        try
        {
            var current = requestUri;
            for (var hop = 0; hop <= MaxRedirectHops; hop++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var request = new HttpRequestMessage(HttpMethod.Get, current);
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                if (IsRedirect(response.StatusCode) && response.Headers.Location is not null)
                {
                    var next = new Uri(current, response.Headers.Location);
                    if (!NetworkGuard.IsPublicHttps(next))
                    {
                        RecordError(new ProviderError("RedirectBlocked", "MusicHoarderz redirect target was blocked."));
                        LogRedirectBlocked(_logger);
                        return null;
                    }

                    current = next;
                    continue;
                }

                return await ReadResultsAsync(response, cancellationToken).ConfigureAwait(false);
            }

            RecordError(new ProviderError("RedirectBlocked", "MusicHoarderz redirect chain exceeded the hop limit."));
            return null;
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            RecordError(new ProviderError("Timeout", "MusicHoarderz request timed out."));
            LogRequestTimeout(_logger, ex);
            return null;
        }
        catch (HttpRequestException ex)
        {
            RecordError(new ProviderError("NetworkError", "MusicHoarderz request failed."));
            LogRequestFailed(_logger, ex);
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

        byte[] body;
        try
        {
            body = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            RecordError(new ProviderError("Timeout", "MusicHoarderz request timed out."));
            LogRequestTimeout(_logger, ex);
            return null;
        }

        if (body.Length > MaxResponseBytes)
        {
            RecordError(new ProviderError("ResponseTooLarge", "MusicHoarderz response exceeded the size limit."));
            LogResponseTooLarge(_logger, body.Length);
            return null;
        }

        try
        {
            return ParseResults(body);
        }
        catch (JsonException ex)
        {
            RecordError(new ProviderError("InvalidPayload", "MusicHoarderz response was not valid JSON."));
            LogInvalidPayload(_logger, ex);
            return null;
        }
        catch (InvalidDataException ex)
        {
            RecordError(new ProviderError("InvalidPayload", "MusicHoarderz response had an unexpected shape."));
            LogInvalidPayload(_logger, ex);
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

    private static IReadOnlyList<NormalizedCoverResult> ParseResults(byte[] body)
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
                return Array.Empty<NormalizedCoverResult>();
            }
        }
        else if (root.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<NormalizedCoverResult>();
        }

        var results = new List<NormalizedCoverResult>();
        foreach (var item in items.EnumerateArray())
        {
            var parsed = ParseItem(item);
            if (parsed is not null)
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

        var url = ReadString(item, "url", "coverUrl", "imageUrl", "thumbnailUrl");
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
            ReadInt(item, "year", "releaseYear"));
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
        _recentErrors.Enqueue(error);
        while (_recentErrors.Count > MaxRecentErrors && _recentErrors.TryDequeue(out _))
        {
        }
    }

    private void StoreInCache(string key, IReadOnlyList<NormalizedCoverResult> results)
    {
        if (_cache.Count >= MaxCacheEntries)
        {
            foreach (var pair in _cache)
            {
                if (pair.Value.ExpiresAtUtc <= DateTimeOffset.UtcNow)
                {
                    _ = _cache.TryRemove(pair.Key, out _);
                }
            }
        }

        var entry = new CoverCacheEntry(results, DateTimeOffset.UtcNow.AddMinutes(CacheMinutes));
        _cache[key] = entry;
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
    private static partial void LogRequestFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 8, Level = LogLevel.Warning, Message = "MusicHoarderz request timed out.")]
    private static partial void LogRequestTimeout(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 9, Level = LogLevel.Warning, Message = "MusicHoarderz response payload invalid.")]
    private static partial void LogInvalidPayload(ILogger logger, Exception exception);

    private sealed class CoverCacheEntry
    {
        public CoverCacheEntry(IReadOnlyList<NormalizedCoverResult> results, DateTimeOffset expiresAtUtc)
        {
            Results = results;
            ExpiresAtUtc = expiresAtUtc;
        }

        public IReadOnlyList<NormalizedCoverResult> Results { get; }

        public DateTimeOffset ExpiresAtUtc { get; }
    }
}
