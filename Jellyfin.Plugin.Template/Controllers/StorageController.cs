using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Template.Controllers;

/// <summary>
/// Storage dashboard endpoint.
/// </summary>
[ApiController]
[Route("Plugins/StorageDashboard")]
public class StorageController : ControllerBase
{
    /// <summary>
    /// Gets storage and library data for dashboard UI.
    /// </summary>
    /// <returns>Dashboard data.</returns>
    [HttpGet("Storage")]
    public ActionResult<StorageDashboardResponse> GetStorage()
    {
        var libraryManager = HttpContext.RequestServices.GetService<ILibraryManager>();
        if (libraryManager is null)
        {
            return StatusCode(500, "ILibraryManager service is not available.");
        }

        var virtualFolders = libraryManager.GetVirtualFolders().ToArray();

        var libraries = virtualFolders
            .Select(v => new LibraryEntry
            {
                Name = string.IsNullOrWhiteSpace(v.Name) ? "Library" : v.Name,
                Type = NormalizeLibraryType(v.CollectionType?.ToString()),
                Paths = (v.Locations ?? Array.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            })
            .ToArray();

        var pathDriveMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in libraries.SelectMany(l => l.Paths).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var key = GetDriveKey(path);
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            if (!pathDriveMap.TryGetValue(key, out var paths))
            {
                paths = new List<string>();
                pathDriveMap[key] = paths;
            }

            paths.Add(path);
        }

        var drives = new List<DriveEntry>();
        foreach (var kv in pathDriveMap)
        {
            var key = kv.Key;
            var drive = TryReadDrive(key);
            if (drive is null)
            {
                continue;
            }

            drives.Add(drive);
        }

        if (drives.Count == 0)
        {
            foreach (var driveInfo in DriveInfo.GetDrives().Where(d => d.IsReady))
            {
                var safeDrive = TryReadDrive(driveInfo.Name);
                if (safeDrive is not null)
                {
                    drives.Add(safeDrive);
                }
            }
        }

        return Ok(new StorageDashboardResponse
        {
            Drives = drives
                .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            Libraries = libraries
        });
    }

    private static DriveEntry? TryReadDrive(string root)
    {
        try
        {
            var info = new DriveInfo(root);
            if (!info.IsReady)
            {
                return null;
            }

            var total = info.TotalSize;
            var free = info.AvailableFreeSpace;
            return new DriveEntry
            {
                Name = root,
                Label = string.IsNullOrWhiteSpace(info.VolumeLabel) ? root : info.VolumeLabel,
                TotalBytes = total,
                FreeBytes = free,
                UsedBytes = Math.Max(0, total - free)
            };
        }
        catch (Exception)
        {
            return null;
        }
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
            "books" => "books",
            "photos" => "photos",
            "homevideos" => "homevideos",
            "mixed" => "mixed",
            _ => "other"
        };
    }

    private static string GetDriveKey(string path)
    {
        try
        {
            var full = Path.GetFullPath(path).Replace('\\', '/');

            if (full.Length >= 2 && full[1] == ':')
            {
                return full.Substring(0, 3);
            }

            if (full.StartsWith("/mnt/", StringComparison.OrdinalIgnoreCase)
                || full.StartsWith("/media/", StringComparison.OrdinalIgnoreCase))
            {
                var parts = full.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    return "/" + parts[0] + "/" + parts[1];
                }
            }

            return "/";
        }
        catch
        {
            return string.Empty;
        }
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
