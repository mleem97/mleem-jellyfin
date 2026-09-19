using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MetadataDashboard.Services;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MetadataDashboard.Providers;

/// <summary>
/// Remote image provider fetching album and release artwork from CoverArtArchive.org.
/// </summary>
public sealed class CoverArtArchiveImageProvider : IRemoteImageProvider, IHasOrder
{
    private readonly CoverArtArchiveService _coverArtService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<CoverArtArchiveImageProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CoverArtArchiveImageProvider"/> class.
    /// </summary>
    public CoverArtArchiveImageProvider(
        CoverArtArchiveService coverArtService,
        IHttpClientFactory httpClientFactory,
        ILogger<CoverArtArchiveImageProvider> logger)
    {
        _coverArtService = coverArtService;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "CoverArtArchive";

    /// <inheritdoc />
    public int Order => 1;

    /// <inheritdoc />
    public bool Supports(BaseItem item)
    {
        return item is MusicAlbum;
    }

    /// <inheritdoc />
    public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
    {
        yield return ImageType.Primary;
        yield return ImageType.Backdrop;
        yield return ImageType.Disc;
    }

    /// <inheritdoc />
    public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
    {
        if (Plugin.Instance?.Configuration.EnableCoverArtArchiveProvider != true)
        {
            return Array.Empty<RemoteImageInfo>();
        }

        var releaseMbid = item.GetProviderId("MusicBrainzAlbum");
        if (string.IsNullOrWhiteSpace(releaseMbid))
        {
            releaseMbid = item.GetProviderId("MusicBrainzReleaseGroup");
        }

        if (string.IsNullOrWhiteSpace(releaseMbid))
        {
            return Array.Empty<RemoteImageInfo>();
        }

        try
        {
            return await _coverArtService.GetRemoteImagesAsync(releaseMbid, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to fetch CoverArtArchive images for item {ItemName}", item.Name);
            return Array.Empty<RemoteImageInfo>();
        }
    }

    /// <inheritdoc />
    public async Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("MetadataDashboard");
        return await client.GetAsync(new Uri(url), HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
    }
}
