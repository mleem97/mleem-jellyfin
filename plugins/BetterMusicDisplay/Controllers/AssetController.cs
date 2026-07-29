using System;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.BetterMusicDisplay.Controllers;

/// <summary>
/// Serves embedded Better MusicDisplay web assets.
/// </summary>
[ApiController]
[Authorize]
[Route("Plugins/BetterMusicDisplay/Assets")]
public sealed class AssetController : ControllerBase
{
    private const string AlbumsViewResource = "Jellyfin.Plugin.BetterMusicDisplay.Web.albums-view.js";
    private const string JavaScriptContentType = "application/javascript; charset=utf-8";

    /// <summary>
    /// Gets the current Albums view loader with revalidation enabled.
    /// </summary>
    /// <returns>The embedded Albums view loader.</returns>
    [HttpGet("AlbumsView.js")]
    public ActionResult GetAlbumsView()
    {
        return CreateAssetResponse(immutable: false);
    }

    /// <summary>
    /// Gets the Albums view loader through an immutable versioned URL.
    /// </summary>
    /// <param name="version">Expected plugin assembly version.</param>
    /// <returns>The embedded Albums view loader.</returns>
    [HttpGet("{version}/AlbumsView.js")]
    public ActionResult GetVersionedAlbumsView(string version)
    {
        var currentVersion = CurrentVersion();
        if (!string.Equals(version, currentVersion, StringComparison.Ordinal))
        {
            return NotFound(new
            {
                Message = "The requested Better MusicDisplay asset version is not installed.",
                CurrentVersion = currentVersion
            });
        }

        return CreateAssetResponse(immutable: true);
    }

    private ActionResult CreateAssetResponse(bool immutable)
    {
        var stream = typeof(Plugin).Assembly.GetManifestResourceStream(AlbumsViewResource);
        if (stream is null)
        {
            return StatusCode(500, new
            {
                Message = "The embedded Better MusicDisplay Albums asset is unavailable."
            });
        }

        var version = CurrentVersion();
        Response.Headers.ETag = string.Concat("\"", version, "\"");
        Response.Headers.CacheControl = immutable
            ? "private, max-age=31536000, immutable"
            : "private, no-cache";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(stream, JavaScriptContentType, enableRangeProcessing: false);
    }

    private static string CurrentVersion()
    {
        return typeof(Plugin).Assembly.GetName().Version?.ToString() ?? "0.0.0.0";
    }
}
