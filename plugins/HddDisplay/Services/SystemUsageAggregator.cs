using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Jellyfin.Plugin.HddDisplay.Services;

/// <summary>
/// Aggregates exclusive byte usage for Jellyfin system paths.
/// </summary>
public static class SystemUsageAggregator
{
    private static readonly object CacheLock = new();
    private static CachedSystemUsage? CachedResult;

    /// <summary>
    /// Calculates system-path usage while excluding nested configured paths from their parents.
    /// </summary>
    /// <param name="inputs">System paths ordered from most specific to least specific.</param>
    /// <param name="cacheMinutes">Cache lifetime in minutes.</param>
    /// <param name="forceRefresh">Whether to bypass the cache.</param>
    /// <returns>Exclusive system usage data.</returns>
    public static SystemUsageAggregationResult Calculate(
        IReadOnlyList<SystemUsageScanInput> inputs,
        int cacheMinutes,
        bool forceRefresh)
    {
        var normalizedInputs = NormalizeInputs(inputs);
        var cacheKey = BuildCacheKey(normalizedInputs);
        if (!forceRefresh && TryGetCached(cacheKey, cacheMinutes, out var cached))
        {
            return cached;
        }

        var diagnostics = new List<string>();
        if (forceRefresh)
        {
            diagnostics.Add("System-path scan cache was bypassed by request.");
        }

        var entries = new List<SystemUsageEntry>();
        foreach (var input in normalizedInputs)
        {
            var exclusions = normalizedInputs
                .Where(other => !string.Equals(other.Path, input.Path, StringComparison.OrdinalIgnoreCase)
                    && IsPathWithin(other.Path, input.Path))
                .Select(other => other.Path)
                .ToArray();

            entries.Add(new SystemUsageEntry
            {
                Category = input.Category,
                Path = input.Path,
                MountPath = input.MountPath,
                UsedBytes = CalculatePathBytes(input.Path, exclusions, diagnostics)
            });
        }

        var result = new SystemUsageAggregationResult
        {
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            CacheHit = false,
            ForcedRefresh = forceRefresh,
            Entries = entries
                .OrderBy(entry => entry.MountPath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.Category, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            Diagnostics = diagnostics
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };

        StoreCache(cacheKey, result);
        return result;
    }

    /// <summary>
    /// Clears the system usage cache.
    /// </summary>
    public static void ClearCache()
    {
        lock (CacheLock)
        {
            CachedResult = null;
        }
    }

    private static IReadOnlyList<SystemUsageScanInput> NormalizeInputs(IReadOnlyList<SystemUsageScanInput> inputs)
    {
        return inputs
            .Where(input => !string.IsNullOrWhiteSpace(input.Category)
                && !string.IsNullOrWhiteSpace(input.Path)
                && !string.IsNullOrWhiteSpace(input.MountPath))
            .Select(input => new SystemUsageScanInput
            {
                Category = input.Category.Trim().ToLowerInvariant(),
                Path = NormalizePath(input.Path),
                MountPath = input.MountPath
            })
            .Where(input => !string.IsNullOrWhiteSpace(input.Path))
            .GroupBy(input => input.Path, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderByDescending(input => input.Path.Length)
            .ToArray();
    }

    private static long CalculatePathBytes(
        string path,
        IReadOnlyList<string> excludedRoots,
        List<string> diagnostics)
    {
        try
        {
            if (File.Exists(path))
            {
                return new FileInfo(path).Length;
            }

            if (!Directory.Exists(path))
            {
                diagnostics.Add($"System path does not exist: {path}");
                return 0;
            }

            long total = 0;
            var pending = new Stack<string>();
            pending.Push(path);

            while (pending.Count > 0)
            {
                var current = pending.Pop();
                if (excludedRoots.Any(excluded => IsPathWithin(current, excluded)))
                {
                    continue;
                }

                IEnumerable<string> children;
                try
                {
                    children = Directory.EnumerateFileSystemEntries(current).ToArray();
                }
                catch (IOException exception)
                {
                    diagnostics.Add($"Failed to enumerate system path: {current}: {exception.Message}");
                    continue;
                }
                catch (UnauthorizedAccessException exception)
                {
                    diagnostics.Add($"Access denied while enumerating system path: {current}: {exception.Message}");
                    continue;
                }

                foreach (var child in children)
                {
                    try
                    {
                        var attributes = File.GetAttributes(child);
                        if ((attributes & FileAttributes.ReparsePoint) != 0)
                        {
                            continue;
                        }

                        if ((attributes & FileAttributes.Directory) != 0)
                        {
                            if (!excludedRoots.Any(excluded => IsPathWithin(child, excluded)))
                            {
                                pending.Push(child);
                            }

                            continue;
                        }

                        total += new FileInfo(child).Length;
                    }
                    catch (IOException exception)
                    {
                        diagnostics.Add($"Failed to read system path entry: {child}: {exception.Message}");
                    }
                    catch (UnauthorizedAccessException exception)
                    {
                        diagnostics.Add($"Access denied while reading system path entry: {child}: {exception.Message}");
                    }
                }
            }

            return total;
        }
        catch (IOException exception)
        {
            diagnostics.Add($"Failed to scan system path: {path}: {exception.Message}");
            return 0;
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add($"Access denied while scanning system path: {path}: {exception.Message}");
            return 0;
        }
    }

    private static string NormalizePath(string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            var root = Path.GetPathRoot(fullPath);
            return string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase)
                ? fullPath
                : fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (ArgumentException)
        {
            return string.Empty;
        }
        catch (NotSupportedException)
        {
            return string.Empty;
        }
    }

    private static bool IsPathWithin(string path, string parent)
    {
        if (string.Equals(path, parent, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var parentWithSeparator = parent.EndsWith(Path.DirectorySeparatorChar)
            || parent.EndsWith(Path.AltDirectorySeparatorChar)
            ? parent
            : string.Concat(parent, Path.DirectorySeparatorChar);
        return path.StartsWith(parentWithSeparator, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildCacheKey(IReadOnlyList<SystemUsageScanInput> inputs)
    {
        var raw = string.Join("\n", inputs.Select(input => string.Join("|", input.Category, input.Path, input.MountPath)));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
    }

    private static bool TryGetCached(
        string cacheKey,
        int cacheMinutes,
        out SystemUsageAggregationResult result)
    {
        result = new SystemUsageAggregationResult();
        if (cacheMinutes <= 0)
        {
            return false;
        }

        lock (CacheLock)
        {
            if (CachedResult is null
                || !string.Equals(CachedResult.CacheKey, cacheKey, StringComparison.Ordinal)
                || DateTimeOffset.UtcNow - CachedResult.CreatedAtUtc > TimeSpan.FromMinutes(cacheMinutes))
            {
                return false;
            }

            result = CachedResult.Result.Clone(cacheHit: true, forcedRefresh: false);
            return true;
        }
    }

    private static void StoreCache(string cacheKey, SystemUsageAggregationResult result)
    {
        lock (CacheLock)
        {
            CachedResult = new CachedSystemUsage
            {
                CacheKey = cacheKey,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                Result = result.Clone(cacheHit: false, forcedRefresh: false)
            };
        }
    }

    private sealed class CachedSystemUsage
    {
        public string CacheKey { get; init; } = string.Empty;

        public DateTimeOffset CreatedAtUtc { get; init; }

        public SystemUsageAggregationResult Result { get; init; } = new();
    }
}

/// <summary>
/// Defines one Jellyfin system path to scan.
/// </summary>
public class SystemUsageScanInput
{
    /// <summary>
    /// Gets or sets the usage category.
    /// </summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the system path.
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the resolved mount path.
    /// </summary>
    public string MountPath { get; set; } = string.Empty;
}

/// <summary>
/// Contains aggregated Jellyfin system-path usage.
/// </summary>
public class SystemUsageAggregationResult
{
    /// <summary>
    /// Gets or sets the generation time.
    /// </summary>
    public DateTimeOffset GeneratedAtUtc { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the result came from cache.
    /// </summary>
    public bool CacheHit { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the cache was bypassed.
    /// </summary>
    public bool ForcedRefresh { get; set; }

    /// <summary>
    /// Gets or sets system usage entries.
    /// </summary>
    public IReadOnlyList<SystemUsageEntry> Entries { get; set; } = Array.Empty<SystemUsageEntry>();

    /// <summary>
    /// Gets or sets diagnostics.
    /// </summary>
    public IReadOnlyList<string> Diagnostics { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Creates a detached copy with updated cache state.
    /// </summary>
    /// <param name="cacheHit">Cache state.</param>
    /// <param name="forcedRefresh">Forced refresh state.</param>
    /// <returns>A cloned result.</returns>
    public SystemUsageAggregationResult Clone(bool cacheHit, bool forcedRefresh)
    {
        return new SystemUsageAggregationResult
        {
            GeneratedAtUtc = GeneratedAtUtc,
            CacheHit = cacheHit,
            ForcedRefresh = forcedRefresh,
            Entries = Entries.Select(entry => entry.Clone()).ToArray(),
            Diagnostics = Diagnostics.ToArray()
        };
    }
}

/// <summary>
/// Describes exclusive usage for one Jellyfin system path.
/// </summary>
public class SystemUsageEntry
{
    /// <summary>
    /// Gets or sets the category.
    /// </summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the source path.
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the resolved mount path.
    /// </summary>
    public string MountPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets used bytes.
    /// </summary>
    public long UsedBytes { get; set; }

    /// <summary>
    /// Creates a detached copy.
    /// </summary>
    /// <returns>A cloned entry.</returns>
    public SystemUsageEntry Clone()
    {
        return new SystemUsageEntry
        {
            Category = Category,
            Path = Path,
            MountPath = MountPath,
            UsedBytes = UsedBytes
        };
    }
}
