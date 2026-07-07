using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Jellyfin.Plugin.HddDisplay.Services;

/// <summary>
/// Aggregates real file usage for Jellyfin libraries by resolved mount and media type.
/// </summary>
public static class MediaUsageAggregator
{
    private static readonly object CacheLock = new();
    private static CachedMediaUsage? CachedResult;

    /// <summary>
    /// Aggregates byte usage for the supplied library paths.
    /// </summary>
    /// <param name="inputs">Scan inputs.</param>
    /// <param name="cacheMinutes">Cache lifetime in minutes. Use zero to disable cache.</param>
    /// <returns>Media usage aggregation result.</returns>
    public static MediaUsageAggregationResult Calculate(IReadOnlyList<MediaUsageScanInput> inputs, int cacheMinutes)
    {
        var cacheKey = BuildCacheKey(inputs);
        if (TryGetCached(cacheKey, cacheMinutes, out var cached))
        {
            return cached;
        }

        var diagnostics = new List<string>();
        var usage = new Dictionary<string, MediaUsageEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var input in inputs)
        {
            var usedBytes = CalculatePathBytes(input.LibraryPath, diagnostics);
            var mediaType = NormalizeMediaType(input.LibraryType);
            var key = string.Concat(input.MountPath, "|", mediaType);
            if (!usage.TryGetValue(key, out var entry))
            {
                entry = new MediaUsageEntry
                {
                    MountPath = input.MountPath,
                    MediaType = mediaType
                };
                usage[key] = entry;
            }

            entry.UsedBytes += usedBytes;
        }

        var result = new MediaUsageAggregationResult
        {
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            CacheHit = false,
            Entries = usage.Values
                .OrderBy(entry => entry.MountPath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.MediaType, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            Diagnostics = diagnostics.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToArray()
        };

        StoreCache(cacheKey, result);
        return result;
    }

    private static bool TryGetCached(string cacheKey, int cacheMinutes, out MediaUsageAggregationResult result)
    {
        result = new MediaUsageAggregationResult();
        if (cacheMinutes <= 0)
        {
            return false;
        }

        lock (CacheLock)
        {
            if (CachedResult is null || !string.Equals(CachedResult.CacheKey, cacheKey, StringComparison.Ordinal))
            {
                return false;
            }

            if (DateTimeOffset.UtcNow - CachedResult.CreatedAtUtc > TimeSpan.FromMinutes(cacheMinutes))
            {
                return false;
            }

            result = CachedResult.Result.Clone(cacheHit: true);
            return true;
        }
    }

    private static void StoreCache(string cacheKey, MediaUsageAggregationResult result)
    {
        lock (CacheLock)
        {
            CachedResult = new CachedMediaUsage
            {
                CacheKey = cacheKey,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                Result = result.Clone(cacheHit: false)
            };
        }
    }

    private static long CalculatePathBytes(string path, List<string> diagnostics)
    {
        try
        {
            if (File.Exists(path))
            {
                return new FileInfo(path).Length;
            }

            if (!Directory.Exists(path))
            {
                diagnostics.Add($"Path does not exist: {path}");
                return 0;
            }

            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint
            };

            long total = 0;
            foreach (var file in new DirectoryInfo(path).EnumerateFiles("*", options))
            {
                try
                {
                    total += file.Length;
                }
                catch (IOException exception)
                {
                    diagnostics.Add($"Failed to read file size: {file.FullName}: {exception.Message}");
                }
                catch (UnauthorizedAccessException exception)
                {
                    diagnostics.Add($"Access denied while reading file size: {file.FullName}: {exception.Message}");
                }
            }

            return total;
        }
        catch (IOException exception)
        {
            diagnostics.Add($"Failed to scan path: {path}: {exception.Message}");
            return 0;
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add($"Access denied while scanning path: {path}: {exception.Message}");
            return 0;
        }
    }

    private static string NormalizeMediaType(string libraryType)
    {
        return string.IsNullOrWhiteSpace(libraryType) ? "other" : libraryType.Trim().ToLowerInvariant();
    }

    private static string BuildCacheKey(IReadOnlyList<MediaUsageScanInput> inputs)
    {
        var raw = string.Join("\n", inputs
            .OrderBy(input => input.LibraryPath, StringComparer.OrdinalIgnoreCase)
            .Select(input => string.Join("|", input.LibraryPath, input.LibraryType, input.MountPath)));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
    }

    private sealed class CachedMediaUsage
    {
        public string CacheKey { get; init; } = string.Empty;

        public DateTimeOffset CreatedAtUtc { get; init; }

        public MediaUsageAggregationResult Result { get; init; } = new();
    }
}

/// <summary>
/// Library path scan input.
/// </summary>
public class MediaUsageScanInput
{
    /// <summary>
    /// Gets or sets the library name.
    /// </summary>
    public string LibraryName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the library type.
    /// </summary>
    public string LibraryType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the library path.
    /// </summary>
    public string LibraryPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the resolved mount path.
    /// </summary>
    public string MountPath { get; set; } = string.Empty;
}

/// <summary>
/// Media usage aggregation result.
/// </summary>
public class MediaUsageAggregationResult
{
    /// <summary>
    /// Gets or sets generation timestamp.
    /// </summary>
    public DateTimeOffset GeneratedAtUtc { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this result came from cache.
    /// </summary>
    public bool CacheHit { get; set; }

    /// <summary>
    /// Gets or sets usage entries.
    /// </summary>
    public IReadOnlyList<MediaUsageEntry> Entries { get; set; } = Array.Empty<MediaUsageEntry>();

    /// <summary>
    /// Gets or sets diagnostics.
    /// </summary>
    public IReadOnlyList<string> Diagnostics { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Clones the result and applies cache hit state.
    /// </summary>
    /// <param name="cacheHit">Cache hit state.</param>
    /// <returns>Cloned result.</returns>
    public MediaUsageAggregationResult Clone(bool cacheHit)
    {
        return new MediaUsageAggregationResult
        {
            GeneratedAtUtc = GeneratedAtUtc,
            CacheHit = cacheHit,
            Entries = Entries.Select(entry => entry.Clone()).ToArray(),
            Diagnostics = Diagnostics.ToArray()
        };
    }
}

/// <summary>
/// Media usage entry.
/// </summary>
public class MediaUsageEntry
{
    /// <summary>
    /// Gets or sets the mount path.
    /// </summary>
    public string MountPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the media type.
    /// </summary>
    public string MediaType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets used bytes.
    /// </summary>
    public long UsedBytes { get; set; }

    /// <summary>
    /// Clones this entry.
    /// </summary>
    /// <returns>Cloned entry.</returns>
    public MediaUsageEntry Clone()
    {
        return new MediaUsageEntry
        {
            MountPath = MountPath,
            MediaType = MediaType,
            UsedBytes = UsedBytes
        };
    }
}
