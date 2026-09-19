using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SpotifyProvider.Services;

/// <summary>
/// DTOs for Spotify Web API.
/// </summary>
public sealed class SpotifySearchResponse
{
    [JsonPropertyName("albums")]
    public SpotifyPagedList<SpotifyAlbumDto>? Albums { get; set; }

    [JsonPropertyName("artists")]
    public SpotifyPagedList<SpotifyArtistDto>? Artists { get; set; }

    [JsonPropertyName("audiobooks")]
    public SpotifyPagedList<SpotifyAudiobookDto>? Audiobooks { get; set; }

    [JsonPropertyName("shows")]
    public SpotifyPagedList<SpotifyShowDto>? Shows { get; set; }

    [JsonPropertyName("tracks")]
    public SpotifyPagedList<SpotifyTrackDto>? Tracks { get; set; }
}

/// <summary>
/// Paged list container.
/// </summary>
/// <typeparam name="T">Item type.</typeparam>
public sealed class SpotifyPagedList<T>
{
    [JsonPropertyName("items")]
    public List<T>? Items { get; set; }

    [JsonPropertyName("total")]
    public int Total { get; set; }
}

/// <summary>
/// Spotify Album DTO.
/// </summary>
public sealed class SpotifyAlbumDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("album_type")]
    public string? AlbumType { get; set; }

    [JsonPropertyName("release_date")]
    public string? ReleaseDate { get; set; }

    [JsonPropertyName("total_tracks")]
    public int TotalTracks { get; set; }

    [JsonPropertyName("genres")]
    public List<string>? Genres { get; set; }

    [JsonPropertyName("popularity")]
    public int? Popularity { get; set; }

    [JsonPropertyName("images")]
    public List<SpotifyImageDto>? Images { get; set; }

    [JsonPropertyName("artists")]
    public List<SpotifyArtistDto>? Artists { get; set; }

    [JsonPropertyName("external_ids")]
    public Dictionary<string, string>? ExternalIds { get; set; }
}

/// <summary>
/// Spotify Artist DTO.
/// </summary>
public sealed class SpotifyArtistDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("genres")]
    public List<string>? Genres { get; set; }

    [JsonPropertyName("popularity")]
    public int? Popularity { get; set; }

    [JsonPropertyName("images")]
    public List<SpotifyImageDto>? Images { get; set; }
}

/// <summary>
/// Spotify Audiobook DTO.
/// </summary>
public sealed class SpotifyAudiobookDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("html_description")]
    public string? HtmlDescription { get; set; }

    [JsonPropertyName("authors")]
    public List<SpotifyAuthorDto>? Authors { get; set; }

    [JsonPropertyName("narrators")]
    public List<SpotifyNarratorDto>? Narrators { get; set; }

    [JsonPropertyName("publisher")]
    public string? Publisher { get; set; }

    [JsonPropertyName("languages")]
    public List<string>? Languages { get; set; }

    [JsonPropertyName("edition")]
    public string? Edition { get; set; }

    [JsonPropertyName("total_chapters")]
    public int TotalChapters { get; set; }

    [JsonPropertyName("images")]
    public List<SpotifyImageDto>? Images { get; set; }

    [JsonPropertyName("external_ids")]
    public Dictionary<string, string>? ExternalIds { get; set; }
}

/// <summary>
/// Author DTO.
/// </summary>
public sealed class SpotifyAuthorDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// Narrator DTO.
/// </summary>
public sealed class SpotifyNarratorDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// Spotify Podcast/Show DTO.
/// </summary>
public sealed class SpotifyShowDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("publisher")]
    public string? Publisher { get; set; }

    [JsonPropertyName("languages")]
    public List<string>? Languages { get; set; }

    [JsonPropertyName("images")]
    public List<SpotifyImageDto>? Images { get; set; }
}

/// <summary>
/// Spotify Track DTO.
/// </summary>
public sealed class SpotifyTrackDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("track_number")]
    public int TrackNumber { get; set; }

    [JsonPropertyName("disc_number")]
    public int DiscNumber { get; set; }

    [JsonPropertyName("duration_ms")]
    public long DurationMs { get; set; }

    [JsonPropertyName("popularity")]
    public int? Popularity { get; set; }

    [JsonPropertyName("artists")]
    public List<SpotifyArtistDto>? Artists { get; set; }

    [JsonPropertyName("album")]
    public SpotifyAlbumDto? Album { get; set; }

    [JsonPropertyName("external_ids")]
    public Dictionary<string, string>? ExternalIds { get; set; }
}

/// <summary>
/// Spotify Image DTO.
/// </summary>
public sealed class SpotifyImageDto
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("width")]
    public int? Width { get; set; }

    [JsonPropertyName("height")]
    public int? Height { get; set; }
}

/// <summary>
/// Client for the Spotify Web API.
/// </summary>
public sealed class SpotifyApiClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly SpotifyAuthService _auth;
    private readonly ILogger<SpotifyApiClient> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SpotifyApiClient"/> class.
    /// </summary>
    /// <param name="httpClientFactory">HTTP client factory.</param>
    /// <param name="auth">Spotify auth service.</param>
    /// <param name="logger">Logger.</param>
    public SpotifyApiClient(IHttpClientFactory httpClientFactory, SpotifyAuthService auth, ILogger<SpotifyApiClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _auth = auth;
        _logger = logger;
    }

    /// <summary>
    /// Searches Spotify for items.
    /// </summary>
    /// <param name="query">Search query.</param>
    /// <param name="type">Types comma-separated (e.g. album,artist).</param>
    /// <param name="market">Market country code.</param>
    /// <param name="limit">Result limit.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Search response.</returns>
    public async Task<SpotifySearchResponse?> SearchAsync(string query, string type, string market, int limit = 10, CancellationToken cancellationToken = default)
    {
        var url = "https://api.spotify.com/v1/search?q=" + Uri.EscapeDataString(query)
            + "&type=" + Uri.EscapeDataString(type)
            + "&market=" + Uri.EscapeDataString(market)
            + "&limit=" + limit;

        return await GetJsonWithAuthAsync<SpotifySearchResponse>(url, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets album details by Spotify ID.
    /// </summary>
    /// <param name="id">Album ID.</param>
    /// <param name="market">Market country code.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Album details.</returns>
    public async Task<SpotifyAlbumDto?> GetAlbumAsync(string id, string market, CancellationToken cancellationToken = default)
    {
        var url = $"https://api.spotify.com/v1/albums/{Uri.EscapeDataString(id)}?market={Uri.EscapeDataString(market)}";
        return await GetJsonWithAuthAsync<SpotifyAlbumDto>(url, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets artist details by Spotify ID.
    /// </summary>
    /// <param name="id">Artist ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Artist details.</returns>
    public async Task<SpotifyArtistDto?> GetArtistAsync(string id, CancellationToken cancellationToken = default)
    {
        var url = $"https://api.spotify.com/v1/artists/{Uri.EscapeDataString(id)}";
        return await GetJsonWithAuthAsync<SpotifyArtistDto>(url, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets audiobook details by Spotify ID.
    /// </summary>
    /// <param name="id">Audiobook ID.</param>
    /// <param name="market">Market country code.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Audiobook details.</returns>
    public async Task<SpotifyAudiobookDto?> GetAudiobookAsync(string id, string market, CancellationToken cancellationToken = default)
    {
        var url = $"https://api.spotify.com/v1/audiobooks/{Uri.EscapeDataString(id)}?market={Uri.EscapeDataString(market)}";
        return await GetJsonWithAuthAsync<SpotifyAudiobookDto>(url, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets show / podcast details by Spotify ID.
    /// </summary>
    /// <param name="id">Show ID.</param>
    /// <param name="market">Market country code.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Show details.</returns>
    public async Task<SpotifyShowDto?> GetShowAsync(string id, string market, CancellationToken cancellationToken = default)
    {
        var url = $"https://api.spotify.com/v1/shows/{Uri.EscapeDataString(id)}?market={Uri.EscapeDataString(market)}";
        return await GetJsonWithAuthAsync<SpotifyShowDto>(url, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets track details by Spotify ID.
    /// </summary>
    /// <param name="id">Track ID.</param>
    /// <param name="market">Market country code.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Track details.</returns>
    public async Task<SpotifyTrackDto?> GetTrackAsync(string id, string market, CancellationToken cancellationToken = default)
    {
        var url = $"https://api.spotify.com/v1/tracks/{Uri.EscapeDataString(id)}?market={Uri.EscapeDataString(market)}";
        return await GetJsonWithAuthAsync<SpotifyTrackDto>(url, cancellationToken).ConfigureAwait(false);
    }

    private async Task<T?> GetJsonWithAuthAsync<T>(string url, CancellationToken cancellationToken)
        where T : class
    {
        var token = await _auth.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        var client = _httpClientFactory.CreateClient("spotify");

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            _logger.LogInformation("Received 401 Unauthorized from Spotify. Invalidating token and retrying once.");
            _auth.Invalidate();
            token = await _auth.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);

            using var retryRequest = new HttpRequestMessage(HttpMethod.Get, url);
            retryRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var retryResponse = await client.SendAsync(retryRequest, cancellationToken).ConfigureAwait(false);

            if (!retryResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning("Retry failed with status {StatusCode} for URL {Url}", retryResponse.StatusCode, url);
                return null;
            }

            return await retryResponse.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (response.StatusCode == (HttpStatusCode)429)
        {
            _logger.LogWarning("Spotify rate limit reached (HTTP 429). Retry-After: {RetryAfter}", response.Headers.RetryAfter?.Delta?.TotalSeconds);
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Spotify request to {Url} returned status {StatusCode}", url, response.StatusCode);
            return null;
        }

        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}
