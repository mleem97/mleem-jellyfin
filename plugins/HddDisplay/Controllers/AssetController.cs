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
