using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.SpotifyProvider.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SpotifyProvider.Providers;

/// <summary>
/// Remote image provider backed by the Spotify Web API.
/// </summary>
public sealed class SpotifyImageProvider : IRemoteImageProvider, IHasOrder
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly SpotifyAuthService _auth;
    private readonly ILogger<SpotifyImageProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SpotifyImageProvider"/> class.
    /// </summary>
    /// <param name="httpClientFactory">HTTP client factory.</param>
    /// <param name="auth">Spotify auth service.</param>
    /// <param name="logger">Logger.</param>
    public SpotifyImageProvider(IHttpClientFactory httpClientFactory, SpotifyAuthService auth, ILogger<SpotifyImageProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _auth = auth;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Spotify";

    /// <inheritdoc />
    public int Order => 3;

    /// <inheritdoc />
    public bool Supports(BaseItem item) => item is MusicAlbum or MusicArtist;

    /// <inheritdoc />
    public IEnumerable<ImageType> GetSupportedImages(BaseItem item) => new[] { ImageType.Primary };

    /// <inheritdoc />
    public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration;
        if (config is null || !config.EnableImageLookup)
        {
            return Array.Empty<RemoteImageInfo>();
        }

        try
        {
            var token = await _auth.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
            var images = item is MusicAlbum album
                ? await SearchAlbumImagesAsync(album, token, config.Market, cancellationToken).ConfigureAwait(false)
                : await SearchArtistImagesAsync(item.Name, token, config.Market, cancellationToken).ConfigureAwait(false);
            return images;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Spotify cover lookup failed for {Name}", item.Name);
            return Array.Empty<RemoteImageInfo>();
        }
    }

    /// <inheritdoc />
    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("spotify");
        return client.GetAsync(url, cancellationToken);
    }

    private async Task<IReadOnlyList<RemoteImageInfo>> SearchAlbumImagesAsync(MusicAlbum album, string token, string market, CancellationToken cancellationToken)
    {
        var query = "album:" + (album.Name ?? string.Empty);
        if (!string.IsNullOrWhiteSpace(album.AlbumArtist))
        {
            query += " artist:" + album.AlbumArtist;
        }

        var url = "https://api.spotify.com/v1/search?q=" + Uri.EscapeDataString(query)
            + "&type=album&market=" + Uri.EscapeDataString(market)
            + "&limit=5";

        var payload = await GetJsonAsync<SpotifySearchResponse>(url, token, cancellationToken).ConfigureAwait(false);
        var result = new List<RemoteImageInfo>();
        foreach (var a in payload?.Albums?.Items ?? Enumerable.Empty<SpotifyAlbum>())
        {
            var best = a.Images?.OrderByDescending(i => i.Width ?? 0).FirstOrDefault();
            if (best is null || string.IsNullOrWhiteSpace(best.Url))
            {
                continue;
            }

            result.Add(new RemoteImageInfo
            {
                ProviderName = Name,
                Url = best.Url,
                Width = best.Width,
                Height = best.Height,
                Type = ImageType.Primary,
            });
        }

        return result;
    }

    private async Task<IReadOnlyList<RemoteImageInfo>> SearchArtistImagesAsync(string? artist, string token, string market, CancellationToken cancellationToken)
    {
        var url = "https://api.spotify.com/v1/search?q=" + Uri.EscapeDataString("artist:" + (artist ?? string.Empty))
            + "&type=artist&market=" + Uri.EscapeDataString(market)
            + "&limit=5";

        var payload = await GetJsonAsync<SpotifyArtistSearchResponse>(url, token, cancellationToken).ConfigureAwait(false);
        var result = new List<RemoteImageInfo>();
        foreach (var a in payload?.Artists?.Items ?? Enumerable.Empty<SpotifyArtist>())
        {
            var best = a.Images?.OrderByDescending(i => i.Width ?? 0).FirstOrDefault();
            if (best is null || string.IsNullOrWhiteSpace(best.Url))
            {
                continue;
            }

            result.Add(new RemoteImageInfo
            {
                ProviderName = Name,
                Url = best.Url,
                Width = best.Width,
                Height = best.Height,
                Type = ImageType.Primary,
            });
        }

        return result;
    }

    private async Task<T?> GetJsonAsync<T>(string url, string token, CancellationToken cancellationToken)
        where T : class
    {
        var client = _httpClientFactory.CreateClient("spotify");
        client.Timeout = TimeSpan.FromSeconds(20);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            _auth.Invalidate();
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private sealed class SpotifySearchResponse
    {
        [JsonPropertyName("albums")]
        public SpotifyAlbumPage? Albums { get; set; }
    }

    private sealed class SpotifyAlbumPage
    {
        [JsonPropertyName("items")]
        public List<SpotifyAlbum> Items { get; set; } = new();
    }

    private sealed class SpotifyAlbum
    {
        [JsonPropertyName("images")]
        public List<SpotifyImage>? Images { get; set; }
    }

    private sealed class SpotifyArtistSearchResponse
    {
        [JsonPropertyName("artists")]
        public SpotifyArtistPage? Artists { get; set; }
    }

    private sealed class SpotifyArtistPage
    {
        [JsonPropertyName("items")]
        public List<SpotifyArtist> Items { get; set; } = new();
    }

    private sealed class SpotifyArtist
    {
        [JsonPropertyName("images")]
        public List<SpotifyImage>? Images { get; set; }
    }

    private sealed class SpotifyImage
    {
        [JsonPropertyName("url")]
        public string Url { get; set; } = string.Empty;

        [JsonPropertyName("width")]
        public int? Width { get; set; }

        [JsonPropertyName("height")]
        public int? Height { get; set; }
    }
}
