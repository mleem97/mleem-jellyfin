using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace Jellyfin.Plugin.HddDisplay.Services;

/// <summary>
/// Aggregates real file usage for Jellyfin libraries by resolved mount and media type.
/// </summary>
public static class MediaUsageAggregator
{
    private const int DefaultTimeoutSeconds = 120;
    private static readonly object CacheLock = new();
    private static CachedMediaUsage? CachedResult;

    /// <summary>
    /// Aggregates byte usage for the supplied library paths.
    /// </summary>
    /// <param name="inputs">Scan inputs.</param>
    /// <param name="cacheMinutes">Cache lifetime in minutes. Use zero to disable cache.</param>
    /// <param name="forceRefresh">Whether the current cache should be bypassed.</param>
    /// <param name="cancellationToken">Request cancellation token.</param>
    /// <param name="timeoutSeconds">Hard scan deadline in seconds.</param>
    /// <returns>Media usage aggregation result. Partial results are returned after cancellation or timeout.</returns>
    public static MediaUsageAggregationResult Calculate(
        IReadOnlyList<MediaUsageScanInput> inputs,
        int cacheMinutes,
        bool forceRefresh,
        CancellationToken cancellationToken = default,
        int timeoutSeconds = DefaultTimeoutSeconds)
    {
        var cacheKey = BuildCacheKey(inputs);
        if (!forceRefresh && TryGetCached(cacheKey, cacheMinutes, out var cached))
        {
            return cached;
        }

        var diagnostics = new List<string>();
        if (forceRefresh)
        {
            diagnostics.Add("Storage scan cache was bypassed by request.");
        }

        var context = new ScanContext(cancellationToken, timeoutSeconds, diagnostics, "media");
        var usage = new Dictionary<string, MediaUsageEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var input in inputs)
        {
            if (context.ShouldStop(input.LibraryPath))
            {
                break;
            }

            var usedBytes = CalculatePathBytes(input.LibraryPath, diagnostics, context);
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
            ForcedRefresh = forceRefresh,
            Completed = !context.Stopped,
            Entries = usage.Values
                .OrderBy(entry => entry.MountPath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.MediaType, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            Diagnostics = diagnostics
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };

        if (result.Completed)
        {
            StoreCache(cacheKey, result);
        }

        return result;
    }

    /// <summary>
    /// Clears the in-memory storage scan cache.
    /// </summary>
    public static void ClearCache()
    {
        lock (CacheLock)
        {
            CachedResult = null;
        }
    }

    private static long CalculatePathBytes(
        string path,
        List<string> diagnostics,
        ScanContext context)
    {
        try
        {
            if (File.Exists(path))
            {
                return context.ShouldStop(path) ? 0 : new FileInfo(path).Length;
            }

            if (!Directory.Exists(path))
            {
                diagnostics.Add($"Path does not exist: {path}");
                return 0;
            }

            long total = 0;
            var pending = new Stack<string>();
            pending.Push(path);
            while (pending.Count > 0)
            {
                var current = pending.Pop();
                if (context.ShouldStop(current))
                {
                    break;
                }

                IEnumerable<string> entries;
                try
                {
                    entries = Directory.EnumerateFileSystemEntries(current).ToArray();
                }
                catch (IOException exception)
                {
                    diagnostics.Add($"Failed to enumerate media path: {current}: {exception.Message}");
                    continue;
                }
                catch (UnauthorizedAccessException exception)
                {
                    diagnostics.Add($"Access denied while enumerating media path: {current}: {exception.Message}");
                    continue;
                }

                foreach (var entryPath in entries)
                {
                    if (context.ShouldStop(entryPath))
                    {
                        break;
                    }

                    try
                    {
                        var attributes = File.GetAttributes(entryPath);
                        if ((attributes & FileAttributes.ReparsePoint) != 0)
                        {
                            diagnostics.Add($"Skipped symbolic link or reparse point: {entryPath}");
                            continue;
                        }

                        if ((attributes & FileAttributes.Directory) != 0)
                        {
                            pending.Push(entryPath);
                        }
                        else
                        {
                            total += new FileInfo(entryPath).Length;
                        }
                    }
                    catch (IOException exception)
                    {
                        diagnostics.Add($"Failed to read media entry: {entryPath}: {exception.Message}");
                    }
                    catch (UnauthorizedAccessException exception)
                    {
                        diagnostics.Add($"Access denied while reading media entry: {entryPath}: {exception.Message}");
                    }
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

    private static bool TryGetCached(
        string cacheKey,
        int cacheMinutes,
        out MediaUsageAggregationResult result)
    {
        result = new MediaUsageAggregationResult();
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

    private static void StoreCache(string cacheKey, MediaUsageAggregationResult result)
    {
        lock (CacheLock)
        {
            CachedResult = new CachedMediaUsage
            {
                CacheKey = cacheKey,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                Result = result.Clone(cacheHit: false, forcedRefresh: false)
            };
        }
    }

    private static string NormalizeMediaType(string libraryType)
    {
        return string.IsNullOrWhiteSpace(libraryType)
            ? "other"
            : libraryType.Trim().ToLowerInvariant();
    }

    private static string BuildCacheKey(IReadOnlyList<MediaUsageScanInput> inputs)
    {
        var raw = string.Join("\n", inputs
            .OrderBy(input => input.LibraryPath, StringComparer.OrdinalIgnoreCase)
            .Select(input => string.Join("|", input.LibraryPath, input.LibraryType, input.MountPath)));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
    }

    private sealed class ScanContext
    {
        private readonly CancellationToken _cancellationToken;
        private readonly DateTimeOffset _deadlineUtc;
        private readonly List<string> _diagnostics;
        private readonly string _scanName;

        public ScanContext(
            CancellationToken cancellationToken,
            int timeoutSeconds,
            List<string> diagnostics,
            string scanName)
        {
            _cancellationToken = cancellationToken;
            _diagnostics = diagnostics;
            _scanName = scanName;
            var boundedTimeout = Math.Clamp(timeoutSeconds, 1, 3600);
            _deadlineUtc = DateTimeOffset.UtcNow.AddSeconds(boundedTimeout);
        }

        public bool Stopped { get; private set; }

        public bool ShouldStop(string currentPath)
        {
            if (Stopped)
            {
                return true;
            }

            if (_cancellationToken.IsCancellationRequested)
            {
                Stopped = true;
                _diagnostics.Add($"The {_scanName} scan was cancelled while reading: {currentPath}");
                return true;
            }

            if (DateTimeOffset.UtcNow >= _deadlineUtc)
            {
                Stopped = true;
                _diagnostics.Add($"The {_scanName} scan timed out while reading: {currentPath}");
                return true;
            }

            return false;
        }
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
    /// Gets or sets a value indicating whether the cache was bypassed for this result.
    /// </summary>
    public bool ForcedRefresh { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the scan completed before cancellation or timeout.
    /// </summary>
    public bool Completed { get; set; } = true;

    /// <summary>
    /// Gets or sets usage entries.
    /// </summary>
    public IReadOnlyList<MediaUsageEntry> Entries { get; set; } = Array.Empty<MediaUsageEntry>();

    /// <summary>
    /// Gets or sets diagnostics.
    /// </summary>
    public IReadOnlyList<string> Diagnostics { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Clones the result and applies cache state.
    /// </summary>
    /// <param name="cacheHit">Cache hit state.</param>
    /// <param name="forcedRefresh">Forced refresh state.</param>
    /// <returns>Cloned result.</returns>
    public MediaUsageAggregationResult Clone(bool cacheHit, bool forcedRefresh)
    {
        return new MediaUsageAggregationResult
        {
            GeneratedAtUtc = GeneratedAtUtc,
            CacheHit = cacheHit,
            ForcedRefresh = forcedRefresh,
            Completed = Completed,
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
