using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
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
    private readonly SpotifyApiClient _apiClient;
    private readonly ILogger<SpotifyImageProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SpotifyImageProvider"/> class.
    /// </summary>
    /// <param name="httpClientFactory">HTTP client factory.</param>
    /// <param name="apiClient">Spotify API client.</param>
    /// <param name="logger">Logger.</param>
    public SpotifyImageProvider(IHttpClientFactory httpClientFactory, SpotifyApiClient apiClient, ILogger<SpotifyImageProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _apiClient = apiClient;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Spotify";

    /// <inheritdoc />
    public int Order => 3;

    /// <inheritdoc />
    public bool Supports(BaseItem item) => item is MusicAlbum or MusicArtist or Book or Audio;

    /// <inheritdoc />
    public IEnumerable<ImageType> GetSupportedImages(BaseItem item) => new[] { ImageType.Primary };

    /// <inheritdoc />
    public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration;
        if (config is null || !config.EnableImageLookup || string.IsNullOrWhiteSpace(config.ClientId))
        {
            return Array.Empty<RemoteImageInfo>();
        }

        try
        {
            if (item is MusicAlbum album)
            {
                return await GetAlbumImagesAsync(album, config.Market, cancellationToken).ConfigureAwait(false);
            }

            if (item is MusicArtist artist)
            {
                return await GetArtistImagesAsync(artist.Name, config.Market, cancellationToken).ConfigureAwait(false);
            }

            if (item is Book book)
            {
                return await GetAudiobookImagesAsync(book, config.Market, cancellationToken).ConfigureAwait(false);
            }

            if (item is Audio audio)
            {
                return await GetTrackImagesAsync(audio, config.Market, cancellationToken).ConfigureAwait(false);
            }

            return Array.Empty<RemoteImageInfo>();
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

    private async Task<IReadOnlyList<RemoteImageInfo>> GetAlbumImagesAsync(MusicAlbum album, string market, CancellationToken cancellationToken)
    {
        if (album.ProviderIds.TryGetValue("Spotify", out var spotifyId) && !string.IsNullOrWhiteSpace(spotifyId))
        {
            var details = await _apiClient.GetAlbumAsync(spotifyId, market, cancellationToken).ConfigureAwait(false);
            if (details?.Images != null && details.Images.Count > 0)
            {
                return MapImages(details.Images);
            }
        }

        var query = "album:" + (album.Name ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(album.AlbumArtist))
        {
            query += " artist:" + album.AlbumArtist.Trim();
        }

        var search = await _apiClient.SearchAsync(query, "album", market, 5, cancellationToken).ConfigureAwait(false);
        var list = new List<RemoteImageInfo>();
        foreach (var item in search?.Albums?.Items ?? Enumerable.Empty<SpotifyAlbumDto>())
        {
            if (item.Images != null)
            {
                list.AddRange(MapImages(item.Images));
            }
        }

        return list;
    }

    private async Task<IReadOnlyList<RemoteImageInfo>> GetArtistImagesAsync(string? artistName, string market, CancellationToken cancellationToken)
    {
        var query = "artist:" + (artistName ?? string.Empty).Trim();
        var search = await _apiClient.SearchAsync(query, "artist", market, 5, cancellationToken).ConfigureAwait(false);
        var list = new List<RemoteImageInfo>();
        foreach (var item in search?.Artists?.Items ?? Enumerable.Empty<SpotifyArtistDto>())
        {
            if (item.Images != null)
            {
                list.AddRange(MapImages(item.Images));
            }
        }

        return list;
    }

    private async Task<IReadOnlyList<RemoteImageInfo>> GetAudiobookImagesAsync(Book book, string market, CancellationToken cancellationToken)
    {
        if (book.ProviderIds.TryGetValue("Spotify", out var spotifyId) && !string.IsNullOrWhiteSpace(spotifyId))
        {
            var details = await _apiClient.GetAudiobookAsync(spotifyId, market, cancellationToken).ConfigureAwait(false);
            if (details?.Images != null && details.Images.Count > 0)
            {
                return MapImages(details.Images);
            }
        }

        var search = await _apiClient.SearchAsync((book.Name ?? string.Empty).Trim(), "audiobook", market, 5, cancellationToken).ConfigureAwait(false);
        var list = new List<RemoteImageInfo>();
        foreach (var item in search?.Audiobooks?.Items ?? Enumerable.Empty<SpotifyAudiobookDto>())
        {
            if (item.Images != null)
            {
                list.AddRange(MapImages(item.Images));
            }
        }

        return list;
    }

    private async Task<IReadOnlyList<RemoteImageInfo>> GetTrackImagesAsync(Audio audio, string market, CancellationToken cancellationToken)
    {
        if (audio.ProviderIds.TryGetValue("Spotify", out var spotifyId) && !string.IsNullOrWhiteSpace(spotifyId))
        {
            var details = await _apiClient.GetTrackAsync(spotifyId, market, cancellationToken).ConfigureAwait(false);
            if (details?.Album?.Images != null && details.Album.Images.Count > 0)
            {
                return MapImages(details.Album.Images);
            }
        }

        var query = "track:" + (audio.Name ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(audio.Album))
        {
            query += " album:" + audio.Album.Trim();
        }

        var search = await _apiClient.SearchAsync(query, "track", market, 5, cancellationToken).ConfigureAwait(false);
        var list = new List<RemoteImageInfo>();
        foreach (var item in search?.Tracks?.Items ?? Enumerable.Empty<SpotifyTrackDto>())
        {
            if (item.Album?.Images != null)
            {
                list.AddRange(MapImages(item.Album.Images));
            }
        }

        return list;
    }

    private List<RemoteImageInfo> MapImages(IEnumerable<SpotifyImageDto> images)
    {
        return images
            .Where(i => !string.IsNullOrWhiteSpace(i.Url))
            .OrderByDescending(i => i.Width ?? 0)
            .Select(i => new RemoteImageInfo
            {
                ProviderName = Name,
                Url = i.Url,
                Width = i.Width,
                Height = i.Height,
                Type = ImageType.Primary,
            })
            .ToList();
    }
}
