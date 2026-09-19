using System;
using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.MusicToolkit.Controllers;

/// <summary>
/// Serves MusicToolkit web assets (dashboard HTML, JS, CSS) from embedded resources.
/// </summary>
[ApiController]
[Route("MusicToolkit/asset")]
public sealed class AssetController : ControllerBase
{
    private const string DashboardResource = "Jellyfin.Plugin.MusicToolkit.Web.spotify-dashboard.html";
    private const string DashboardJsResource = "Jellyfin.Plugin.MusicToolkit.Web.spotify-dashboard.js";
    private const string ThemeCssResource = "Jellyfin.Plugin.MusicToolkit.Web.spotify-theme.css";
    private const string RedirectJsResource = "Jellyfin.Plugin.MusicToolkit.Web.music-redirect.js";

    /// <summary>
    /// Serves the Spotify music dashboard page.
    /// </summary>
    /// <returns>The dashboard HTML.</returns>
    [HttpGet("dashboard")]
    [Produces("text/html")]
    public ContentResult Dashboard()
    {
        var html = ReadResource(DashboardResource);
        if (html is null)
        {
            return Content("<html><body><h3>MusicToolkit dashboard resource missing.</h3></body></html>", "text/html");
        }

        // Rewrite relative asset references to plugin asset URLs.
        html = html
            .Replace("spotify-theme.css", "/MusicToolkit/asset/spotify-theme.css", StringComparison.Ordinal)
            .Replace("spotify-dashboard.js", "/MusicToolkit/asset/spotify-dashboard.js", StringComparison.Ordinal);
        return Content(html, "text/html");
    }

    /// <summary>
    /// Serves the Spotify dashboard JavaScript.
    /// </summary>
    /// <returns>The dashboard JS.</returns>
    [HttpGet("spotify-dashboard.js")]
    [Produces("application/javascript")]
    public ContentResult DashboardJs()
    {
        return Content(ReadResource(DashboardJsResource) ?? string.Empty, "application/javascript");
    }

    /// <summary>
    /// Serves the Spotify theme stylesheet.
    /// </summary>
    /// <returns>The theme CSS.</returns>
    [HttpGet("spotify-theme.css")]
    [Produces("text/css")]
    public ContentResult ThemeCss()
    {
        return Content(ReadResource(ThemeCssResource) ?? string.Empty, "text/css");
    }

    /// <summary>
    /// Serves the music redirect JavaScript.
    /// </summary>
    /// <returns>The redirect JS.</returns>
    [HttpGet("music-redirect.js")]
    [Produces("application/javascript")]
    public ContentResult RedirectJs()
    {
        return Content(ReadResource(RedirectJsResource) ?? string.Empty, "application/javascript");
    }

    private static string? ReadResource(string name)
    {
        return typeof(AssetController).Assembly.GetManifestResourceStream(name) is { } stream
            ? new StreamReader(stream, Encoding.UTF8).ReadToEnd()
            : null;
    }
}
