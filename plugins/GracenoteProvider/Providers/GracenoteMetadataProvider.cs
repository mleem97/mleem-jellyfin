using System;
using System.Collections.Generic;
using System.Linq;
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
    private readonly ILogger<GracenoteMetadataProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="GracenoteMetadataProvider"/> class.
    /// </summary>
    /// <param name="gracenote">Gracenote client.</param>
    /// <param name="logger">Logger.</param>
    public GracenoteMetadataProvider(GracenoteClient gracenote, ILogger<GracenoteMetadataProvider> logger)
    {
        _gracenote = gracenote;
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
        if (config is null || !config.EnableGracenote || string.IsNullOrWhiteSpace(config.GracenoteClientId))
        {
            return result;
        }

        try
        {
            var artist = info.AlbumArtists.Count > 0 ? info.AlbumArtists[0] : info.Name;
            var userId = await EnsureUserIdAsync(config, cancellationToken).ConfigureAwait(false);
            var matches = await _gracenote.SearchAlbumAsync(config.GracenoteClientId, userId, artist, info.Name, cancellationToken).ConfigureAwait(false);
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
        if (config is null || !config.EnableGracenote || string.IsNullOrWhiteSpace(config.GracenoteClientId))
        {
            return Array.Empty<RemoteSearchResult>();
        }

        try
        {
            var artist = searchInfo.AlbumArtists.Count > 0 ? searchInfo.AlbumArtists[0] : searchInfo.Name;
            var userId = await EnsureUserIdAsync(config, cancellationToken).ConfigureAwait(false);
            var matches = await _gracenote.SearchAlbumAsync(config.GracenoteClientId, userId, artist, searchInfo.Name, cancellationToken).ConfigureAwait(false);
            var results = new List<RemoteSearchResult>();
            foreach (var m in matches)
            {
                results.Add(new RemoteSearchResult
                {
                    Name = m.Title,
                    AlbumArtist = string.IsNullOrWhiteSpace(m.Artist) ? null : new RemoteSearchResult { Name = m.Artist },
                    ProductionYear = m.Year,
                    ProviderIds = string.IsNullOrWhiteSpace(m.GnId)
                        ? new Dictionary<string, string>()
                        : new Dictionary<string, string> { ["Gracenote"] = m.GnId },
                    ImageUrl = string.IsNullOrWhiteSpace(m.CoverUrl) ? null : m.CoverUrl,
                    Overview = string.IsNullOrWhiteSpace(m.Review) ? null : m.Review,
                });
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
        throw new NotSupportedException("Gracenote provider does not serve images directly.");
    }

    private async Task<string> EnsureUserIdAsync(PluginConfiguration config, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(config.GracenoteUserId))
        {
            return config.GracenoteUserId;
        }

        var userId = await _gracenote.RegisterAsync(config.GracenoteClientId, cancellationToken).ConfigureAwait(false);
        config.GracenoteUserId = userId;
        Plugin.Instance?.SaveConfiguration();
        return userId;
    }
}
