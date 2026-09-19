using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Jellyfin.Plugin.GracenoteProvider.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.GracenoteProvider.Services;

/// <summary>
/// Album search result from Gracenote.
/// </summary>
/// <param name="GnId">Gracenote id.</param>
/// <param name="Title">Album title.</param>
/// <param name="Artist">Album artist.</param>
/// <param name="Year">Release year.</param>
/// <param name="Genre">Genre.</param>
/// <param name="CoverUrl">Hi-res cover url.</param>
/// <param name="Review">Review text.</param>
public sealed record GracenoteAlbum(string GnId, string Title, string Artist, int? Year, string Genre, string CoverUrl, string Review);

/// <summary>
/// Artist search result from Gracenote.
/// </summary>
/// <param name="GnId">Gracenote id.</param>
/// <param name="Name">Artist name.</param>
/// <param name="Genre">Genre.</param>
/// <param name="ImageUrl">Image url.</param>
/// <param name="Biography">Biography or overview.</param>
public sealed record GracenoteArtist(string GnId, string Name, string Genre, string ImageUrl, string Biography);

/// <summary>
/// Dual-mode Gracenote MusicID client supporting both GMD v2 (XML) and GMD v3 (REST JSON).
/// </summary>
public sealed partial class GracenoteClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<GracenoteClient> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="GracenoteClient"/> class.
    /// </summary>
    /// <param name="httpClientFactory">HTTP client factory.</param>
    /// <param name="logger">Logger.</param>
    public GracenoteClient(IHttpClientFactory httpClientFactory, ILogger<GracenoteClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    private static string EndpointForV2(string clientId)
    {
        var prefix = clientId.Length >= 8 ? clientId.Substring(0, 8) : clientId;
        return string.Format(CultureInfo.InvariantCulture, "https://c{0}.web.cddbp.net/webapi/xml/1.0/", prefix);
    }

    /// <summary>
    /// Registers a client id and returns the user id for GMD v2.
    /// </summary>
    /// <param name="clientId">Gracenote client id (CLIENT).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Registered user id.</returns>
    public async Task<string> RegisterAsync(string clientId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        var xml = new XDocument(
            new XElement(
                "QUERIES",
                new XElement(
                    "AUTH",
                    new XElement("CLIENT", clientId),
                    new XElement("USER", string.Empty)),
                new XElement(
                    "QUERY",
                    new XAttribute("CMD", "REGISTER"),
                    new XElement("CLIENT", clientId))));

        var doc = await PostXmlAsync(EndpointForV2(clientId), xml, cancellationToken).ConfigureAwait(false);
        var user = doc.Descendants("USER").FirstOrDefault()?.Value?.Trim()
            ?? doc.Descendants("RESPONSE").FirstOrDefault()?.Attribute("USER")?.Value?.Trim();

        if (string.IsNullOrWhiteSpace(user))
        {
            throw new InvalidOperationException("Gracenote REGISTER did not return a USER value.");
        }

        LogClientRegistered(_logger, user.Length);
        return user;
    }

    [LoggerMessage(EventId = 0, Level = LogLevel.Information, Message = "Gracenote client registered, user id length {Len}")]
    private static partial void LogClientRegistered(ILogger logger, int len);

    /// <summary>
    /// Searches albums using configured GMD version (v2 or v3).
    /// </summary>
    /// <param name="config">Plugin configuration.</param>
    /// <param name="artist">Artist query.</param>
    /// <param name="album">Album query.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Album matches.</returns>
    public async Task<IReadOnlyList<GracenoteAlbum>> SearchAlbumAsync(
        PluginConfiguration config,
        string artist,
        string album,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (string.Equals(config.ApiVersion, "v3", StringComparison.OrdinalIgnoreCase))
        {
            return await SearchAlbumV3Async(config, artist, album, cancellationToken).ConfigureAwait(false);
        }

        return await SearchAlbumV2Async(config.GracenoteClientId, config.GracenoteUserId, artist, album, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Searches artists using configured GMD version (v2 or v3).
    /// </summary>
    /// <param name="config">Plugin configuration.</param>
    /// <param name="artistName">Artist name query.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Artist matches.</returns>
    public async Task<IReadOnlyList<GracenoteArtist>> SearchArtistAsync(
        PluginConfiguration config,
        string artistName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (string.Equals(config.ApiVersion, "v3", StringComparison.OrdinalIgnoreCase))
        {
            return await SearchArtistV3Async(config, artistName, cancellationToken).ConfigureAwait(false);
        }

        return await SearchArtistV2Async(config.GracenoteClientId, config.GracenoteUserId, artistName, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<GracenoteAlbum>> SearchAlbumV2Async(
        string clientId,
        string userId,
        string artist,
        string album,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(userId))
        {
            return Array.Empty<GracenoteAlbum>();
        }

        var xml = new XDocument(
            new XElement(
                "QUERIES",
                new XElement(
                    "AUTH",
                    new XElement("CLIENT", clientId),
                    new XElement("USER", userId)),
                new XElement(
                    "QUERY",
                    new XAttribute("CMD", "ALBUM_SEARCH"),
                    new XElement("MODE", "EXTENDED"),
                    new XElement(
                        "TEXT",
                        new XAttribute("TYPE", "ARTIST"),
                        artist ?? string.Empty),
                    new XElement(
                        "TEXT",
                        new XAttribute("TYPE", "ALBUM_TITLE"),
                        album ?? string.Empty),
                    new XElement(
                        "OPTION",
                        new XElement("PARAMETER", "SELECT_EXTENDED"),
                        new XElement("VALUE", "COVER,REVIEW,GENRE")))));

        var doc = await PostXmlAsync(EndpointForV2(clientId), xml, cancellationToken).ConfigureAwait(false);
        var results = new List<GracenoteAlbum>();

        foreach (var node in doc.Descendants("ALBUM"))
        {
            string? Get(string name) => node.Element(name)?.Value?.Trim();
            var gnId = node.Attribute("ID")?.Value?.Trim() ?? Get("GN_ID") ?? string.Empty;
            var title = Get("TITLE") ?? album ?? string.Empty;
            var artistName = Get("ARTIST") ?? artist ?? string.Empty;
            int? year = int.TryParse(Get("DATE"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var y) ? y : null;
            var genre = node.Descendants("GENRE").Select(g => g.Value.Trim()).FirstOrDefault(g => !string.IsNullOrWhiteSpace(g)) ?? string.Empty;
            var cover = node.Descendants("URL").Select(u => u.Value.Trim()).FirstOrDefault(u => u.StartsWith("http", StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
            var review = Get("REVIEW") ?? string.Empty;
            results.Add(new GracenoteAlbum(gnId, title, artistName, year, genre, cover, review));
        }

        return results;
    }

    private async Task<IReadOnlyList<GracenoteArtist>> SearchArtistV2Async(
        string clientId,
        string userId,
        string artistName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(userId))
        {
            return Array.Empty<GracenoteArtist>();
        }

        var xml = new XDocument(
            new XElement(
                "QUERIES",
                new XElement(
                    "AUTH",
                    new XElement("CLIENT", clientId),
                    new XElement("USER", userId)),
                new XElement(
                    "QUERY",
                    new XAttribute("CMD", "ALBUM_SEARCH"),
                    new XElement("MODE", "EXTENDED"),
                    new XElement(
                        "TEXT",
                        new XAttribute("TYPE", "ARTIST"),
                        artistName ?? string.Empty),
                    new XElement(
                        "OPTION",
                        new XElement("PARAMETER", "SELECT_EXTENDED"),
                        new XElement("VALUE", "ARTIST_IMAGE,ARTIST_BIOGRAPHY,GENRE")))));

        var doc = await PostXmlAsync(EndpointForV2(clientId), xml, cancellationToken).ConfigureAwait(false);
        var results = new List<GracenoteArtist>();

        foreach (var node in doc.Descendants("ALBUM"))
        {
            string? Get(string name) => node.Element(name)?.Value?.Trim();
            var gnId = node.Element("ARTIST_GN_ID")?.Value?.Trim() ?? node.Attribute("ID")?.Value?.Trim() ?? string.Empty;
            var name = Get("ARTIST") ?? artistName ?? string.Empty;
            var genre = node.Descendants("GENRE").Select(g => g.Value.Trim()).FirstOrDefault(g => !string.IsNullOrWhiteSpace(g)) ?? string.Empty;
            var image = node.Descendants("URL").Select(u => u.Value.Trim()).FirstOrDefault(u => u.StartsWith("http", StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
            var bio = Get("ARTIST_BIOGRAPHY") ?? string.Empty;
            results.Add(new GracenoteArtist(gnId, name, genre, image, bio));
            break;
        }

        return results;
    }

    private async Task<IReadOnlyList<GracenoteAlbum>> SearchAlbumV3Async(
        PluginConfiguration config,
        string artist,
        string album,
        CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("gracenote-v3");
        var key = !string.IsNullOrWhiteSpace(config.ApiKey) ? config.ApiKey : config.GracenoteClientId;
        var url = $"https://api.gracenote.com/music/v3/search/albums?q={Uri.EscapeDataString(album)}&artist={Uri.EscapeDataString(artist)}&client={Uri.EscapeDataString(key)}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrWhiteSpace(config.ApiKey))
        {
            request.Headers.Add("x-api-key", config.ApiKey);
        }

        using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Gracenote V3 search returned status {StatusCode}", response.StatusCode);
            return Array.Empty<GracenoteAlbum>();
        }

        var payload = await response.Content.ReadFromJsonAsync<GmdV3AlbumResponse>(cancellationToken: cancellationToken).ConfigureAwait(false);
        var list = new List<GracenoteAlbum>();
        foreach (var item in payload?.Albums ?? Enumerable.Empty<GmdV3AlbumDto>())
        {
            var cover = item.Images?.OrderByDescending(i => i.Width ?? 0).FirstOrDefault()?.Url ?? string.Empty;
            list.Add(new GracenoteAlbum(
                item.Id ?? string.Empty,
                item.Title ?? album,
                item.Artist ?? artist,
                item.Year,
                item.Genre ?? string.Empty,
                cover,
                item.Review ?? string.Empty));
        }

        return list;
    }

    private async Task<IReadOnlyList<GracenoteArtist>> SearchArtistV3Async(
        PluginConfiguration config,
        string artistName,
        CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("gracenote-v3");
        var key = !string.IsNullOrWhiteSpace(config.ApiKey) ? config.ApiKey : config.GracenoteClientId;
        var url = $"https://api.gracenote.com/music/v3/search/artists?q={Uri.EscapeDataString(artistName)}&client={Uri.EscapeDataString(key)}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrWhiteSpace(config.ApiKey))
        {
            request.Headers.Add("x-api-key", config.ApiKey);
        }

        using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Gracenote V3 artist search returned status {StatusCode}", response.StatusCode);
            return Array.Empty<GracenoteArtist>();
        }

        var payload = await response.Content.ReadFromJsonAsync<GmdV3ArtistResponse>(cancellationToken: cancellationToken).ConfigureAwait(false);
        var list = new List<GracenoteArtist>();
        foreach (var item in payload?.Artists ?? Enumerable.Empty<GmdV3ArtistDto>())
        {
            var image = item.Images?.OrderByDescending(i => i.Width ?? 0).FirstOrDefault()?.Url ?? string.Empty;
            list.Add(new GracenoteArtist(
                item.Id ?? string.Empty,
                item.Name ?? artistName,
                item.Genre ?? string.Empty,
                image,
                item.Biography ?? string.Empty));
        }

        return list;
    }

    private async Task<XDocument> PostXmlAsync(string endpoint, XDocument payload, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("gracenote");
        client.Timeout = TimeSpan.FromSeconds(30);
        using var content = new StringContent(payload.ToString(SaveOptions.DisableFormatting), Encoding.UTF8, "application/xml");
        using var response = await client.PostAsync(endpoint, content, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return XDocument.Parse(body);
    }

    private sealed class GmdV3AlbumResponse
    {
        [JsonPropertyName("albums")]
        public List<GmdV3AlbumDto>? Albums { get; set; }
    }

    private sealed class GmdV3AlbumDto
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("artist")]
        public string? Artist { get; set; }

        [JsonPropertyName("year")]
        public int? Year { get; set; }

        [JsonPropertyName("genre")]
        public string? Genre { get; set; }

        [JsonPropertyName("review")]
        public string? Review { get; set; }

        [JsonPropertyName("images")]
        public List<GmdV3ImageDto>? Images { get; set; }
    }

    private sealed class GmdV3ArtistResponse
    {
        [JsonPropertyName("artists")]
        public List<GmdV3ArtistDto>? Artists { get; set; }
    }

    private sealed class GmdV3ArtistDto
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("genre")]
        public string? Genre { get; set; }

        [JsonPropertyName("biography")]
        public string? Biography { get; set; }

        [JsonPropertyName("images")]
        public List<GmdV3ImageDto>? Images { get; set; }
    }

    private sealed class GmdV3ImageDto
    {
        [JsonPropertyName("url")]
        public string? Url { get; set; }

        [JsonPropertyName("width")]
        public int? Width { get; set; }

        [JsonPropertyName("height")]
        public int? Height { get; set; }
    }
}
