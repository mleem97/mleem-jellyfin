using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Jellyfin.Plugin.HddDisplay.Services;

/// <summary>
/// Resolves Jellyfin library paths to host/container mount points.
/// </summary>
public class MountResolver
{
    private const string MountInfoPath = "/proc/self/mountinfo";

    /// <summary>
    /// Resolves a library path to the deepest matching mount point.
    /// </summary>
    /// <param name="path">Library path.</param>
    /// <returns>Mount resolution result.</returns>
    public MountResolution Resolve(string path)
    {
        var normalizedPath = NormalizePath(path);
        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            return MountResolution.Unresolved(path, string.Empty, "Path could not be normalized.");
        }

        if (IsWindowsPath(normalizedPath))
        {
            var root = Path.GetPathRoot(normalizedPath) ?? normalizedPath;
            return new MountResolution
            {
                LibraryPath = path,
                NormalizedPath = normalizedPath,
                MountPath = root,
                Source = root,
                FileSystemType = "windows",
                ResolutionProvider = "DriveInfo",
                IsResolved = true,
                Diagnostic = "Resolved from Windows drive root."
            };
        }

        var mounts = ReadMounts();
        var match = mounts
            .Where(mount => IsPathOnMount(normalizedPath, mount.MountPath))
            .OrderByDescending(mount => mount.MountPath.Length)
            .FirstOrDefault();

        if (match is not null)
        {
            return new MountResolution
            {
                LibraryPath = path,
                NormalizedPath = normalizedPath,
                MountPath = match.MountPath,
                Source = match.Source,
                FileSystemType = match.FileSystemType,
                ResolutionProvider = "mountinfo",
                IsResolved = true,
                Diagnostic = "Resolved from /proc/self/mountinfo."
            };
        }

        return new MountResolution
        {
            LibraryPath = path,
            NormalizedPath = normalizedPath,
            MountPath = "/",
            Source = "/",
            FileSystemType = "unknown",
            ResolutionProvider = "fallback",
            IsResolved = true,
            Diagnostic = "No mountinfo match found. Falling back to root mount."
        };
    }

    private static IReadOnlyList<MountInfoEntry> ReadMounts()
    {
        try
        {
            if (!File.Exists(MountInfoPath))
            {
                return Array.Empty<MountInfoEntry>();
            }

            return File.ReadAllLines(MountInfoPath)
                .Select(ParseMountInfoLine)
                .Where(entry => entry is not null)
                .Cast<MountInfoEntry>()
                .ToArray();
        }
        catch (IOException)
        {
            return Array.Empty<MountInfoEntry>();
        }
        catch (UnauthorizedAccessException)
        {
            return Array.Empty<MountInfoEntry>();
        }
    }

    private static MountInfoEntry? ParseMountInfoLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        var separatorIndex = line.IndexOf(" - ", StringComparison.Ordinal);
        if (separatorIndex < 0)
        {
            return null;
        }

        var left = line[..separatorIndex].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var right = line[(separatorIndex + 3)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (left.Length < 5 || right.Length < 3)
        {
            return null;
        }

        return new MountInfoEntry
        {
            MountPath = NormalizePath(UnescapeMountInfoValue(left[4])),
            FileSystemType = right[0],
            Source = UnescapeMountInfoValue(right[1])
        };
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        try
        {
            return Path.GetFullPath(path).Replace('\\', '/').TrimEnd('/');
        }
        catch (ArgumentException)
        {
            return path.Replace('\\', '/').TrimEnd('/');
        }
        catch (NotSupportedException)
        {
            return path.Replace('\\', '/').TrimEnd('/');
        }
    }

    private static bool IsPathOnMount(string path, string mountPath)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(mountPath))
        {
            return false;
        }

        if (string.Equals(path, mountPath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var normalizedMount = mountPath.TrimEnd('/');
        return path.StartsWith(normalizedMount + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWindowsPath(string path)
    {
        return path.Length >= 2 && path[1] == ':';
    }

    private static string UnescapeMountInfoValue(string value)
    {
        return value
            .Replace("\\040", " ", StringComparison.Ordinal)
            .Replace("\\011", "\t", StringComparison.Ordinal)
            .Replace("\\012", "\n", StringComparison.Ordinal)
            .Replace("\\134", "\\", StringComparison.Ordinal);
    }
}

/// <summary>
/// Mount resolution result for one Jellyfin library path.
/// </summary>
public class MountResolution
{
    /// <summary>
    /// Gets or sets the original library path.
    /// </summary>
    public string LibraryPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the normalized library path.
    /// </summary>
    public string NormalizedPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the resolved mount path.
    /// </summary>
    public string MountPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the mount source.
    /// </summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the filesystem type.
    /// </summary>
    public string FileSystemType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the resolution provider.
    /// </summary>
    public string ResolutionProvider { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the path was resolved.
    /// </summary>
    public bool IsResolved { get; set; }

    /// <summary>
    /// Gets or sets a diagnostic note.
    /// </summary>
    public string Diagnostic { get; set; } = string.Empty;

    /// <summary>
    /// Creates an unresolved result.
    /// </summary>
    /// <param name="libraryPath">Original library path.</param>
    /// <param name="normalizedPath">Normalized path.</param>
    /// <param name="diagnostic">Diagnostic note.</param>
    /// <returns>Unresolved mount result.</returns>
    public static MountResolution Unresolved(string libraryPath, string normalizedPath, string diagnostic)
    {
        return new MountResolution
        {
            LibraryPath = libraryPath,
            NormalizedPath = normalizedPath,
            IsResolved = false,
            ResolutionProvider = "none",
            Diagnostic = diagnostic
        };
    }
}

/// <summary>
/// Parsed mountinfo entry.
/// </summary>
public class MountInfoEntry
{
    /// <summary>
    /// Gets or sets the mount path.
    /// </summary>
    public string MountPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the filesystem type.
    /// </summary>
    public string FileSystemType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the mount source.
    /// </summary>
    public string Source { get; set; } = string.Empty;
}
