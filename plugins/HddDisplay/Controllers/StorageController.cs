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
public class StorageController : ControllerBase
{
    /// <summary>
    /// Gets storage and library data for the dashboard UI.
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

        var drives = mountResolutions
            .Where(resolution => resolution.IsResolved && !string.IsNullOrWhiteSpace(resolution.MountPath))
            .GroupBy(resolution => resolution.MountPath, StringComparer.OrdinalIgnoreCase)
            .Select(group => TryReadDrive(group.Key, group.ToArray()))
            .Where(drive => drive is not null)
            .Cast<DriveEntry>()
            .OrderBy(drive => drive.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Ok(new StorageDashboardResponse
        {
            Drives = drives,
            Libraries = libraries,
            Mounts = mountResolutions,
            Note = "Library paths are resolved through /proc/self/mountinfo where available, with DriveInfo fallback for non-Linux paths."
        });
    }

    private static DriveEntry? TryReadDrive(string root, IReadOnlyList<MountResolution> resolutions)
    {
        try
        {
            var info = new DriveInfo(root);
            if (!info.IsReady)
            {
                return CreateUnavailableDrive(root, resolutions, "DriveInfo reports the mount as not ready.");
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
                LibraryPaths = resolutions.Select(resolution => resolution.LibraryPath).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray(),
                Diagnostics = resolutions.Select(resolution => resolution.Diagnostic).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                TotalBytes = total,
                FreeBytes = free,
                UsedBytes = Math.Max(0, total - free),
                IsReady = true
            };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return CreateUnavailableDrive(root, resolutions, exception.Message);
        }
    }

    private static DriveEntry CreateUnavailableDrive(string root, IReadOnlyList<MountResolution> resolutions, string diagnostic)
    {
        return new DriveEntry
        {
            Name = root,
            Label = root,
            Source = resolutions.FirstOrDefault()?.Source ?? string.Empty,
            FileSystemType = resolutions.FirstOrDefault()?.FileSystemType ?? string.Empty,
            ResolutionProvider = resolutions.FirstOrDefault()?.ResolutionProvider ?? string.Empty,
            LibraryPaths = resolutions.Select(resolution => resolution.LibraryPath).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray(),
            Diagnostics = resolutions.Select(resolution => resolution.Diagnostic).Append(diagnostic).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            IsReady = false
        };
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
    /// Gets or sets a diagnostic note.
    /// </summary>
    public string Note { get; set; } = string.Empty;
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
