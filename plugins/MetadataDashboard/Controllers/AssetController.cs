using System;
using System.IO;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.MetadataDashboard.Controllers;

/// <summary>
/// Serves embedded web assets, icons, and pages for MetadataDashboard.
/// </summary>
[ApiController]
[Authorize]
[Route("Plugins/MetadataDashboard/Assets")]
public sealed class AssetController : ControllerBase
{
    private const string JavaScriptContentType = "application/javascript; charset=utf-8";
    private const string HtmlContentType = "text/html; charset=utf-8";
    private const string CssContentType = "text/css; charset=utf-8";
    private const string SvgContentType = "image/svg+xml";

    /// <summary>
    /// Serves the metadata dashboard standalone HTML.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("metadata-dashboard.html")]
    public ActionResult GetMetadataDashboardHtml()
    {
        return ServeSingleAsset("Jellyfin.Plugin.MetadataDashboard.Web.metadata-dashboard.html", HtmlContentType);
    }

    /// <summary>
    /// Serves the metadata dashboard client script.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("metadata-dashboard.js")]
    public ActionResult GetMetadataDashboardJs()
    {
        return ServeSingleAsset("Jellyfin.Plugin.MetadataDashboard.Web.metadata-dashboard.js", JavaScriptContentType);
    }

    /// <summary>
    /// Serves the metadata dashboard stylesheet.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("metadata-dashboard.css")]
    public ActionResult GetMetadataDashboardCss()
    {
        return ServeSingleAsset("Jellyfin.Plugin.MetadataDashboard.Web.metadata-dashboard.css", CssContentType);
    }

    /// <summary>
    /// Serves the admin config HTML.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("config.html")]
    public ActionResult GetConfigHtml()
    {
        return ServeSingleAsset("Jellyfin.Plugin.MetadataDashboard.Web.config.html", HtmlContentType);
    }

    /// <summary>
    /// Serves the SVG squircle icon.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("icon")]
    [HttpGet("assets/metadata-dashboard-icon.svg")]
    public ActionResult GetIcon()
    {
        return ServeSingleAsset("Jellyfin.Plugin.MetadataDashboard.Web.assets.metadata-dashboard-icon.svg", SvgContentType);
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
