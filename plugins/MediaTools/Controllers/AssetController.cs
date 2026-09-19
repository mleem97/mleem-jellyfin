using System;
using System.IO;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.MediaTools.Controllers;

/// <summary>
/// Serves embedded MediaTools web assets and icons.
/// </summary>
[ApiController]
[Route("Plugins/MediaTools/Assets")]
public class AssetController : MediaToolsAdminControllerBase
{
    private const string JavaScriptContentType = "application/javascript; charset=utf-8";
    private const string HtmlContentType = "text/html; charset=utf-8";
    private const string CssContentType = "text/css; charset=utf-8";
    private const string SvgContentType = "image/svg+xml";

    /// <summary>
    /// Serves the MediaTools admin dashboard HTML.
    /// </summary>
    [HttpGet("mediatools.html")]
    public ActionResult GetMediaToolsHtml()
    {
        return ServeSingleAsset("Jellyfin.Plugin.MediaTools.Web.mediatools.html", HtmlContentType);
    }

    /// <summary>
    /// Serves the MediaTools admin dashboard JavaScript.
    /// </summary>
    [HttpGet("mediatools.js")]
    public ActionResult GetMediaToolsJs()
    {
        return ServeSingleAsset("Jellyfin.Plugin.MediaTools.Web.mediatools.js", JavaScriptContentType);
    }

    /// <summary>
    /// Serves the MediaTools admin dashboard CSS.
    /// </summary>
    [HttpGet("mediatools.css")]
    public ActionResult GetMediaToolsCss()
    {
        return ServeSingleAsset("Jellyfin.Plugin.MediaTools.Web.mediatools.css", CssContentType);
    }

    /// <summary>
    /// Serves the MediaTools custom SVG icon.
    /// </summary>
    [HttpGet("icon")]
    [HttpGet("assets/mediatools-icon.svg")]
    public ActionResult GetMediaToolsIcon()
    {
        return ServeSingleAsset("Jellyfin.Plugin.MediaTools.Web.assets.mediatools-icon.svg", SvgContentType);
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
