using System;
using System.IO;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.HddDisplay.Controllers;

/// <summary>
/// Serves embedded HDD Display web assets.
/// </summary>
[ApiController]
[Route("Plugins/HddDisplay/Assets")]
public class AssetController : HddDisplayAdminControllerBase
{
    private const string DashboardWidgetResource = "Jellyfin.Plugin.HddDisplay.Web.dashboard-widget.js";
    private const string SystemUsageResource = "Jellyfin.Plugin.HddDisplay.Web.system-usage-extension.js";
    private const string JavaScriptContentType = "application/javascript; charset=utf-8";
    private static readonly string[] DashboardResources =
    {
        DashboardWidgetResource,
        SystemUsageResource
    };

    /// <summary>
    /// Gets the current dashboard widget with revalidation enabled.
    /// </summary>
    /// <returns>The embedded dashboard widget.</returns>
    [HttpGet("DashboardWidget.js")]
    public ActionResult GetDashboardWidget()
    {
        return CreateAssetResponse(immutable: false);
    }

    /// <summary>
    /// Gets the dashboard widget through a versioned, immutable URL.
    /// </summary>
    /// <param name="version">Expected plugin assembly version.</param>
    /// <returns>The embedded dashboard widget.</returns>
    [HttpGet("{version}/DashboardWidget.js")]
    public ActionResult GetVersionedDashboardWidget(string version)
    {
        var currentVersion = CurrentVersion();
        if (!string.Equals(version, currentVersion, StringComparison.Ordinal))
        {
            return NotFound(new
            {
                Message = "The requested HDD Display asset version is not installed.",
                CurrentVersion = currentVersion
            });
        }

        return CreateAssetResponse(immutable: true);
    }

    /// <summary>
    /// Gets the HDD Display detail dashboard HTML.
    /// </summary>
    /// <returns>The detail HTML page.</returns>
    [HttpGet("hdd-display.html")]
    public ActionResult GetHddDisplayHtml()
    {
        return ServeSingleAsset("Jellyfin.Plugin.HddDisplay.Web.hdd-display.html", "text/html; charset=utf-8");
    }

    /// <summary>
    /// Gets the HDD Display detail dashboard JavaScript.
    /// </summary>
    /// <returns>The detail JavaScript.</returns>
    [HttpGet("hdd-display.js")]
    public ActionResult GetHddDisplayJs()
    {
        return ServeSingleAsset("Jellyfin.Plugin.HddDisplay.Web.hdd-display.js", JavaScriptContentType);
    }

    /// <summary>
    /// Gets the HDD Display detail dashboard CSS.
    /// </summary>
    /// <returns>The detail CSS.</returns>
    [HttpGet("hdd-display.css")]
    public ActionResult GetHddDisplayCss()
    {
        return ServeSingleAsset("Jellyfin.Plugin.HddDisplay.Web.hdd-display.css", "text/css; charset=utf-8");
    }

    /// <summary>
    /// Gets the HDD Display custom SVG icon.
    /// </summary>
    /// <returns>The SVG icon.</returns>
    [HttpGet("icon")]
    [HttpGet("assets/hdd-icon.svg")]
    public ActionResult GetHddDisplayIcon()
    {
        return ServeSingleAsset("Jellyfin.Plugin.HddDisplay.Web.assets.hdd-icon.svg", "image/svg+xml");
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

    private ActionResult CreateAssetResponse(bool immutable)
    {
        var stream = CreateDashboardBundle();
        if (stream is null)
        {
            return StatusCode(500, new
            {
                Message = "One or more embedded HDD Display dashboard assets are unavailable."
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

    private static MemoryStream? CreateDashboardBundle()
    {
        var assembly = typeof(Plugin).Assembly;
        var output = new MemoryStream();
        foreach (var resourceName in DashboardResources)
        {
            using var resource = assembly.GetManifestResourceStream(resourceName);
            if (resource is null)
            {
                output.Dispose();
                return null;
            }

            resource.CopyTo(output);
            output.WriteByte((byte)'\n');
        }

        output.Position = 0;
        return output;
    }

    private static string CurrentVersion()
    {
        return typeof(Plugin).Assembly
            .GetName()
            .Version?
            .ToString()
            ?? "0.0.0.0";
    }
}
