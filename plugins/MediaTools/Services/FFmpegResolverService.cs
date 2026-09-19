using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MediaTools.Services;

/// <summary>
/// Service to locate FFmpeg and FFprobe binaries and execute processes with live progress parsing.
/// </summary>
public partial class FFmpegResolverService
{
    private static readonly Regex ProgressRegex = new(
        @"(?:frame=\s*(?<frame>\d+))?\s*(?:fps=\s*(?<fps>[\d\.]+))?.*?(?:time=(?<time>\S+))?.*?(?:speed=\s*(?<speed>[\d\.]+)x)?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly IServerConfigurationManager _configManager;
    private readonly ILogger<FFmpegResolverService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FFmpegResolverService"/> class.
    /// </summary>
    /// <param name="configManager">Server configuration manager.</param>
    /// <param name="logger">Logger.</param>
    public FFmpegResolverService(
        IServerConfigurationManager configManager,
        ILogger<FFmpegResolverService> logger)
    {
        _configManager = configManager;
        _logger = logger;
    }

    /// <summary>
    /// Progress event arguments from an active FFmpeg process.
    /// </summary>
    public sealed class FFmpegProgressEventArgs : EventArgs
    {
        public int Frame { get; set; }

        public double Fps { get; set; }

        public TimeSpan CurrentTime { get; set; }

        public string Speed { get; set; } = string.Empty;

        public string RawLine { get; set; } = string.Empty;
    }

    /// <summary>
    /// Resolves the absolute path to the FFmpeg executable.
    /// </summary>
    /// <returns>Path to FFmpeg, or empty if not found.</returns>
    public string ResolveFFmpegPath()
    {
        var custom = Plugin.Instance?.Configuration?.FFmpegPathOverride;
        if (!string.IsNullOrWhiteSpace(custom) && File.Exists(custom))
        {
            return custom;
        }

        try
        {
            var serverEncoder = (_configManager.GetConfiguration("encoding") as MediaBrowser.Model.Configuration.EncodingOptions)?.EncoderAppPath;
            if (!string.IsNullOrWhiteSpace(serverEncoder) && File.Exists(serverEncoder))
            {
                return serverEncoder;
            }
        }
        catch (Exception ex)
        {
            LogEncoderLookupFailed(_logger, ex);
        }

        string[] standardPaths =
        {
            "/usr/lib/jellyfin-ffmpeg/ffmpeg",
            "/usr/bin/ffmpeg",
            "/usr/local/bin/ffmpeg",
            "C:\\Program Files\\Jellyfin\\Server\\ffmpeg.exe",
            "ffmpeg"
        };

        foreach (var path in standardPaths)
        {
            if (File.Exists(path))
            {
                return path;
            }
        }

        return "ffmpeg";
    }

    /// <summary>
    /// Resolves the absolute path to the FFprobe executable.
    /// </summary>
    /// <returns>Path to FFprobe, or empty if not found.</returns>
    public string ResolveFFprobePath()
    {
        var custom = Plugin.Instance?.Configuration?.FFprobePathOverride;
        if (!string.IsNullOrWhiteSpace(custom) && File.Exists(custom))
        {
            return custom;
        }

        var ffmpeg = ResolveFFmpegPath();
        if (!string.IsNullOrWhiteSpace(ffmpeg) && File.Exists(ffmpeg))
        {
            var dir = Path.GetDirectoryName(ffmpeg);
            if (!string.IsNullOrEmpty(dir))
            {
                var candidate = Path.Combine(dir, OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        string[] standardPaths =
        {
            "/usr/lib/jellyfin-ffmpeg/ffprobe",
            "/usr/bin/ffprobe",
            "/usr/local/bin/ffprobe",
            "C:\\Program Files\\Jellyfin\\Server\\ffprobe.exe",
            "ffprobe"
        };

        foreach (var path in standardPaths)
        {
            if (File.Exists(path))
            {
                return path;
            }
        }

        return "ffprobe";
    }

    /// <summary>
    /// Executes an FFmpeg command asynchronously, reporting parsed stderr progress.
    /// </summary>
    /// <param name="arguments">Process arguments.</param>
    /// <param name="onProgress">Optional callback for progress updates.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Exit code of the process.</returns>
    public async Task<int> RunFFmpegAsync(
        string arguments,
        Action<FFmpegProgressEventArgs>? onProgress,
        CancellationToken cancellationToken)
    {
        var binary = ResolveFFmpegPath();
        return await RunProcessWithProgressAsync(binary, arguments, onProgress, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Executes a process and captures standard output.
    /// </summary>
    /// <param name="binary">Executable path.</param>
    /// <param name="arguments">Command line arguments.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Captured stdout.</returns>
    public async Task<(int ExitCode, string StandardOutput, string StandardError)> RunProcessCaptureAsync(
        string binary,
        string arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = binary,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
            var outTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errTask = process.StandardError.ReadToEndAsync(cancellationToken);

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var stdout = await outTask.ConfigureAwait(false);
            var stderr = await errTask.ConfigureAwait(false);

            return (process.ExitCode, stdout, stderr);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception ex)
            {
                LogProcessKillFailed(_logger, ex);
            }

            throw;
        }
    }

    private async Task<int> RunProcessWithProgressAsync(
        string binary,
        string arguments,
        Action<FFmpegProgressEventArgs>? onProgress,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = binary,
            Arguments = arguments,
            RedirectStandardError = true,
            RedirectStandardOutput = false,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();

            var lineTask = Task.Run(async () =>
            {
                while (!process.StandardError.EndOfStream)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var line = await process.StandardError.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    if (onProgress != null)
                    {
                        var parsed = ParseProgressLine(line);
                        if (parsed != null)
                        {
                            onProgress(parsed);
                        }
                    }
                }
            }, cancellationToken);

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            await lineTask.ConfigureAwait(false);

            return process.ExitCode;
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception ex)
            {
                LogProcessKillFailed(_logger, ex);
            }

            throw;
        }
    }

    private static FFmpegProgressEventArgs? ParseProgressLine(string line)
    {
        var match = ProgressRegex.Match(line);
        if (!match.Success)
        {
            return null;
        }

        var args = new FFmpegProgressEventArgs { RawLine = line };
        if (int.TryParse(match.Groups["frame"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var frame))
        {
            args.Frame = frame;
        }

        if (double.TryParse(match.Groups["fps"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var fps))
        {
            args.Fps = fps;
        }

        var timeStr = match.Groups["time"].Value;
        if (!string.IsNullOrWhiteSpace(timeStr) && TimeSpan.TryParse(timeStr, CultureInfo.InvariantCulture, out var time))
        {
            args.CurrentTime = time;
        }

        args.Speed = match.Groups["speed"].Value;
        return args;
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Failed to query Jellyfin server encoder configuration.")]
    private static partial void LogEncoderLookupFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "Failed to terminate cancelled process.")]
    private static partial void LogProcessKillFailed(ILogger logger, Exception exception);
}
