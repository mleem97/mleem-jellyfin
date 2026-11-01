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
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MusicHoarderzProvider.Providers;

/// <summary>
/// Remote image provider backed by MusicHoarderz/COV. Only validated covers are returned; failures are fail-open.
/// </summary>
public sealed partial class MusicHoarderzImageProvider : IRemoteImageProvider, IHasOrder
{
    private const int MaxCandidates = 5;

    private readonly ICoverProvider _httpClient;
    private readonly IHttpContextAccessor? _contextAccessor;
    private readonly Func<PluginConfiguration?> _configurationAccessor;
    private readonly CoverMatchScorer _scorer;
    private readonly CoverImageValidator _validator;
    private readonly ILogger<MusicHoarderzImageProvider> _logger;
    private readonly ValidatedCoverCache _imageCache;

    /// <summary>
    /// Initializes a new instance of the <see cref="MusicHoarderzImageProvider"/> class.
    /// </summary>
    /// <param name="httpClient">Cover HTTP client.</param>
    /// <param name="scorer">Match scorer.</param>
    /// <param name="validator">Image validator.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="contextAccessor">Jellyfin request context.</param>
    /// <param name="configurationAccessor">Optional configuration source for offline tests.</param>
    /// <param name="imageCache">Shared validated bytes served to the native Jellyfin downloader.</param>
    public MusicHoarderzImageProvider(
        ICoverProvider httpClient,
        CoverMatchScorer scorer,
        CoverImageValidator validator,
        ILogger<MusicHoarderzImageProvider> logger,
        IHttpContextAccessor? contextAccessor = null,
        Func<PluginConfiguration?>? configurationAccessor = null,
        ValidatedCoverCache? imageCache = null)
    {
        _httpClient = httpClient;
        _scorer = scorer;
        _validator = validator;
        _logger = logger;
        _contextAccessor = contextAccessor;
        _imageCache = imageCache ?? new ValidatedCoverCache();
        _configurationAccessor = configurationAccessor ?? (() => Plugin.Instance?.Configuration);
    }

    /// <inheritdoc />
    public string Name => "MusicHoarderz";

    /// <inheritdoc />
    public int Order => 1;

    /// <inheritdoc />
    public bool Supports(BaseItem item) => item is MusicAlbum || item is MusicArtist;

    /// <inheritdoc />
    public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
    {
        return item is MusicArtist
            ? new[] { ImageType.Primary, ImageType.Backdrop }
            : new[] { ImageType.Primary };
    }

    /// <inheritdoc />
    public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        var configuration = _configurationAccessor();
        if (!ProviderRequestPolicy.IsImageSearchRequest(_contextAccessor?.HttpContext, configuration))
        {
            return Array.Empty<RemoteImageInfo>();
        }

        string title;
        string artist;
        int? year;
        if (item is MusicAlbum album)
        {
            title = album.Name ?? string.Empty;
            artist = album.AlbumArtist ?? string.Empty;
            year = album.ProductionYear;
        }
        else if (item is MusicArtist musicArtist)
        {
            title = string.Empty;
            artist = musicArtist.Name ?? string.Empty;
            year = null;
        }
        else
        {
            return Array.Empty<RemoteImageInfo>();
        }

        try
        {
            var query = new CoverSearchQuery(
                title,
                artist,
                year,
                configuration!.MusicHoarderz.Country ?? "DE");
            var results = await _httpClient.SearchAsync(query, cancellationToken).ConfigureAwait(false);
            var scored = _scorer.Score(query, results, configuration.MinimumWidth, configuration.MinimumHeight);
            var images = new List<RemoteImageInfo>();
            var responseBytes = 0;
            foreach (var candidate in scored.Take(MaxCandidates))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var validated = await _validator.ValidateAsync(candidate.Url, configuration.MinimumWidth, configuration.MinimumHeight, cancellationToken).ConfigureAwait(false);
                if (validated is null)
                {
                    continue;
                }

                var ticket = _imageCache.AddForResponse(validated, ref responseBytes);
                if (!ticket.HasValue)
                {
                    continue;
                }

                var request = _contextAccessor!.HttpContext!.Request;
                var localUrl = string.Concat(request.Scheme, "://", request.Host.Value, request.PathBase.Value, "/Plugins/MusicHoarderzProvider/Images/", ticket.Value.ToString("N"));
                var providerDisplayName = string.Format(
                    CultureInfo.InvariantCulture,
                    "MusicHoarderz [{0}] ({1}x{2})",
                    candidate.Source.ToUpperInvariant(),
                    validated.Width,
                    validated.Height);

                images.Add(new RemoteImageInfo
                {
                    ProviderName = providerDisplayName,
                    Url = localUrl,
                    Width = validated.Width,
                    Height = validated.Height,
                    Type = ImageType.Primary,
                });

                if (item is MusicArtist && validated.Width >= 1200)
                {
                    images.Add(new RemoteImageInfo
                    {
                        ProviderName = providerDisplayName,
                        Url = localUrl,
                        Width = validated.Width,
                        Height = validated.Height,
                        Type = ImageType.Backdrop,
                    });
                }
            }

            return images;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            LogLookupFailed(_logger);
            return Array.Empty<RemoteImageInfo>();
        }
    }

    /// <inheritdoc />
    public async Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        cancellationToken.ThrowIfCancellationRequested();
        var configuration = _configurationAccessor();
        if (!ProviderRequestPolicy.IsManualRequest(_contextAccessor?.HttpContext, configuration))
        {
            return new HttpResponseMessage(HttpStatusCode.Forbidden);
        }

        var minimumWidth = configuration!.MinimumWidth;
        var minimumHeight = configuration.MinimumHeight;
        if (Uri.TryCreate(url, UriKind.Absolute, out var local)
            && local.AbsolutePath.Contains("/Plugins/MusicHoarderzProvider/Images/", StringComparison.Ordinal))
        {
            var lastSegment = local.Segments.LastOrDefault();
            var cached = Guid.TryParse(lastSegment, out var ticket) ? _imageCache.Get(ticket) : null;
            return cached is not null && cached.Width >= minimumWidth && cached.Height >= minimumHeight
                ? CreateImageResponse(cached.ContentType, cached.Content.ToArray())
                : new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        try
        {
            var validated = await _validator.ValidateAsync(url, minimumWidth, minimumHeight, cancellationToken).ConfigureAwait(false);
            if (validated is null)
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            return CreateImageResponse(validated.ContentType, validated.Content.ToArray());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            LogDownloadFailed(_logger);
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

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "MusicHoarderz cover lookup failed.")]
    private static partial void LogLookupFailed(ILogger logger);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "MusicHoarderz cover download failed.")]
    private static partial void LogDownloadFailed(ILogger logger);
}
