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
/// Remote metadata provider for MusicArtist backed by Gracenote.
/// </summary>
public sealed class GracenoteArtistMetadataProvider : IRemoteMetadataProvider<MusicArtist, ArtistInfo>, IHasOrder
{
    private readonly GracenoteClient _gracenote;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<GracenoteArtistMetadataProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="GracenoteArtistMetadataProvider"/> class.
    /// </summary>
    /// <param name="gracenote">Gracenote client.</param>
    /// <param name="httpClientFactory">HTTP client factory.</param>
    /// <param name="logger">Logger.</param>
    public GracenoteArtistMetadataProvider(GracenoteClient gracenote, IHttpClientFactory httpClientFactory, ILogger<GracenoteArtistMetadataProvider> logger)
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
    public async Task<MetadataResult<MusicArtist>> GetMetadata(ArtistInfo info, CancellationToken cancellationToken)
    {
        var result = new MetadataResult<MusicArtist> { HasMetadata = false };
        var config = Plugin.Instance?.Configuration;
        if (config is null || !config.EnableGracenote || !config.EnableArtistMetadata)
        {
            return result;
        }

        try
        {
            var matches = await _gracenote.SearchArtistAsync(config, info.Name, cancellationToken).ConfigureAwait(false);
            if (matches.Count == 0)
            {
                return result;
            }

            var best = matches[0];
            var artist = new MusicArtist
            {
                Name = string.IsNullOrWhiteSpace(best.Name) ? info.Name : best.Name,
                Overview = string.IsNullOrWhiteSpace(best.Biography) ? null : best.Biography,
            };

            if (!string.IsNullOrWhiteSpace(best.Genre))
            {
                artist.Genres = new[] { best.Genre };
            }

            if (!string.IsNullOrWhiteSpace(best.GnId))
            {
                artist.ProviderIds["Gracenote"] = best.GnId;
            }

            result.Item = artist;
            result.HasMetadata = true;
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Gracenote metadata lookup failed for artist {Artist}", info.Name);
            return result;
        }
    }

    /// <inheritdoc />
    public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(ArtistInfo searchInfo, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration;
        if (config is null || !config.EnableGracenote || !config.EnableArtistMetadata)
        {
            return Array.Empty<RemoteSearchResult>();
        }

        try
        {
            var matches = await _gracenote.SearchArtistAsync(config, searchInfo.Name, cancellationToken).ConfigureAwait(false);
            var results = new List<RemoteSearchResult>();

            foreach (var match in matches)
            {
                var r = new RemoteSearchResult
                {
                    Name = match.Name,
                    SearchProviderName = Name,
                    Overview = match.Biography,
                    ImageUrl = match.ImageUrl,
                };

                if (!string.IsNullOrWhiteSpace(match.GnId))
                {
                    r.ProviderIds["Gracenote"] = match.GnId;
                }

                results.Add(r);
            }

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Gracenote artist search failed for {Artist}", searchInfo.Name);
            return Array.Empty<RemoteSearchResult>();
        }
    }

    /// <inheritdoc />
    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("gracenote");
        return client.GetAsync(url, cancellationToken);
    }
}
