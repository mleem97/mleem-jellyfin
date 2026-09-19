using System;
using System.Collections.Generic;
using System.Globalization;
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
/// Remote metadata provider for MusicAlbum backed by Spotify Web API.
/// </summary>
public sealed class SpotifyAlbumMetadataProvider : IRemoteMetadataProvider<MusicAlbum, AlbumInfo>, IHasOrder
{
    private readonly SpotifyApiClient _apiClient;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SpotifyAlbumMetadataProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SpotifyAlbumMetadataProvider"/> class.
    /// </summary>
    /// <param name="apiClient">Spotify API client.</param>
    /// <param name="httpClientFactory">HTTP client factory.</param>
    /// <param name="logger">Logger.</param>
    public SpotifyAlbumMetadataProvider(SpotifyApiClient apiClient, IHttpClientFactory httpClientFactory, ILogger<SpotifyAlbumMetadataProvider> logger)
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
    public async Task<MetadataResult<MusicAlbum>> GetMetadata(AlbumInfo info, CancellationToken cancellationToken)
    {
        var result = new MetadataResult<MusicAlbum> { HasMetadata = false };
        var config = Plugin.Instance?.Configuration;
        if (config is null || !config.EnableMusicMetadata || string.IsNullOrWhiteSpace(config.ClientId))
        {
            return result;
        }

        try
        {
            SpotifyAlbumDto? albumDto = null;
            if (info.ProviderIds.TryGetValue("Spotify", out var spotifyId) && !string.IsNullOrWhiteSpace(spotifyId))
            {
                albumDto = await _apiClient.GetAlbumAsync(spotifyId, config.Market, cancellationToken).ConfigureAwait(false);
            }

            if (albumDto is null)
            {
                var query = BuildSearchQuery(info);
                var search = await _apiClient.SearchAsync(query, "album", config.Market, 5, cancellationToken).ConfigureAwait(false);
                var first = search?.Albums?.Items?.FirstOrDefault();
                if (first is not null)
                {
                    albumDto = await _apiClient.GetAlbumAsync(first.Id, config.Market, cancellationToken).ConfigureAwait(false) ?? first;
                }
            }

            if (albumDto is null)
            {
                return result;
            }

            var album = new MusicAlbum
            {
                Name = albumDto.Name,
            };

            if (TryParseYear(albumDto.ReleaseDate, out var year, out var premiereDate))
            {
                album.ProductionYear = year;
                album.PremiereDate = premiereDate;
            }

            if (albumDto.Genres is not null && albumDto.Genres.Count > 0)
            {
                album.Genres = albumDto.Genres.ToArray();
            }

            album.ProviderIds["Spotify"] = albumDto.Id;
            if (albumDto.ExternalIds != null && albumDto.ExternalIds.TryGetValue("upc", out var upc))
            {
                album.ProviderIds["UPC"] = upc;
            }

            result.Item = album;
            result.HasMetadata = true;
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Spotify metadata lookup failed for album {Name}", info.Name);
            return result;
        }
    }

    /// <inheritdoc />
    public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(AlbumInfo searchInfo, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration;
        if (config is null || !config.EnableMusicMetadata || string.IsNullOrWhiteSpace(config.ClientId))
        {
            return Array.Empty<RemoteSearchResult>();
        }

        try
        {
            var query = BuildSearchQuery(searchInfo);
            var search = await _apiClient.SearchAsync(query, "album", config.Market, 10, cancellationToken).ConfigureAwait(false);
            if (search?.Albums?.Items is null)
            {
                return Array.Empty<RemoteSearchResult>();
            }

            var results = new List<RemoteSearchResult>();
            foreach (var item in search.Albums.Items)
            {
                var r = new RemoteSearchResult
                {
                    Name = item.Name,
                    SearchProviderName = Name,
                    ImageUrl = item.Images?.FirstOrDefault()?.Url,
                };

                if (TryParseYear(item.ReleaseDate, out var year, out var premiereDate))
                {
                    r.ProductionYear = year;
                    r.PremiereDate = premiereDate;
                }

                r.ProviderIds["Spotify"] = item.Id;
                if (item.Artists != null && item.Artists.Count > 0)
                {
                    r.Artists = item.Artists.Select(a => new RemoteSearchResult { Name = a.Name }).ToArray();
                }

                results.Add(r);
            }

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Spotify search failed for album {Name}", searchInfo.Name);
            return Array.Empty<RemoteSearchResult>();
        }
    }

    /// <inheritdoc />
    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("spotify");
        return client.GetAsync(url, cancellationToken);
    }

    private static string BuildSearchQuery(AlbumInfo info)
    {
        var query = "album:" + (info.Name ?? string.Empty).Trim();
        var artist = info.AlbumArtists.Count > 0 ? info.AlbumArtists[0] : null;
        if (!string.IsNullOrWhiteSpace(artist))
        {
            query += " artist:" + artist.Trim();
        }

        return query;
    }

    private static bool TryParseYear(string? releaseDate, out int? year, out DateTime? premiereDate)
    {
        year = null;
        premiereDate = null;
        if (string.IsNullOrWhiteSpace(releaseDate))
        {
            return false;
        }

        if (DateTime.TryParse(releaseDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
        {
            premiereDate = dt;
            year = dt.Year;
            return true;
        }

        if (releaseDate.Length >= 4 && int.TryParse(releaseDate.AsSpan(0, 4), out var y))
        {
            year = y;
            premiereDate = new DateTime(y, 1, 1);
            return true;
        }

        return false;
    }
}
