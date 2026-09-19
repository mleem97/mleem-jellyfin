using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MetadataDashboard.Services;

/// <summary>
/// Service providing structured search and lookup functionality against MusicBrainz WS/2 API.
/// </summary>
public sealed class MusicBrainzLookupService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<MusicBrainzLookupService> _logger;
    private static readonly SemaphoreSlim RateLimiter = new(1, 1);
    private static DateTime _lastRequestTime = DateTime.MinValue;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="MusicBrainzLookupService"/> class.
    /// </summary>
    public MusicBrainzLookupService(IHttpClientFactory httpClientFactory, ILogger<MusicBrainzLookupService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Searches MusicBrainz releases by album name and optional artist.
    /// </summary>
    public async Task<List<MusicBrainzReleaseMatch>> SearchReleasesAsync(string album, string? artist, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(album))
        {
            return new List<MusicBrainzReleaseMatch>();
        }

        var queryParts = new List<string>
        {
            $"release:\"{album.Trim()}\""
        };

        if (!string.IsNullOrWhiteSpace(artist))
        {
            queryParts.Add($"artist:\"{artist.Trim()}\"");
        }

        var luceneQuery = string.Join(" AND ", queryParts);
        var baseUrl = Plugin.Instance?.Configuration.MusicBrainzMirror ?? "https://musicbrainz.org";
        var url = $"{baseUrl.TrimEnd('/')}/ws/2/release/?query={Uri.EscapeDataString(luceneQuery)}&fmt=json&limit=15";

        var response = await ExecuteMusicBrainzQueryAsync<MusicBrainzReleaseSearchResponse>(url, cancellationToken).ConfigureAwait(false);
        if (response?.Releases == null)
        {
            return new List<MusicBrainzReleaseMatch>();
        }

        return response.Releases.Select(r => new MusicBrainzReleaseMatch
        {
            Id = r.Id ?? string.Empty,
            Title = r.Title ?? album,
            Artist = r.ArtistCredit != null && r.ArtistCredit.Count > 0 ? string.Join(", ", r.ArtistCredit.Select(a => a.Name)) : (artist ?? string.Empty),
            Date = r.Date,
            Country = r.Country,
            Status = r.Status,
            Score = r.Score,
            TrackCount = r.TrackCount,
            ReleaseGroupId = r.ReleaseGroup?.Id
        }).ToList();
    }

    /// <summary>
    /// Searches MusicBrainz artists by name.
    /// </summary>
    public async Task<List<MusicBrainzArtistMatch>> SearchArtistsAsync(string artistName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(artistName))
        {
            return new List<MusicBrainzArtistMatch>();
        }

        var baseUrl = Plugin.Instance?.Configuration.MusicBrainzMirror ?? "https://musicbrainz.org";
        var url = $"{baseUrl.TrimEnd('/')}/ws/2/artist/?query={Uri.EscapeDataString(artistName.Trim())}&fmt=json&limit=10";

        var response = await ExecuteMusicBrainzQueryAsync<MusicBrainzArtistSearchResponse>(url, cancellationToken).ConfigureAwait(false);
        if (response?.Artists == null)
        {
            return new List<MusicBrainzArtistMatch>();
        }

        return response.Artists.Select(a => new MusicBrainzArtistMatch
        {
            Id = a.Id ?? string.Empty,
            Name = a.Name ?? artistName,
            SortName = a.SortName,
            Type = a.Type,
            Country = a.Country,
            Disambiguation = a.Disambiguation,
            Score = a.Score
        }).ToList();
    }

    private async Task<T?> ExecuteMusicBrainzQueryAsync<T>(string url, CancellationToken cancellationToken)
        where T : class
    {
        await RateLimiter.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var delayMs = Plugin.Instance?.Configuration.RateLimitDelayMs ?? 1000;
            var elapsed = (DateTime.UtcNow - _lastRequestTime).TotalMilliseconds;
            if (elapsed < delayMs)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(delayMs - elapsed), cancellationToken).ConfigureAwait(false);
            }

            using var client = _httpClientFactory.CreateClient("MetadataDashboard");
            client.DefaultRequestHeaders.UserAgent.Clear();
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MetadataDashboard", "1.0.0"));
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            _lastRequestTime = DateTime.UtcNow;

            using var response = await client.GetAsync(new Uri(url), HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("MusicBrainz API query failed with status code {StatusCode}", response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "MusicBrainz request failed for URL: {Url}", url);
            return null;
        }
        finally
        {
            RateLimiter.Release();
        }
    }
}

public sealed class MusicBrainzReleaseMatch
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public string? Date { get; set; }
    public string? Country { get; set; }
    public string? Status { get; set; }
    public int Score { get; set; }
    public int TrackCount { get; set; }
    public string? ReleaseGroupId { get; set; }
}

public sealed class MusicBrainzArtistMatch
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? SortName { get; set; }
    public string? Type { get; set; }
    public string? Country { get; set; }
    public string? Disambiguation { get; set; }
    public int Score { get; set; }
}

internal sealed class MusicBrainzReleaseSearchResponse
{
    public List<MusicBrainzReleaseRaw>? Releases { get; set; }
}

internal sealed class MusicBrainzReleaseRaw
{
    public string? Id { get; set; }
    public string? Title { get; set; }
    public string? Status { get; set; }
    public string? Date { get; set; }
    public string? Country { get; set; }
    public int Score { get; set; }

    [JsonPropertyName("track-count")]
    public int TrackCount { get; set; }

    [JsonPropertyName("artist-credit")]
    public List<MusicBrainzArtistCredit>? ArtistCredit { get; set; }

    [JsonPropertyName("release-group")]
    public MusicBrainzReleaseGroupRaw? ReleaseGroup { get; set; }
}

internal sealed class MusicBrainzArtistCredit
{
    public string? Name { get; set; }
}

internal sealed class MusicBrainzReleaseGroupRaw
{
    public string? Id { get; set; }
}

internal sealed class MusicBrainzArtistSearchResponse
{
    public List<MusicBrainzArtistRaw>? Artists { get; set; }
}

internal sealed class MusicBrainzArtistRaw
{
    public string? Id { get; set; }
    public string? Name { get; set; }

    [JsonPropertyName("sort-name")]
    public string? SortName { get; set; }

    public string? Type { get; set; }
    public string? Country { get; set; }
    public string? Disambiguation { get; set; }
    public int Score { get; set; }
}
