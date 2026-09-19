using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.GracenoteProvider.Configuration;
using Jellyfin.Plugin.GracenoteProvider.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.GracenoteProvider.Providers;

/// <summary>
/// Remote image provider backed by Gracenote.
/// </summary>
public sealed class GracenoteImageProvider : IRemoteImageProvider, IHasOrder
{
    private readonly GracenoteClient _gracenote;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<GracenoteImageProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="GracenoteImageProvider"/> class.
    /// </summary>
    /// <param name="gracenote">Gracenote client.</param>
    /// <param name="httpClientFactory">HTTP client factory.</param>
    /// <param name="logger">Logger.</param>
    public GracenoteImageProvider(GracenoteClient gracenote, IHttpClientFactory httpClientFactory, ILogger<GracenoteImageProvider> logger)
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
    public bool Supports(BaseItem item) => item is MusicAlbum or MusicArtist;

    /// <inheritdoc />
    public IEnumerable<ImageType> GetSupportedImages(BaseItem item) => new[] { ImageType.Primary };

    /// <inheritdoc />
    public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration;
        if (config is null || !config.EnableGracenote || !config.EnableImageLookup)
        {
            return Array.Empty<RemoteImageInfo>();
        }

        try
        {
            if (item is MusicAlbum album)
            {
                var artist = album.AlbumArtist ?? (album.Name ?? string.Empty);
                var matches = await _gracenote.SearchAlbumAsync(config, artist, album.Name ?? string.Empty, cancellationToken).ConfigureAwait(false);
                var list = new List<RemoteImageInfo>();
                foreach (var match in matches)
                {
                    if (!string.IsNullOrWhiteSpace(match.CoverUrl))
                    {
                        list.Add(new RemoteImageInfo
                        {
                            ProviderName = Name,
                            Url = match.CoverUrl,
                            Type = ImageType.Primary,
                        });
                    }
                }

                return list;
            }

            if (item is MusicArtist artistItem)
            {
                var matches = await _gracenote.SearchArtistAsync(config, artistItem.Name, cancellationToken).ConfigureAwait(false);
                var list = new List<RemoteImageInfo>();
                foreach (var match in matches)
                {
                    if (!string.IsNullOrWhiteSpace(match.ImageUrl))
                    {
                        list.Add(new RemoteImageInfo
                        {
                            ProviderName = Name,
                            Url = match.ImageUrl,
                            Type = ImageType.Primary,
                        });
                    }
                }

                return list;
            }

            return Array.Empty<RemoteImageInfo>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Gracenote image lookup failed for {Name}", item.Name);
            return Array.Empty<RemoteImageInfo>();
        }
    }

    /// <inheritdoc />
    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("gracenote");
        return client.GetAsync(url, cancellationToken);
    }
}
