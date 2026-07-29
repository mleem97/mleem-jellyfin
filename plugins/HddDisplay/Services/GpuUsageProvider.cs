using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.HddDisplay.Services;

/// <summary>
/// Provides GPU usage snapshots.
/// </summary>
public interface IGpuUsageProvider
{
    /// <summary>
    /// Gets the current GPU usage snapshot.
    /// </summary>
    /// <returns>GPU usage snapshot.</returns>
    GpuUsageSnapshot GetSnapshot();
}

/// <summary>
/// Provides an independent short-lived cache for GPU snapshots.
/// </summary>
public static class GpuUsageCache
{
    private static readonly object CacheLock = new();
    private static CachedGpuUsage? CachedResult;

    /// <summary>
    /// Gets a cached GPU snapshot or invokes the provider.
    /// </summary>
    /// <param name="provider">GPU provider.</param>
    /// <param name="cacheSeconds">Cache lifetime in seconds.</param>
    /// <param name="forceRefresh">Whether the cache should be bypassed.</param>
    /// <returns>A detached GPU snapshot.</returns>
    public static GpuUsageSnapshot GetSnapshot(
        IGpuUsageProvider provider,
        int cacheSeconds,
        bool forceRefresh)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (!forceRefresh && TryGetCached(cacheSeconds, out var cached))
        {
            cached.CacheHit = true;
            return cached;
        }

        var snapshot = provider.GetSnapshot();
        snapshot.CacheHit = false;
        lock (CacheLock)
        {
            CachedResult = new CachedGpuUsage
            {
                CreatedAtUtc = DateTimeOffset.UtcNow,
                Snapshot = snapshot.Clone()
            };
        }

        return snapshot.Clone();
    }

    /// <summary>
    /// Clears the GPU snapshot cache.
    /// </summary>
    public static void Clear()
    {
        lock (CacheLock)
        {
            CachedResult = null;
        }
    }

    private static bool TryGetCached(int cacheSeconds, out GpuUsageSnapshot snapshot)
    {
        snapshot = new GpuUsageSnapshot();
        if (cacheSeconds <= 0)
        {
            return false;
        }

        lock (CacheLock)
        {
            if (CachedResult is null
                || DateTimeOffset.UtcNow - CachedResult.CreatedAtUtc > TimeSpan.FromSeconds(cacheSeconds))
            {
                return false;
            }

            snapshot = CachedResult.Snapshot.Clone();
            return true;
        }
    }

    private sealed class CachedGpuUsage
    {
        public DateTimeOffset CreatedAtUtc { get; init; }

        public GpuUsageSnapshot Snapshot { get; init; } = new();
    }
}

/// <summary>
/// Reads NVIDIA GPU usage through nvidia-smi.
/// </summary>
public sealed class NvidiaSmiGpuUsageProvider : IGpuUsageProvider
{
    private const int DefaultTimeoutMilliseconds = 2500;
    private readonly int _timeoutMilliseconds;

    /// <summary>
    /// Initializes a new instance of the <see cref="NvidiaSmiGpuUsageProvider"/> class.
    /// </summary>
    /// <param name="timeoutMilliseconds">Hard timeout for each nvidia-smi invocation.</param>
    public NvidiaSmiGpuUsageProvider(int timeoutMilliseconds = DefaultTimeoutMilliseconds)
    {
        _timeoutMilliseconds = Math.Clamp(timeoutMilliseconds, 250, 30000);
    }

    /// <inheritdoc />
    public GpuUsageSnapshot GetSnapshot()
    {
        var gpuCommand = RunNvidiaSmi(
            new[]
            {
                "--query-gpu=index,name,utilization.gpu,memory.total,memory.used,memory.free",
                "--format=csv,noheader,nounits"
            },
            _timeoutMilliseconds);

        if (!gpuCommand.Success)
        {
            return GpuUsageSnapshot.Unavailable("nvidia-smi", gpuCommand.Error);
        }

        var processCommand = RunNvidiaSmi(
            new[]
            {
                "--query-compute-apps=pid,process_name,used_memory",
                "--format=csv,noheader,nounits"
            },
            _timeoutMilliseconds);

        var devices = ParseDevices(gpuCommand.Output);
        var processes = processCommand.Success
            ? ParseProcesses(processCommand.Output)
            : Array.Empty<GpuProcessUsage>();
        var processDiagnostic = processCommand.Success
            ? string.Empty
            : string.Concat(" Process telemetry unavailable: ", processCommand.Error);

        return new GpuUsageSnapshot
        {
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            IsAvailable = devices.Count > 0,
            Provider = "nvidia-smi",
            Diagnostic = devices.Count > 0
                ? string.Concat("NVIDIA telemetry collected.", processDiagnostic)
                : "nvidia-smi returned no GPU rows.",
            Devices = devices,
            Processes = processes,
            JellyfinFfmpegProcessCount = processes.Count(process => process.IsJellyfinFfmpeg)
        };
    }

    private static IReadOnlyList<GpuDeviceUsage> ParseDevices(string output)
    {
        return output
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(ParseDeviceLine)
            .Where(device => device is not null)
            .Cast<GpuDeviceUsage>()
            .ToArray();
    }

    private static GpuDeviceUsage? ParseDeviceLine(string line)
    {
        var parts = line.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length < 6)
        {
            return null;
        }

        return new GpuDeviceUsage
        {
            Index = ParseInt(parts[0]),
            Name = parts[1],
            GpuUtilizationPercent = ParseInt(parts[2]),
            MemoryTotalMiB = ParseInt(parts[3]),
            MemoryUsedMiB = ParseInt(parts[4]),
            MemoryFreeMiB = ParseInt(parts[5])
        };
    }

    private static IReadOnlyList<GpuProcessUsage> ParseProcesses(string output)
    {
        return output
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(ParseProcessLine)
            .Where(process => process is not null)
            .Cast<GpuProcessUsage>()
            .ToArray();
    }

    private static GpuProcessUsage? ParseProcessLine(string line)
    {
        var parts = line.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length < 3)
        {
            return null;
        }

        var processName = parts[1];
        return new GpuProcessUsage
        {
            Pid = ParseInt(parts[0]),
            ProcessName = processName,
            UsedMemoryMiB = ParseInt(parts[2]),
            IsJellyfinFfmpeg = IsJellyfinFfmpegProcess(processName)
        };
    }

    private static bool IsJellyfinFfmpegProcess(string processName)
    {
        return processName.Contains("jellyfin-ffmpeg", StringComparison.OrdinalIgnoreCase)
            || processName.Contains("ffmpeg", StringComparison.OrdinalIgnoreCase);
    }

    private static int ParseInt(string value)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result
            : 0;
    }

    private static CommandResult RunNvidiaSmi(
        IReadOnlyList<string> arguments,
        int timeoutMilliseconds)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "nvidia-smi",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            foreach (var argument in arguments)
            {
                process.StartInfo.ArgumentList.Add(argument);
            }

            if (!process.Start())
            {
                return CommandResult.Fail("nvidia-smi could not be started.");
            }

            Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
            Task<string> errorTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(timeoutMilliseconds))
            {
                TryKillProcess(process);
                return CommandResult.Fail(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"nvidia-smi exceeded the {timeoutMilliseconds} ms timeout and was terminated."));
            }

            var output = outputTask.GetAwaiter().GetResult();
            var error = errorTask.GetAwaiter().GetResult();
            if (process.ExitCode != 0)
            {
                return CommandResult.Fail(
                    string.IsNullOrWhiteSpace(error)
                        ? string.Create(CultureInfo.InvariantCulture, $"nvidia-smi exited with code {process.ExitCode}.")
                        : error.Trim());
            }

            return CommandResult.Ok(output);
        }
        catch (Win32Exception exception)
        {
            return CommandResult.Fail(exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            return CommandResult.Fail(exception.Message);
        }
        catch (NotSupportedException exception)
        {
            return CommandResult.Fail(exception.Message);
        }
    }

    private static void TryKillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(500);
            }
        }
        catch (InvalidOperationException)
        {
            // The process already exited between the timeout and cleanup attempt.
        }
        catch (NotSupportedException)
        {
            // Process-tree termination is not available on this platform.
        }
        catch (Win32Exception)
        {
            // The process could not be terminated; the timeout is still reported.
        }
    }

    private sealed class CommandResult
    {
        public bool Success { get; init; }

        public string Output { get; init; } = string.Empty;

        public string Error { get; init; } = string.Empty;

        public static CommandResult Ok(string output)
        {
            return new CommandResult
            {
                Success = true,
                Output = output
            };
        }

        public static CommandResult Fail(string error)
        {
            return new CommandResult
            {
                Success = false,
                Error = error
            };
        }
    }
}

/// <summary>
/// GPU usage snapshot.
/// </summary>
public class GpuUsageSnapshot
{
    /// <summary>
    /// Gets or sets generation timestamp.
    /// </summary>
    public DateTimeOffset GeneratedAtUtc { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether GPU telemetry is available.
    /// </summary>
    public bool IsAvailable { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this snapshot came from cache.
    /// </summary>
    public bool CacheHit { get; set; }

    /// <summary>
    /// Gets or sets the provider name.
    /// </summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets diagnostic text.
    /// </summary>
    public string Diagnostic { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets GPU devices.
    /// </summary>
    public IReadOnlyList<GpuDeviceUsage> Devices { get; set; } = Array.Empty<GpuDeviceUsage>();

    /// <summary>
    /// Gets or sets GPU processes.
    /// </summary>
    public IReadOnlyList<GpuProcessUsage> Processes { get; set; } = Array.Empty<GpuProcessUsage>();

    /// <summary>
    /// Gets or sets the detected Jellyfin ffmpeg process count.
    /// </summary>
    public int JellyfinFfmpegProcessCount { get; set; }

    /// <summary>
    /// Creates an unavailable snapshot.
    /// </summary>
    /// <param name="provider">Provider name.</param>
    /// <param name="diagnostic">Diagnostic text.</param>
    /// <returns>Unavailable snapshot.</returns>
    public static GpuUsageSnapshot Unavailable(string provider, string diagnostic)
    {
        return new GpuUsageSnapshot
        {
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            IsAvailable = false,
            Provider = provider,
            Diagnostic = diagnostic
        };
    }

    /// <summary>
    /// Creates a detached snapshot copy.
    /// </summary>
    /// <returns>A cloned snapshot.</returns>
    public GpuUsageSnapshot Clone()
    {
        return new GpuUsageSnapshot
        {
            GeneratedAtUtc = GeneratedAtUtc,
            IsAvailable = IsAvailable,
            CacheHit = CacheHit,
            Provider = Provider,
            Diagnostic = Diagnostic,
            Devices = Devices.Select(device => device.Clone()).ToArray(),
            Processes = Processes.Select(process => process.Clone()).ToArray(),
            JellyfinFfmpegProcessCount = JellyfinFfmpegProcessCount
        };
    }
}

/// <summary>
/// GPU device usage.
/// </summary>
public class GpuDeviceUsage
{
    /// <summary>
    /// Gets or sets GPU index.
    /// </summary>
    public int Index { get; set; }

    /// <summary>
    /// Gets or sets GPU name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets GPU utilization percentage.
    /// </summary>
    public int GpuUtilizationPercent { get; set; }

    /// <summary>
    /// Gets or sets total memory in MiB.
    /// </summary>
    public int MemoryTotalMiB { get; set; }

    /// <summary>
    /// Gets or sets used memory in MiB.
    /// </summary>
    public int MemoryUsedMiB { get; set; }

    /// <summary>
    /// Gets or sets free memory in MiB.
    /// </summary>
    public int MemoryFreeMiB { get; set; }

    /// <summary>
    /// Creates a detached copy.
    /// </summary>
    /// <returns>A cloned device.</returns>
    public GpuDeviceUsage Clone()
    {
        return new GpuDeviceUsage
        {
            Index = Index,
            Name = Name,
            GpuUtilizationPercent = GpuUtilizationPercent,
            MemoryTotalMiB = MemoryTotalMiB,
            MemoryUsedMiB = MemoryUsedMiB,
            MemoryFreeMiB = MemoryFreeMiB
        };
    }
}

/// <summary>
/// GPU process usage.
/// </summary>
public class GpuProcessUsage
{
    /// <summary>
    /// Gets or sets process id.
    /// </summary>
    public int Pid { get; set; }

    /// <summary>
    /// Gets or sets process name.
    /// </summary>
    public string ProcessName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets used memory in MiB.
    /// </summary>
    public int UsedMemoryMiB { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the process looks like Jellyfin ffmpeg.
    /// </summary>
    public bool IsJellyfinFfmpeg { get; set; }

    /// <summary>
    /// Creates a detached copy.
    /// </summary>
    /// <returns>A cloned process.</returns>
    public GpuProcessUsage Clone()
    {
        return new GpuProcessUsage
        {
            Pid = Pid,
            ProcessName = ProcessName,
            UsedMemoryMiB = UsedMemoryMiB,
            IsJellyfinFfmpeg = IsJellyfinFfmpeg
        };
    }
}
