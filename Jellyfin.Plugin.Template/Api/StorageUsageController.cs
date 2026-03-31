using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Template.Api;

/// <summary>
/// Exposes storage usage information for drives used by Jellyfin libraries.
/// </summary>
[ApiController]
[Route("Plugins/HddDisplay/Storage")]
public class StorageUsageController : ControllerBase
{
    private readonly ILibraryManager _libraryManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="StorageUsageController"/> class.
    /// </summary>
    /// <param name="libraryManager">Library manager.</param>
    public StorageUsageController(ILibraryManager libraryManager)
    {
        _libraryManager = libraryManager;
    }

    /// <summary>
    /// Gets usage for mounted drives that contain Jellyfin library paths.
    /// </summary>
    /// <returns>A list of storage usage entries.</returns>
    [HttpGet("Usage")]
    public ActionResult<IReadOnlyList<StorageUsageEntry>> GetUsage()
    {
        var libraryPaths = _libraryManager
            .GetVirtualFolders()
            .SelectMany(v => v.Locations ?? Array.Empty<string>())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(GetPathComparer())
            .ToArray();

        var entries = new List<StorageUsageEntry>();

        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
        {
            var matchingPaths = libraryPaths
                .Where(path => IsPathOnDrive(path, drive.Name))
                .ToArray();

            if (matchingPaths.Length == 0)
            {
                continue;
            }

            long totalBytes;
            long freeBytes;

            try
            {
                totalBytes = drive.TotalSize;
                freeBytes = drive.AvailableFreeSpace;
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            var usedBytes = Math.Max(0, totalBytes - freeBytes);
            var usedPercent = totalBytes == 0 ? 0 : (double)usedBytes / totalBytes * 100;

            entries.Add(new StorageUsageEntry
            {
                DriveName = drive.Name,
                VolumeLabel = drive.VolumeLabel,
                TotalBytes = totalBytes,
                UsedBytes = usedBytes,
                FreeBytes = freeBytes,
                UsedPercent = Math.Round(usedPercent, 2),
                LibraryPaths = matchingPaths
            });
        }

        return Ok(entries.OrderBy(e => e.DriveName, GetStringComparer()).ToArray());
    }

    private static bool IsPathOnDrive(string path, string driveName)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(driveName))
        {
            return false;
        }

        string normalizedPath;
        string normalizedDrive;

        try
        {
            normalizedPath = Path.GetFullPath(path).Replace('\\', '/');
            normalizedDrive = Path.GetFullPath(driveName).Replace('\\', '/');
        }
        catch (Exception)
        {
            return false;
        }

        if (!normalizedDrive.EndsWith('/'))
        {
            normalizedDrive += "/";
        }

        var comparer = GetStringComparison();
        return normalizedPath.StartsWith(normalizedDrive, comparer)
            || string.Equals(normalizedPath, normalizedDrive.TrimEnd('/'), GetStringComparison());
    }

    private static StringComparer GetPathComparer()
        => RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    private static StringComparer GetStringComparer()
        => RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    private static StringComparison GetStringComparison()
        => RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
}

/// <summary>
/// Single storage usage result entry.
/// </summary>
public class StorageUsageEntry
{
    /// <summary>
    /// Gets or sets drive name or mountpoint root.
    /// </summary>
    public string DriveName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets volume label.
    /// </summary>
    public string VolumeLabel { get; set; } = string.Empty;

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
    /// Gets or sets used percentage.
    /// </summary>
    public double UsedPercent { get; set; }

    /// <summary>
    /// Gets or sets Jellyfin library paths on this drive.
    /// </summary>
    public IReadOnlyList<string> LibraryPaths { get; set; } = Array.Empty<string>();
}
