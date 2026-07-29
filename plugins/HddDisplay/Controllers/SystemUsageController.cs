using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.HddDisplay.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.HddDisplay.Controllers;

/// <summary>
/// Exposes exclusive Jellyfin system-path usage.
/// </summary>
[ApiController]
[Route("Plugins/HddDisplay/SystemUsage")]
public class SystemUsageController : ControllerBase
{
    /// <summary>
    /// Gets usage for cache, metadata, transcoding, log and other Jellyfin system paths.
    /// </summary>
    /// <param name="refresh">Whether to bypass the system-path cache.</param>
    /// <returns>System-path usage grouped by mount.</returns>
    [HttpGet]
    public ActionResult<SystemUsageAggregationResult> GetSystemUsage([FromQuery] bool? refresh)
    {
        var applicationPaths = HttpContext.RequestServices.GetService<IServerApplicationPaths>();
        if (applicationPaths is null)
        {
            return StatusCode(500, "IServerApplicationPaths service is not available.");
        }

        var diagnostics = new List<string>();
        var configurationManager = HttpContext.RequestServices.GetService<IServerConfigurationManager>();
        var inputs = CreateInputs(applicationPaths, configurationManager, diagnostics);
        var configuration = Plugin.Instance?.Configuration;
        var result = SystemUsageAggregator.Calculate(
            inputs,
            configuration?.SystemScanCacheMinutes ?? 30,
            refresh == true,
            HttpContext.RequestAborted,
            configuration?.SystemScanTimeoutSeconds ?? 60);
        result.Diagnostics = result.Diagnostics
            .Concat(diagnostics)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return Ok(result);
    }

    /// <summary>
    /// Clears the system-path usage cache.
    /// </summary>
    /// <returns>A cache clear result.</returns>
    [HttpPost("Cache/Clear")]
    public ActionResult<CacheClearResponse> ClearSystemUsageCache()
    {
        SystemUsageAggregator.ClearCache();
        return Ok(new CacheClearResponse
        {
            ClearedAtUtc = DateTimeOffset.UtcNow,
            Message = "HDD Display system-path scan cache cleared."
        });
    }

    private static IReadOnlyList<SystemUsageScanInput> CreateInputs(
        IServerApplicationPaths applicationPaths,
        IServerConfigurationManager? configurationManager,
        List<string> diagnostics)
    {
        var candidates = new List<(string Category, string Path)>
        {
            ("image-cache", applicationPaths.ImageCachePath),
            ("metadata", applicationPaths.InternalMetadataPath),
            ("logs", applicationPaths.LogDirectoryPath),
            ("temp", applicationPaths.TempDirectory),
            ("plugins", applicationPaths.PluginsPath),
            ("configuration", applicationPaths.ConfigurationDirectoryPath),
            ("cache", applicationPaths.CachePath),
            ("program-data", applicationPaths.ProgramDataPath),
            ("web", applicationPaths.WebPath)
        };

        var transcodePath = TryGetTranscodePath(configurationManager, diagnostics);
        if (!string.IsNullOrWhiteSpace(transcodePath))
        {
            candidates.Insert(1, ("transcodes", transcodePath));
        }

        var inputs = new List<SystemUsageScanInput>();
        foreach (var candidate in candidates.Where(candidate => !string.IsNullOrWhiteSpace(candidate.Path)))
        {
            var resolution = MountResolver.Resolve(candidate.Path);
            if (!resolution.IsResolved || string.IsNullOrWhiteSpace(resolution.MountPath))
            {
                diagnostics.Add($"Could not resolve system path '{candidate.Category}': {candidate.Path}: {resolution.Diagnostic}");
                continue;
            }

            inputs.Add(new SystemUsageScanInput
            {
                Category = candidate.Category,
                Path = candidate.Path,
                MountPath = resolution.MountPath
            });
        }

        return inputs;
    }

    private static string TryGetTranscodePath(
        IServerConfigurationManager? configurationManager,
        List<string> diagnostics)
    {
        if (configurationManager is null)
        {
            diagnostics.Add("IServerConfigurationManager service is unavailable; transcode usage was not scanned.");
            return string.Empty;
        }

        try
        {
            return configurationManager.GetTranscodePath();
        }
        catch (IOException exception)
        {
            diagnostics.Add($"Failed to resolve the transcode path: {exception.Message}");
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add($"Access denied while resolving the transcode path: {exception.Message}");
        }
        catch (NotSupportedException exception)
        {
            diagnostics.Add($"The configured transcode path is invalid: {exception.Message}");
        }
        catch (InvalidOperationException exception)
        {
            diagnostics.Add($"The configured transcode path overlaps another Jellyfin system path: {exception.Message}");
        }

        return string.Empty;
    }
}
