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
/// Remote metadata provider for Audio tracks backed by Spotify Web API.
/// </summary>
public sealed class SpotifySongMetadataProvider : IRemoteMetadataProvider<Audio, SongInfo>, IHasOrder
{
    private readonly SpotifyApiClient _apiClient;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SpotifySongMetadataProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SpotifySongMetadataProvider"/> class.
    /// </summary>
    /// <param name="apiClient">Spotify API client.</param>
    /// <param name="httpClientFactory">HTTP client factory.</param>
    /// <param name="logger">Logger.</param>
    public SpotifySongMetadataProvider(SpotifyApiClient apiClient, IHttpClientFactory httpClientFactory, ILogger<SpotifySongMetadataProvider> logger)
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
    public async Task<MetadataResult<Audio>> GetMetadata(SongInfo info, CancellationToken cancellationToken)
    {
        var result = new MetadataResult<Audio> { HasMetadata = false };
        var config = Plugin.Instance?.Configuration;
        if (config is null || !config.EnableMusicMetadata || string.IsNullOrWhiteSpace(config.ClientId))
        {
            return result;
        }

        try
        {
            SpotifyTrackDto? trackDto = null;
            if (info.ProviderIds.TryGetValue("Spotify", out var spotifyId) && !string.IsNullOrWhiteSpace(spotifyId))
            {
                trackDto = await _apiClient.GetTrackAsync(spotifyId, config.Market, cancellationToken).ConfigureAwait(false);
            }

            if (trackDto is null)
            {
                var query = "track:" + (info.Name ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(info.Album))
                {
                    query += " album:" + info.Album.Trim();
                }

                var artist = info.Artists.Count > 0 ? info.Artists[0] : null;
                if (!string.IsNullOrWhiteSpace(artist))
                {
                    query += " artist:" + artist.Trim();
                }

                var search = await _apiClient.SearchAsync(query, "track", config.Market, 5, cancellationToken).ConfigureAwait(false);
                trackDto = search?.Tracks?.Items?.FirstOrDefault();
            }

            if (trackDto is null)
            {
                return result;
            }

            var audio = new Audio
            {
                Name = trackDto.Name,
                IndexNumber = trackDto.TrackNumber,
                ParentIndexNumber = trackDto.DiscNumber,
            };

            audio.ProviderIds["Spotify"] = trackDto.Id;
            if (trackDto.ExternalIds != null && trackDto.ExternalIds.TryGetValue("isrc", out var isrc))
            {
                audio.ProviderIds["ISRC"] = isrc;
            }

            result.Item = audio;
            result.HasMetadata = true;
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Spotify metadata lookup failed for track {Name}", info.Name);
            return result;
        }
    }

    /// <inheritdoc />
    public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(SongInfo searchInfo, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration;
        if (config is null || !config.EnableMusicMetadata || string.IsNullOrWhiteSpace(config.ClientId))
        {
            return Array.Empty<RemoteSearchResult>();
        }

        try
        {
            var query = "track:" + (searchInfo.Name ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(searchInfo.Album))
            {
                query += " album:" + searchInfo.Album.Trim();
            }

            var search = await _apiClient.SearchAsync(query, "track", config.Market, 10, cancellationToken).ConfigureAwait(false);
            if (search?.Tracks?.Items is null)
            {
                return Array.Empty<RemoteSearchResult>();
            }

            return search.Tracks.Items.Select(item => new RemoteSearchResult
            {
                Name = item.Name,
                SearchProviderName = Name,
                IndexNumber = item.TrackNumber,
                ParentIndexNumber = item.DiscNumber,
                ImageUrl = item.Album?.Images?.FirstOrDefault()?.Url,
                ProviderIds = { ["Spotify"] = item.Id },
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Spotify search failed for track {Name}", searchInfo.Name);
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
