using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MusicHoarderzProvider.Models;
using Jellyfin.Plugin.MusicHoarderzProvider.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MusicHoarderzProvider.Providers;

/// <summary>
/// Remote image provider backed by MusicHoarderz/COV. Only validated covers are returned; failures are fail-open.
/// </summary>
public sealed partial class MusicHoarderzImageProvider : IRemoteImageProvider, IHasOrder
{
    private const int MaxCandidates = 5;
    private const int ImageCacheMinutes = 10;
    private const int MaxCachedImages = 50;

    private readonly MusicHoarderzHttpClient _httpClient;
    private readonly CoverMatchScorer _scorer;
    private readonly CoverImageValidator _validator;
    private readonly ILogger<MusicHoarderzImageProvider> _logger;
    private readonly ConcurrentDictionary<string, CachedImage> _imageCache = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="MusicHoarderzImageProvider"/> class.
    /// </summary>
    /// <param name="httpClient">Cover HTTP client.</param>
    /// <param name="scorer">Match scorer.</param>
    /// <param name="validator">Image validator.</param>
    /// <param name="logger">Logger.</param>
    public MusicHoarderzImageProvider(
        MusicHoarderzHttpClient httpClient,
        CoverMatchScorer scorer,
        CoverImageValidator validator,
        ILogger<MusicHoarderzImageProvider> logger)
    {
        _httpClient = httpClient;
        _scorer = scorer;
        _validator = validator;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "MusicHoarderz";

    /// <inheritdoc />
    public int Order => 1;

    /// <inheritdoc />
    public bool Supports(BaseItem item) => item is MusicAlbum;

    /// <inheritdoc />
    public IEnumerable<ImageType> GetSupportedImages(BaseItem item) => new[] { ImageType.Primary };

    /// <inheritdoc />
    public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        var configuration = Plugin.Instance?.Configuration;
        if (configuration is null || !configuration.Enabled || !configuration.MusicHoarderz.Enabled)
        {
            return Array.Empty<RemoteImageInfo>();
        }

        if (item is not MusicAlbum album)
        {
            return Array.Empty<RemoteImageInfo>();
        }

        try
        {
            var query = new CoverSearchQuery(
                album.Name ?? string.Empty,
                album.AlbumArtist ?? string.Empty,
                album.ProductionYear,
                configuration.MusicHoarderz.Country ?? "DE");
            var results = await _httpClient.SearchAsync(query, cancellationToken).ConfigureAwait(false);
            var scored = _scorer.Score(query, results, configuration.MinimumWidth, configuration.MinimumHeight);
            var images = new List<RemoteImageInfo>();
            foreach (var candidate in scored.Take(MaxCandidates))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var validated = await _validator.ValidateAsync(candidate.Url, configuration.MinimumWidth, configuration.MinimumHeight, cancellationToken).ConfigureAwait(false);
                if (validated is null)
                {
                    continue;
                }

                StoreInCache(validated);
                images.Add(new RemoteImageInfo
                {
                    ProviderName = Name,
                    Url = validated.Url,
                    Width = validated.Width,
                    Height = validated.Height,
                    Type = ImageType.Primary,
                });
            }

            return images;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogLookupFailed(_logger, ex, item.Name ?? string.Empty);
            return Array.Empty<RemoteImageInfo>();
        }
    }

    /// <inheritdoc />
    public async Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        if (_imageCache.TryGetValue(url, out var cached) && cached.ExpiresAtUtc > DateTimeOffset.UtcNow)
        {
            return CreateImageResponse(cached.ContentType, cached.Content.ToArray());
        }

        var configuration = Plugin.Instance?.Configuration;
        var minimumWidth = configuration?.MinimumWidth ?? 1;
        var minimumHeight = configuration?.MinimumHeight ?? 1;
        try
        {
            var validated = await _validator.ValidateAsync(url, minimumWidth, minimumHeight, cancellationToken).ConfigureAwait(false);
            if (validated is null)
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            StoreInCache(validated);
            return CreateImageResponse(validated.ContentType, validated.Content.ToArray());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogDownloadFailed(_logger, ex);
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }

    private static HttpResponseMessage CreateImageResponse(string contentType, byte[] bytes)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(bytes),
        };
        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        return response;
    }

    private void StoreInCache(ValidatedCoverImage validated)
    {
        if (_imageCache.Count >= MaxCachedImages)
        {
            foreach (var pair in _imageCache)
            {
                if (pair.Value.ExpiresAtUtc <= DateTimeOffset.UtcNow)
                {
                    _ = _imageCache.TryRemove(pair.Key, out _);
                }
            }
        }

        _imageCache[validated.Url] = new CachedImage(
            validated.ContentType,
            validated.Content,
            DateTimeOffset.UtcNow.AddMinutes(ImageCacheMinutes));
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "MusicHoarderz cover lookup failed for {Album}.")]
    private static partial void LogLookupFailed(ILogger logger, Exception exception, string album);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "MusicHoarderz cover download failed.")]
    private static partial void LogDownloadFailed(ILogger logger, Exception exception);

    private sealed class CachedImage
    {
        public CachedImage(string contentType, ReadOnlyMemory<byte> content, DateTimeOffset expiresAtUtc)
        {
            ContentType = contentType;
            Content = content;
            ExpiresAtUtc = expiresAtUtc;
        }

        public string ContentType { get; }

        public ReadOnlyMemory<byte> Content { get; }

        public DateTimeOffset ExpiresAtUtc { get; }
    }
}
