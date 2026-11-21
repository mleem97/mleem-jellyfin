using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MusicHoarderzProvider.Configuration;
using Jellyfin.Plugin.MusicHoarderzProvider.Models;
using Jellyfin.Plugin.MusicHoarderzProvider.Services;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Providers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MusicHoarderzProvider.Providers;

/// <summary>
/// Manual album metadata provider. Only source-delivered fields are returned;
/// background scans and refreshes never replace existing metadata.
/// </summary>
public sealed partial class MusicHoarderzMetadataProvider : IRemoteMetadataProvider<MusicAlbum, AlbumInfo>, IHasOrder
{
    private const string ProviderKey = "MusicHoarderz";
    private const int MaxSearchResults = 8;

    private readonly IAlbumMetadataProvider _provider;
    private readonly CoverMatchScorer _scorer;
    private readonly ILogger<MusicHoarderzMetadataProvider> _logger;
    private readonly IHttpContextAccessor? _contextAccessor;
    private readonly CoverImageValidator? _validator;
    private readonly Func<PluginConfiguration?> _configurationAccessor;

    /// <summary>Initializes a new instance of the <see cref="MusicHoarderzMetadataProvider"/> class.</summary>
    /// <param name="provider">Provider-independent album source.</param>
    /// <param name="scorer">Deterministic match scorer.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="contextAccessor">Jellyfin request context.</param>
    /// <param name="validator">Safe image downloader.</param>
    /// <param name="configurationAccessor">Optional offline test configuration.</param>
    public MusicHoarderzMetadataProvider(
        IAlbumMetadataProvider provider,
        CoverMatchScorer scorer,
        ILogger<MusicHoarderzMetadataProvider> logger,
        IHttpContextAccessor? contextAccessor = null,
        CoverImageValidator? validator = null,
        Func<PluginConfiguration?>? configurationAccessor = null)
    {
        _provider = provider;
        _scorer = scorer;
        _logger = logger;
        _contextAccessor = contextAccessor;
        _validator = validator;
        _configurationAccessor = configurationAccessor ?? (() => Plugin.Instance?.Configuration);
    }

    /// <inheritdoc />
    public string Name => ProviderKey;

    /// <inheritdoc />
    public int Order => 1;

    /// <inheritdoc />
    public async Task<MetadataResult<MusicAlbum>> GetMetadata(AlbumInfo info, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(info);
        cancellationToken.ThrowIfCancellationRequested();
        var empty = new MetadataResult<MusicAlbum> { HasMetadata = false };
        var configuration = _configurationAccessor();
        if (!ProviderRequestPolicy.IsManualRequest(_contextAccessor?.HttpContext, configuration)
            || !info.ProviderIds.TryGetValue(ProviderKey, out var selectedId)
            || string.IsNullOrWhiteSpace(selectedId))
        {
            return empty;
        }

        try
        {
            var selected = await _provider.GetAlbumAsync(selectedId, cancellationToken).ConfigureAwait(false);
            if (selected is null)
            {
                return empty;
            }

            var album = new MusicAlbum();
            var delivered = false;
            if (!string.IsNullOrWhiteSpace(selected.Title))
            {
                album.Name = selected.Title;
                delivered = true;
            }

            if (!string.IsNullOrWhiteSpace(selected.Artist))
            {
                album.Artists = new[] { selected.Artist };
                album.AlbumArtists = new[] { selected.Artist };
                delivered = true;
            }

            if (selected.Year is >= 1 and <= 9999)
            {
                album.ProductionYear = selected.Year;
                delivered = true;
            }

            foreach (var pair in selected.ProviderIds.Where(static pair => !string.IsNullOrWhiteSpace(pair.Value)))
            {
                album.ProviderIds[pair.Key] = pair.Value;
                delivered = true;
            }

            if (!delivered)
            {
                return empty;
            }

            foreach (var existingId in info.ProviderIds)
            {
                album.ProviderIds.TryAdd(existingId.Key, existingId.Value);
            }

            album.ProviderIds[ProviderKey] = selected.Id;
            return new MetadataResult<MusicAlbum> { HasMetadata = true, Item = album };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            LogLookupFailed(_logger);
            return empty;
        }
    }

    /// <inheritdoc />
    public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(AlbumInfo searchInfo, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(searchInfo);
        cancellationToken.ThrowIfCancellationRequested();
        var configuration = _configurationAccessor();
        if (!ProviderRequestPolicy.IsManualRequest(_contextAccessor?.HttpContext, configuration)
            || string.IsNullOrWhiteSpace(searchInfo.Name))
        {
            return Array.Empty<RemoteSearchResult>();
        }

        try
        {
            var query = ToQuery(searchInfo, configuration!.MusicHoarderz.Country);
            var candidates = await _provider.SearchAlbumsAsync(query, cancellationToken).ConfigureAwait(false);
            var covers = candidates.Select(static candidate => new NormalizedCoverResult(candidate.Id, null, null, "musichoarderz", null, 0, string.Empty, candidate.Title, candidate.Artist, candidate.Year, candidate.ProviderIds));
            var scored = _scorer.Score(query, covers, configuration.MinimumWidth, configuration.MinimumHeight);
            var results = new List<RemoteSearchResult>();
            foreach (var candidate in scored.Take(MaxSearchResults))
            {
                var ids = new Dictionary<string, string>(searchInfo.ProviderIds);
                foreach (var externalId in candidate.ProviderIds ?? new Dictionary<string, string>())
                {
                    ids[externalId.Key] = externalId.Value;
                }

                ids[ProviderKey] = candidate.Url;
                results.Add(new RemoteSearchResult
                {
                    Name = candidate.Title ?? query.Title,
                    AlbumArtist = string.IsNullOrWhiteSpace(candidate.Artist) ? null : new RemoteSearchResult { Name = candidate.Artist },
                    ProductionYear = candidate.Year,
                    ProviderIds = ids,
                    // Image selection is separate; never expose an unvalidated upstream URL.
                    ImageUrl = null,
                    Overview = string.Format(CultureInfo.InvariantCulture, "MusicHoarderz score {0}/100: {1}", candidate.Score, candidate.ScoreReason),
                });
            }

            return results;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            LogSearchFailed(_logger);
            return Array.Empty<RemoteSearchResult>();
        }
    }

    /// <inheritdoc />
    public async Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
    {
        var configuration = _configurationAccessor();
        if (_validator is null || !ProviderRequestPolicy.IsManualRequest(_contextAccessor?.HttpContext, configuration))
        {
            return new HttpResponseMessage(HttpStatusCode.Forbidden);
        }

        var image = await _validator.ValidateAsync(url, configuration!.MinimumWidth, configuration.MinimumHeight, cancellationToken).ConfigureAwait(false);
        if (image is null)
        {
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(image.Content.ToArray()) };
        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(image.ContentType);
        return response;
    }

    private static CoverSearchQuery ToQuery(AlbumInfo info, string country)
    {
        var artist = info.AlbumArtists.Count > 0 ? info.AlbumArtists[0] : string.Empty;
        return new CoverSearchQuery(info.Name ?? string.Empty, artist ?? string.Empty, info.Year, country, info.ProviderIds);
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "MusicHoarderz metadata lookup failed.")]
    private static partial void LogLookupFailed(ILogger logger);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "MusicHoarderz metadata search failed.")]
    private static partial void LogSearchFailed(ILogger logger);
}
