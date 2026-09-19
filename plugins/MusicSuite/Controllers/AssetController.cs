using System;
using System.IO;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.MusicSuite.Controllers;

/// <summary>
/// Serves embedded MusicSuite web assets, theme stylesheets, and icons.
/// </summary>
[ApiController]
[Authorize]
[Route("Plugins/MusicSuite/Assets")]
public sealed class AssetController : ControllerBase
{
    private const string JavaScriptContentType = "application/javascript; charset=utf-8";
    private const string HtmlContentType = "text/html; charset=utf-8";
    private const string CssContentType = "text/css; charset=utf-8";
    private const string SvgContentType = "image/svg+xml";

    /// <summary>
    /// Serves the Spotify-inspired MusicSuite dashboard HTML.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("spotify-dashboard.html")]
    public ActionResult GetSpotifyDashboardHtml()
    {
        return ServeSingleAsset("Jellyfin.Plugin.MusicSuite.Web.spotify-dashboard.html", HtmlContentType);
    }

    /// <summary>
    /// Serves the Spotify dashboard client script.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("spotify-dashboard.js")]
    public ActionResult GetSpotifyDashboardJs()
    {
        return ServeSingleAsset("Jellyfin.Plugin.MusicSuite.Web.spotify-dashboard.js", JavaScriptContentType);
    }

    /// <summary>
    /// Serves the Spotify theme stylesheet.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("spotify-theme.css")]
    public ActionResult GetSpotifyThemeCss()
    {
        return ServeSingleAsset("Jellyfin.Plugin.MusicSuite.Web.spotify-theme.css", CssContentType);
    }

    /// <summary>
    /// Serves the music redirect client script.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("music-redirect.js")]
    public ActionResult GetMusicRedirectJs()
    {
        return ServeSingleAsset("Jellyfin.Plugin.MusicSuite.Web.music-redirect.js", JavaScriptContentType);
    }

    /// <summary>
    /// Serves the legacy or enhanced albums view script.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("albums-view.js")]
    [HttpGet("AlbumsView.js")]
    public ActionResult GetAlbumsView()
    {
        return ServeSingleAsset("Jellyfin.Plugin.MusicSuite.Web.albums-view.js", JavaScriptContentType);
    }

    /// <summary>
    /// Serves the admin configuration page HTML.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("config.html")]
    public ActionResult GetConfigHtml()
    {
        return ServeSingleAsset("Jellyfin.Plugin.MusicSuite.Web.config.html", HtmlContentType);
    }

    /// <summary>
    /// Serves the custom MusicSuite SVG icon.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("icon")]
    [HttpGet("assets/musicsuite-icon.svg")]
    public ActionResult GetIcon()
    {
        return ServeSingleAsset("Jellyfin.Plugin.MusicSuite.Web.assets.musicsuite-icon.svg", SvgContentType);
    }

    private ActionResult ServeSingleAsset(string resourceName, string contentType)
    {
        var assembly = typeof(Plugin).Assembly;
        var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return NotFound(new { Message = $"Resource {resourceName} not found." });
        }

        Response.Headers.CacheControl = "private, no-cache";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(stream, contentType);
    }
}
