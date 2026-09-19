using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MetadataDashboard.Services;

/// <summary>
/// Service communicating with the CoverArtArchive API to retrieve high-resolution album artwork.
/// </summary>
public sealed class CoverArtArchiveService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<CoverArtArchiveService> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="CoverArtArchiveService"/> class.
    /// </summary>
    public CoverArtArchiveService(IHttpClientFactory httpClientFactory, ILogger<CoverArtArchiveService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Retrieves artwork listings for a specific MusicBrainz Release MBID.
    /// </summary>
    public async Task<CoverArtArchiveResult?> GetReleaseCoverArtAsync(string releaseMbid, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(releaseMbid))
        {
            return null;
        }

        var baseUrl = Plugin.Instance?.Configuration.CoverArtArchiveMirror ?? "https://coverartarchive.org";
        var requestUrl = $"{baseUrl.TrimEnd('/')}/release/{Uri.EscapeDataString(releaseMbid.Trim())}";

        try
        {
            using var client = _httpClientFactory.CreateClient("MetadataDashboard");
            client.DefaultRequestHeaders.UserAgent.Clear();
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MetadataDashboard", "1.0.0"));
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await client.GetAsync(new Uri(requestUrl), HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return null;
            }

            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<CoverArtArchiveResult>(json, JsonOptions);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to retrieve CoverArtArchive data for release {ReleaseMbid}", releaseMbid);
            return null;
        }
    }

    /// <summary>
    /// Converts CoverArtArchive result into Jellyfin RemoteImageInfo items.
    /// </summary>
    public async Task<List<RemoteImageInfo>> GetRemoteImagesAsync(string releaseMbid, CancellationToken cancellationToken = default)
    {
        var result = new List<RemoteImageInfo>();
        var data = await GetReleaseCoverArtAsync(releaseMbid, cancellationToken).ConfigureAwait(false);
        if (data?.Images == null)
        {
            return result;
        }

        foreach (var img in data.Images)
        {
            var type = ImageType.Primary;
            if (img.Back)
            {
                type = ImageType.Backdrop;
            }
            else if (img.Types != null && img.Types.Contains("Medium"))
            {
                type = ImageType.Disc;
            }

            var url = img.Thumbnails?.Large ?? img.Thumbnails?.Size1200 ?? img.Image;
            if (string.IsNullOrWhiteSpace(url))
            {
                continue;
            }

            result.Add(new RemoteImageInfo
            {
                ProviderName = "CoverArtArchive",
                Url = url,
                Type = type,
                ThumbnailUrl = img.Thumbnails?.Small ?? img.Thumbnails?.Size250 ?? url
            });
        }

        return result;
    }
}

/// <summary>
/// CoverArtArchive API result representation.
/// </summary>
public sealed class CoverArtArchiveResult
{
    public List<CoverArtImage>? Images { get; set; }

    public string? Release { get; set; }
}

/// <summary>
/// Single CoverArtArchive image item.
/// </summary>
public sealed class CoverArtImage
{
    public List<string>? Types { get; set; }

    public bool Front { get; set; }

    public bool Back { get; set; }

    public string? Image { get; set; }

    public CoverArtThumbnails? Thumbnails { get; set; }

    public bool Approved { get; set; }
}

/// <summary>
/// CoverArtArchive thumbnail resolutions.
/// </summary>
public sealed class CoverArtThumbnails
{
    [JsonPropertyName("250")]
    public string? Size250 { get; set; }

    [JsonPropertyName("500")]
    public string? Size500 { get; set; }

    [JsonPropertyName("1200")]
    public string? Size1200 { get; set; }

    public string? Small { get; set; }

    public string? Large { get; set; }
}
