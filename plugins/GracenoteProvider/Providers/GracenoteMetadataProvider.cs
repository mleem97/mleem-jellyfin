using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.GracenoteProvider.Configuration;
using Jellyfin.Plugin.GracenoteProvider.Services;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.GracenoteProvider.Providers;

/// <summary>
/// Remote metadata provider for albums backed by Gracenote.
/// </summary>
public sealed class GracenoteMetadataProvider : IRemoteMetadataProvider<MusicAlbum, AlbumInfo>, IHasOrder
{
    private readonly GracenoteClient _gracenote;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<GracenoteMetadataProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="GracenoteMetadataProvider"/> class.
    /// </summary>
    /// <param name="gracenote">Gracenote client.</param>
    /// <param name="httpClientFactory">HTTP client factory.</param>
    /// <param name="logger">Logger.</param>
    public GracenoteMetadataProvider(GracenoteClient gracenote, IHttpClientFactory httpClientFactory, ILogger<GracenoteMetadataProvider> logger)
    {
        _gracenote = gracenote;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Gracenote";

    /// <inheritdoc />
    public int Order => 2;

    /// <inheritdoc />
    public async Task<MetadataResult<MusicAlbum>> GetMetadata(AlbumInfo info, CancellationToken cancellationToken)
    {
        var result = new MetadataResult<MusicAlbum> { HasMetadata = false };
        var config = Plugin.Instance?.Configuration;
        if (config is null || !config.EnableGracenote || !config.EnableAlbumMetadata)
        {
            return result;
        }

        try
        {
            await EnsureUserIdAsync(config, cancellationToken).ConfigureAwait(false);
            var artist = info.AlbumArtists.Count > 0 ? info.AlbumArtists[0] : (info.Name ?? string.Empty);
            var matches = await _gracenote.SearchAlbumAsync(config, artist, info.Name ?? string.Empty, cancellationToken).ConfigureAwait(false);
            if (matches.Count == 0)
            {
                return result;
            }

            var best = matches[0];
            var album = new MusicAlbum
            {
                Name = string.IsNullOrWhiteSpace(best.Title) ? info.Name : best.Title,
                ProductionYear = best.Year,
                Overview = string.IsNullOrWhiteSpace(best.Review) ? null : best.Review,
            };

            if (!string.IsNullOrWhiteSpace(best.Genre))
            {
                album.Genres = new[] { best.Genre };
            }

            if (!string.IsNullOrWhiteSpace(best.GnId))
            {
                album.ProviderIds["Gracenote"] = best.GnId;
            }

            result.Item = album;
            result.HasMetadata = true;
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Gracenote metadata lookup failed for {Album}", info.Name);
            return result;
        }
    }

    /// <inheritdoc />
    public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(AlbumInfo searchInfo, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration;
        if (config is null || !config.EnableGracenote || !config.EnableAlbumMetadata)
        {
            return Array.Empty<RemoteSearchResult>();
        }

        try
        {
            await EnsureUserIdAsync(config, cancellationToken).ConfigureAwait(false);
            var artist = searchInfo.AlbumArtists.Count > 0 ? searchInfo.AlbumArtists[0] : string.Empty;
            var matches = await _gracenote.SearchAlbumAsync(config, artist, searchInfo.Name, cancellationToken).ConfigureAwait(false);
            var results = new List<RemoteSearchResult>();

            foreach (var match in matches)
            {
                var r = new RemoteSearchResult
                {
                    Name = match.Title,
                    SearchProviderName = Name,
                    ProductionYear = match.Year,
                    Overview = match.Review,
                    ImageUrl = match.CoverUrl,
                };

                if (!string.IsNullOrWhiteSpace(match.GnId))
                {
                    r.ProviderIds["Gracenote"] = match.GnId;
                }

                if (!string.IsNullOrWhiteSpace(match.Artist))
                {
                    r.Artists = new[] { new RemoteSearchResult { Name = match.Artist } };
                }

                results.Add(r);
            }

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Gracenote search failed for {Album}", searchInfo.Name);
            return Array.Empty<RemoteSearchResult>();
        }
    }

    /// <inheritdoc />
    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("gracenote");
        return client.GetAsync(url, cancellationToken);
    }

    private async Task EnsureUserIdAsync(PluginConfiguration config, CancellationToken cancellationToken)
    {
        if (string.Equals(config.ApiVersion, "v2", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(config.GracenoteUserId)
            && !string.IsNullOrWhiteSpace(config.GracenoteClientId))
        {
            try
            {
                var userId = await _gracenote.RegisterAsync(config.GracenoteClientId, cancellationToken).ConfigureAwait(false);
                config.GracenoteUserId = userId;
                Plugin.Instance?.SaveConfiguration();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to auto-register Gracenote client");
            }
        }
    }
}
