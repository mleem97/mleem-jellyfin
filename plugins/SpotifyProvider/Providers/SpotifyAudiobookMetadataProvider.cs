using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.SpotifyProvider.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SpotifyProvider.Providers;

/// <summary>
/// Remote metadata provider for Audiobooks (Book) backed by Spotify Web API.
/// </summary>
public sealed class SpotifyAudiobookMetadataProvider : IRemoteMetadataProvider<Book, BookInfo>, IHasOrder
{
    private readonly SpotifyApiClient _apiClient;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SpotifyAudiobookMetadataProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SpotifyAudiobookMetadataProvider"/> class.
    /// </summary>
    /// <param name="apiClient">Spotify API client.</param>
    /// <param name="httpClientFactory">HTTP client factory.</param>
    /// <param name="logger">Logger.</param>
    public SpotifyAudiobookMetadataProvider(SpotifyApiClient apiClient, IHttpClientFactory httpClientFactory, ILogger<SpotifyAudiobookMetadataProvider> logger)
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
    public async Task<MetadataResult<Book>> GetMetadata(BookInfo info, CancellationToken cancellationToken)
    {
        var result = new MetadataResult<Book> { HasMetadata = false };
        var config = Plugin.Instance?.Configuration;
        if (config is null || !config.EnableAudiobookMetadata || string.IsNullOrWhiteSpace(config.ClientId))
        {
            return result;
        }

        try
        {
            SpotifyAudiobookDto? audiobookDto = null;
            if (info.ProviderIds.TryGetValue("Spotify", out var spotifyId) && !string.IsNullOrWhiteSpace(spotifyId))
            {
                audiobookDto = await _apiClient.GetAudiobookAsync(spotifyId, config.Market, cancellationToken).ConfigureAwait(false);
            }

            if (audiobookDto is null)
            {
                var query = (info.Name ?? string.Empty).Trim();
                var search = await _apiClient.SearchAsync(query, "audiobook", config.Market, 5, cancellationToken).ConfigureAwait(false);
                var first = search?.Audiobooks?.Items?.FirstOrDefault();
                if (first is not null)
                {
                    audiobookDto = await _apiClient.GetAudiobookAsync(first.Id, config.Market, cancellationToken).ConfigureAwait(false) ?? first;
                }
            }

            if (audiobookDto is null)
            {
                return result;
            }

            var book = new Book
            {
                Name = audiobookDto.Name,
                Overview = audiobookDto.Description,
            };

            if (audiobookDto.Authors is not null && audiobookDto.Authors.Count > 0)
            {
                // In Jellyfin Book, authors can be added as persons if supported or set in overview
            }

            book.ProviderIds["Spotify"] = audiobookDto.Id;
            if (audiobookDto.ExternalIds != null && audiobookDto.ExternalIds.TryGetValue("isbn", out var isbn))
            {
                book.ProviderIds["ISBN"] = isbn;
            }

            result.Item = book;
            result.HasMetadata = true;
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Spotify metadata lookup failed for audiobook {Name}", info.Name);
            return result;
        }
    }

    /// <inheritdoc />
    public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(BookInfo searchInfo, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration;
        if (config is null || !config.EnableAudiobookMetadata || string.IsNullOrWhiteSpace(config.ClientId))
        {
            return Array.Empty<RemoteSearchResult>();
        }

        try
        {
            var query = (searchInfo.Name ?? string.Empty).Trim();
            var search = await _apiClient.SearchAsync(query, "audiobook", config.Market, 10, cancellationToken).ConfigureAwait(false);
            if (search?.Audiobooks?.Items is null)
            {
                return Array.Empty<RemoteSearchResult>();
            }

            var results = new List<RemoteSearchResult>();
            foreach (var item in search.Audiobooks.Items)
            {
                var r = new RemoteSearchResult
                {
                    Name = item.Name,
                    SearchProviderName = Name,
                    Overview = item.Description,
                    ImageUrl = item.Images?.FirstOrDefault()?.Url,
                };

                r.ProviderIds["Spotify"] = item.Id;
                if (item.Authors != null && item.Authors.Count > 0)
                {
                    r.Artists = item.Authors.Select(a => new RemoteSearchResult { Name = a.Name }).ToArray();
                }

                results.Add(r);
            }

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Spotify search failed for audiobook {Name}", searchInfo.Name);
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
