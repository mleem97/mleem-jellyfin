using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MusicHoarderzProvider.Models;
using Jellyfin.Plugin.MusicHoarderzProvider.Services;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MusicHoarderzProvider.Providers;

/// <summary>
/// Remote metadata provider for albums backed by MusicHoarderz/COV.
/// Merge-only: only delivered fields are set, stored values are never cleared.
/// </summary>
public sealed partial class MusicHoarderzMetadataProvider : IRemoteMetadataProvider<MusicAlbum, AlbumInfo>, IHasOrder
{
    private const string ProviderKey = "MusicHoarderz";
    private const int MaxSearchResults = 8;

    private readonly MusicHoarderzHttpClient _httpClient;
    private readonly CoverMatchScorer _scorer;
    private readonly ILogger<MusicHoarderzMetadataProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="MusicHoarderzMetadataProvider"/> class.
    /// </summary>
    /// <param name="httpClient">Cover HTTP client.</param>
    /// <param name="scorer">Match scorer.</param>
    /// <param name="logger">Logger.</param>
    public MusicHoarderzMetadataProvider(
        MusicHoarderzHttpClient httpClient,
        CoverMatchScorer scorer,
        ILogger<MusicHoarderzMetadataProvider> logger)
    {
        _httpClient = httpClient;
        _scorer = scorer;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => ProviderKey;

    /// <inheritdoc />
    public int Order => 1;

    /// <inheritdoc />
    public async Task<MetadataResult<MusicAlbum>> GetMetadata(AlbumInfo info, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(info);
        var result = new MetadataResult<MusicAlbum> { HasMetadata = false };
        var configuration = Plugin.Instance?.Configuration;
        if (configuration is null || !configuration.Enabled || !configuration.MusicHoarderz.Enabled)
        {
            return result;
        }

        try
        {
            if (info.ProviderIds.TryGetValue(ProviderKey, out var selectedUrl)
                && !string.IsNullOrWhiteSpace(selectedUrl))
            {
                return FromManualSelection(info, selectedUrl.Trim());
            }

            if (!configuration.AutoSearchEnabled)
            {
                return result;
            }

            var query = ToQuery(info, configuration.MusicHoarderz.Country ?? "DE");
            var results = await _httpClient.SearchAsync(query, cancellationToken).ConfigureAwait(false);
            var scored = _scorer.Score(query, results, configuration.MinimumWidth, configuration.MinimumHeight);
            var best = scored.Count > 0 ? scored[0] : null;
            if (best is null)
            {
                return result;
            }

            var album = new MusicAlbum();
            album.ProviderIds[ProviderKey] = best.Url;
            result.Item = album;
            result.HasMetadata = true;
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogLookupFailed(_logger, ex, info.Name ?? string.Empty);
            return result;
        }
    }

    /// <inheritdoc />
    public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(AlbumInfo searchInfo, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(searchInfo);
        var configuration = Plugin.Instance?.Configuration;
        if (configuration is null || !configuration.Enabled || !configuration.MusicHoarderz.Enabled)
        {
            return Array.Empty<RemoteSearchResult>();
        }

        try
        {
            var query = ToQuery(searchInfo, configuration.MusicHoarderz.Country ?? "DE");
            if (string.IsNullOrWhiteSpace(query.Title))
            {
                return Array.Empty<RemoteSearchResult>();
            }

            var results = await _httpClient.SearchAsync(query, cancellationToken).ConfigureAwait(false);
            var scored = _scorer.Score(query, results, configuration.MinimumWidth, configuration.MinimumHeight);
            var mapped = new List<RemoteSearchResult>();
            foreach (var candidate in scored.Take(MaxSearchResults))
            {
                cancellationToken.ThrowIfCancellationRequested();
                mapped.Add(new RemoteSearchResult
                {
                    Name = string.IsNullOrWhiteSpace(candidate.Title) ? query.Title : candidate.Title,
                    AlbumArtist = string.IsNullOrWhiteSpace(candidate.Artist) && string.IsNullOrWhiteSpace(query.Artist)
                        ? null
                        : new RemoteSearchResult { Name = string.IsNullOrWhiteSpace(candidate.Artist) ? query.Artist : candidate.Artist },
                    ProductionYear = candidate.Year ?? query.Year,
                    ProviderIds = new Dictionary<string, string> { [ProviderKey] = candidate.Url },
                    ImageUrl = candidate.Url,
                    Overview = string.Format(
                        CultureInfo.InvariantCulture,
                        "MusicHoarderz score {0}/100: {1}",
                        candidate.Score,
                        candidate.ScoreReason),
                });
            }

            return mapped;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogSearchFailed(_logger, ex, searchInfo.Name ?? string.Empty);
            return Array.Empty<RemoteSearchResult>();
        }
    }

    /// <inheritdoc />
    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
    {
        _ = url;
        _ = cancellationToken;
        throw new NotSupportedException("MusicHoarderz metadata provider does not serve images directly.");
    }

    private static MetadataResult<MusicAlbum> FromManualSelection(AlbumInfo info, string selectedUrl)
    {
        var result = new MetadataResult<MusicAlbum> { HasMetadata = false };
        var album = new MusicAlbum();
        var delivered = false;

        if (!string.IsNullOrWhiteSpace(info.Name))
        {
            album.Name = info.Name;
            delivered = true;
        }

        var artist = info.AlbumArtists.Count > 0 ? info.AlbumArtists[0] : null;
        if (!string.IsNullOrWhiteSpace(artist))
        {
            album.Artists = new[] { artist };
            delivered = true;
        }

        if (info.Year.HasValue)
        {
            album.ProductionYear = info.Year.Value;
            delivered = true;
        }

        album.ProviderIds[ProviderKey] = selectedUrl;
        result.Item = album;
        result.HasMetadata = delivered;
        return result;
    }

    private static CoverSearchQuery ToQuery(AlbumInfo info, string country)
    {
        var artist = info.AlbumArtists.Count > 0 ? info.AlbumArtists[0] : string.Empty;
        return new CoverSearchQuery(info.Name ?? string.Empty, artist ?? string.Empty, info.Year, country);
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "MusicHoarderz metadata lookup failed for {Album}.")]
    private static partial void LogLookupFailed(ILogger logger, Exception exception, string album);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "MusicHoarderz metadata search failed for {Album}.")]
    private static partial void LogSearchFailed(ILogger logger, Exception exception, string album);
}
