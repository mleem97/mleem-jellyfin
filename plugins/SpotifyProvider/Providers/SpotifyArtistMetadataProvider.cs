using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.SpotifyProvider.Services;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SpotifyProvider.Providers;

/// <summary>
/// Remote metadata provider for MusicArtist backed by Spotify Web API.
/// </summary>
public sealed class SpotifyArtistMetadataProvider : IRemoteMetadataProvider<MusicArtist, ArtistInfo>, IHasOrder
{
    private readonly SpotifyApiClient _apiClient;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SpotifyArtistMetadataProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SpotifyArtistMetadataProvider"/> class.
    /// </summary>
    /// <param name="apiClient">Spotify API client.</param>
    /// <param name="httpClientFactory">HTTP client factory.</param>
    /// <param name="logger">Logger.</param>
    public SpotifyArtistMetadataProvider(SpotifyApiClient apiClient, IHttpClientFactory httpClientFactory, ILogger<SpotifyArtistMetadataProvider> logger)
    {
        _apiClient = apiClient;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Spotify";

    /// <inheritdoc />
    public int Order => 3;

    /// <inheritdoc />
    public async Task<MetadataResult<MusicArtist>> GetMetadata(ArtistInfo info, CancellationToken cancellationToken)
    {
        var result = new MetadataResult<MusicArtist> { HasMetadata = false };
        var config = Plugin.Instance?.Configuration;
        if (config is null || !config.EnableMusicMetadata || string.IsNullOrWhiteSpace(config.ClientId))
        {
            return result;
        }

        try
        {
            SpotifyArtistDto? artistDto = null;
            if (info.ProviderIds.TryGetValue("Spotify", out var spotifyId) && !string.IsNullOrWhiteSpace(spotifyId))
            {
                artistDto = await _apiClient.GetArtistAsync(spotifyId, cancellationToken).ConfigureAwait(false);
            }

            if (artistDto is null)
            {
                var query = "artist:" + (info.Name ?? string.Empty).Trim();
                var search = await _apiClient.SearchAsync(query, "artist", config.Market, 5, cancellationToken).ConfigureAwait(false);
                artistDto = search?.Artists?.Items?.FirstOrDefault();
            }

            if (artistDto is null)
            {
                return result;
            }

            var artist = new MusicArtist
            {
                Name = artistDto.Name,
            };

            if (artistDto.Genres is not null && artistDto.Genres.Count > 0)
            {
                artist.Genres = artistDto.Genres.ToArray();
            }

            artist.ProviderIds["Spotify"] = artistDto.Id;

            result.Item = artist;
            result.HasMetadata = true;
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Spotify metadata lookup failed for artist {Name}", info.Name);
            return result;
        }
    }

    /// <inheritdoc />
    public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(ArtistInfo searchInfo, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration;
        if (config is null || !config.EnableMusicMetadata || string.IsNullOrWhiteSpace(config.ClientId))
        {
            return Array.Empty<RemoteSearchResult>();
        }

        try
        {
            var query = "artist:" + (searchInfo.Name ?? string.Empty).Trim();
            var search = await _apiClient.SearchAsync(query, "artist", config.Market, 10, cancellationToken).ConfigureAwait(false);
            if (search?.Artists?.Items is null)
            {
                return Array.Empty<RemoteSearchResult>();
            }

            return search.Artists.Items.Select(item => new RemoteSearchResult
            {
                Name = item.Name,
                SearchProviderName = Name,
                ImageUrl = item.Images?.FirstOrDefault()?.Url,
                ProviderIds = { ["Spotify"] = item.Id },
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Spotify search failed for artist {Name}", searchInfo.Name);
            return Array.Empty<RemoteSearchResult>();
        }
    }

    /// <inheritdoc />
    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("spotify");
        return client.GetAsync(url, cancellationToken);
    }
}
