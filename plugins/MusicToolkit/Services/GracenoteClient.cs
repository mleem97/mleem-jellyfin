using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MusicToolkit.Services;

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
/// Minimal Gracenote MusicID Web API client (XML over HTTP).
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

    private static string EndpointFor(string clientId)
    {
        var prefix = clientId.Length >= 8 ? clientId.Substring(0, 8) : clientId;
        return string.Format(CultureInfo.InvariantCulture, "https://c{0}.web.cddbp.net/webapi/xml/1.0/", prefix);
    }

    /// <summary>
    /// Registers a client id and returns the user id.
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

        var doc = await PostXmlAsync(EndpointFor(clientId), xml, cancellationToken).ConfigureAwait(false);
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
    /// Searches albums in extended mode (cover, review, genre).
    /// </summary>
    /// <param name="clientId">Client id.</param>
    /// <param name="userId">Registered user id.</param>
    /// <param name="artist">Artist query.</param>
    /// <param name="album">Album query.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Album matches.</returns>
    public async Task<IReadOnlyList<GracenoteAlbum>> SearchAlbumAsync(
        string clientId,
        string userId,
        string artist,
        string album,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

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

        var doc = await PostXmlAsync(EndpointFor(clientId), xml, cancellationToken).ConfigureAwait(false);
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
}
