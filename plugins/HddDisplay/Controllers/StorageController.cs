using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.HddDisplay.Services;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.HddDisplay.Controllers;

/// <summary>
/// Storage endpoints for HDD Display.
/// </summary>
[ApiController]
[Route("Plugins/HddDisplay")]
public class StorageController : HddDisplayAdminControllerBase
{
    /// <summary>
    /// Gets storage and library data for the dashboard UI.
    /// </summary>
    /// <param name="refresh">Whether the storage and GPU caches should be bypassed.</param>
    /// <returns>Dashboard data.</returns>
    [HttpGet("Storage")]
    public ActionResult<StorageDashboardResponse> GetStorage([FromQuery] bool? refresh)
    {
        return BuildStorageResponse(refresh == true);
    }

    /// <summary>
    /// Gets storage and GPU data for the Admin Dashboard widget.
    /// </summary>
    /// <param name="refresh">Whether the storage and GPU caches should be bypassed.</param>
    /// <returns>Admin Dashboard overview data.</returns>
    [HttpGet("AdminDashboard/Overview")]
    public ActionResult<StorageDashboardResponse> GetAdminDashboardOverview([FromQuery] bool? refresh)
    {
        return BuildStorageResponse(refresh == true);
    }

    /// <summary>
    /// Clears the in-memory storage and GPU caches.
    /// </summary>
    /// <returns>Cache clear result.</returns>
    [HttpPost("Storage/Cache/Clear")]
    public ActionResult<CacheClearResponse> ClearStorageCache()
    {
        MediaUsageAggregator.ClearCache();
        GpuUsageCache.Clear();
        return Ok(new CacheClearResponse
        {
            ClearedAtUtc = DateTimeOffset.UtcNow,
            Message = "HDD Display storage and GPU caches cleared."
        });
    }

    private ActionResult<StorageDashboardResponse> BuildStorageResponse(bool refresh)
    {
        var libraryManager = HttpContext.RequestServices.GetService<ILibraryManager>();
        if (libraryManager is null)
        {
            return StatusCode(500, "ILibraryManager service is not available.");
        }

        var configuration = Plugin.Instance?.Configuration;
        var virtualFolders = libraryManager.GetVirtualFolders().ToArray();
        var libraries = virtualFolders
            .Select(v => new LibraryEntry
            {
                Name = string.IsNullOrWhiteSpace(v.Name) ? "Library" : v.Name,
                Type = NormalizeLibraryType(v.CollectionType?.ToString()),
                Paths = (v.Locations ?? Array.Empty<string>())
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToArray()
            })
            .ToArray();

        var mountResolutions = libraries
            .SelectMany(library => library.Paths)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(MountResolver.Resolve)
            .ToArray();

        var usageInputs = libraries
            .SelectMany(library => library.Paths.Select(path => CreateUsageInput(library, path, mountResolutions)))
            .Where(input => input is not null)
            .Cast<MediaUsageScanInput>()
            .ToArray();

        var usage = MediaUsageAggregator.Calculate(
            usageInputs,
            configuration?.StorageScanCacheMinutes ?? 15,
            refresh,
            HttpContext.RequestAborted,
            configuration?.StorageScanTimeoutSeconds ?? 120);
        var gpuProvider = new NvidiaSmiGpuUsageProvider(
            configuration?.GpuCommandTimeoutMilliseconds ?? 2500);
        var gpu = GpuUsageCache.GetSnapshot(
            gpuProvider,
            configuration?.GpuCacheSeconds ?? 5,
            refresh);

        var drives = mountResolutions
            .Where(resolution => resolution.IsResolved && !string.IsNullOrWhiteSpace(resolution.MountPath))
            .GroupBy(resolution => resolution.MountPath, StringComparer.OrdinalIgnoreCase)
            .Select(group => TryReadDrive(group.Key, group.ToArray(), usage.Entries))
            .Where(drive => drive is not null)
            .Cast<DriveEntry>()
            .OrderBy(drive => drive.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Ok(new StorageDashboardResponse
        {
            Drives = drives,
            Libraries = libraries,
            Mounts = mountResolutions,
            Usage = usage,
            Gpu = gpu,
            Note = usage.Completed
                ? "Library paths were resolved and scanned. NVIDIA telemetry is isolated behind a timeout and short-lived cache."
                : "The media scan returned partial results after cancellation or timeout; see usage diagnostics."
        });
    }

    private static MediaUsageScanInput? CreateUsageInput(
        LibraryEntry library,
        string path,
        IReadOnlyList<MountResolution> resolutions)
    {
        var resolution = resolutions.FirstOrDefault(item =>
            string.Equals(item.LibraryPath, path, StringComparison.OrdinalIgnoreCase));
        if (resolution is null || !resolution.IsResolved)
        {
            return null;
        }

        return new MediaUsageScanInput
        {
            LibraryName = library.Name,
            LibraryType = library.Type,
            LibraryPath = path,
            MountPath = resolution.MountPath
        };
    }

    private static DriveEntry? TryReadDrive(
        string root,
        IReadOnlyList<MountResolution> resolutions,
        IReadOnlyList<MediaUsageEntry> usageEntries)
    {
        try
        {
            var info = new DriveInfo(root);
            if (!info.IsReady)
            {
                return CreateUnavailableDrive(
                    root,
                    resolutions,
                    usageEntries,
                    "DriveInfo reports the mount as not ready.");
            }

            var total = info.TotalSize;
            var free = info.AvailableFreeSpace;
            return new DriveEntry
            {
                Name = root,
                Label = string.IsNullOrWhiteSpace(info.VolumeLabel) ? root : info.VolumeLabel,
                Source = resolutions.FirstOrDefault()?.Source ?? string.Empty,
                FileSystemType = resolutions.FirstOrDefault()?.FileSystemType ?? string.Empty,
                ResolutionProvider = resolutions.FirstOrDefault()?.ResolutionProvider ?? string.Empty,
                LibraryPaths = resolutions
                    .Select(resolution => resolution.LibraryPath)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                Usage = UsageForMount(root, usageEntries),
                Diagnostics = resolutions
                    .Select(resolution => resolution.Diagnostic)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                TotalBytes = total,
                FreeBytes = free,
                UsedBytes = Math.Max(0, total - free),
                IsReady = true
            };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return CreateUnavailableDrive(root, resolutions, usageEntries, exception.Message);
        }
    }

    private static DriveEntry CreateUnavailableDrive(
        string root,
        IReadOnlyList<MountResolution> resolutions,
        IReadOnlyList<MediaUsageEntry> usageEntries,
        string diagnostic)
    {
        return new DriveEntry
        {
            Name = root,
            Label = root,
            Source = resolutions.FirstOrDefault()?.Source ?? string.Empty,
            FileSystemType = resolutions.FirstOrDefault()?.FileSystemType ?? string.Empty,
            ResolutionProvider = resolutions.FirstOrDefault()?.ResolutionProvider ?? string.Empty,
            LibraryPaths = resolutions
                .Select(resolution => resolution.LibraryPath)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            Usage = UsageForMount(root, usageEntries),
            Diagnostics = resolutions
                .Select(resolution => resolution.Diagnostic)
                .Append(diagnostic)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            IsReady = false
        };
    }

    private static IReadOnlyList<MediaUsageEntry> UsageForMount(
        string root,
        IReadOnlyList<MediaUsageEntry> usageEntries)
    {
        return usageEntries
            .Where(entry => string.Equals(entry.MountPath, root, StringComparison.OrdinalIgnoreCase))
            .Select(entry => entry.Clone())
            .ToArray();
    }

    private static string NormalizeLibraryType(string? type)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            return "mixed";
        }

        return type.Trim().ToLowerInvariant() switch
        {
            "movies" => "movies",
            "tvshows" => "tvshows",
            "music" => "music",
            "homevideos" => "video",
            "mixed" => "mixed",
            _ => "other"
        };
    }
}

/// <summary>
/// Storage dashboard response.
/// </summary>
public class StorageDashboardResponse
{
    /// <summary>
    /// Gets or sets drives.
    /// </summary>
    public IReadOnlyList<DriveEntry> Drives { get; set; } = Array.Empty<DriveEntry>();

    /// <summary>
    /// Gets or sets libraries.
    /// </summary>
    public IReadOnlyList<LibraryEntry> Libraries { get; set; } = Array.Empty<LibraryEntry>();

    /// <summary>
    /// Gets or sets mount resolutions for diagnostics.
    /// </summary>
    public IReadOnlyList<MountResolution> Mounts { get; set; } = Array.Empty<MountResolution>();

    /// <summary>
    /// Gets or sets real media usage aggregation.
    /// </summary>
    public MediaUsageAggregationResult Usage { get; set; } = new();

    /// <summary>
    /// Gets or sets GPU usage telemetry.
    /// </summary>
    public GpuUsageSnapshot Gpu { get; set; } = new();

    /// <summary>
    /// Gets or sets a diagnostic note.
    /// </summary>
    public string Note { get; set; } = string.Empty;
}

/// <summary>
/// Cache clear response.
/// </summary>
public class CacheClearResponse
{
    /// <summary>
    /// Gets or sets clear timestamp.
    /// </summary>
    public DateTimeOffset ClearedAtUtc { get; set; }

    /// <summary>
    /// Gets or sets response message.
    /// </summary>
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Drive entry.
/// </summary>
public class DriveEntry
{
    /// <summary>
    /// Gets or sets drive path.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets label.
    /// </summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets mount source.
    /// </summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets filesystem type.
    /// </summary>
    public string FileSystemType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets resolution provider.
    /// </summary>
    public string ResolutionProvider { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets library paths on this drive.
    /// </summary>
    public IReadOnlyList<string> LibraryPaths { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Gets or sets media usage for this drive.
    /// </summary>
    public IReadOnlyList<MediaUsageEntry> Usage { get; set; } = Array.Empty<MediaUsageEntry>();

    /// <summary>
    /// Gets or sets diagnostics for this drive.
    /// </summary>
    public IReadOnlyList<string> Diagnostics { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Gets or sets total bytes.
    /// </summary>
    public long TotalBytes { get; set; }

    /// <summary>
    /// Gets or sets used bytes.
    /// </summary>
    public long UsedBytes { get; set; }

    /// <summary>
    /// Gets or sets free bytes.
    /// </summary>
    public long FreeBytes { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether DriveInfo reported this drive as ready.
    /// </summary>
    public bool IsReady { get; set; }
}

/// <summary>
/// Library entry.
/// </summary>
public class LibraryEntry
{
    /// <summary>
    /// Gets or sets library name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets library type.
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets paths.
    /// </summary>
    public IReadOnlyList<string> Paths { get; set; } = Array.Empty<string>();
}
