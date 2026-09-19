using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MediaTools.Controllers.Models;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MediaTools.Services;

/// <summary>
/// Service to batch-convert and re-mux legacy video containers (.avi, .wmv, .mov) into modern Matroska (.mkv) files.
/// </summary>
public partial class ContainerConversionService
{
    private static readonly string[] NonMkvExtensions = { ".avi", ".wmv", ".mov", ".flv", ".divx", ".mpg", ".mpeg", ".ts", ".vob" };

    private readonly ILibraryManager _libraryManager;
    private readonly FFmpegResolverService _ffmpeg;
    private readonly SubtitleMuxerService _subtitleMuxer;
    private readonly SafeDatabaseSyncService _safeSync;
    private readonly ILogger<ContainerConversionService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ContainerConversionService"/> class.
    /// </summary>
    /// <param name="libraryManager">Library manager.</param>
    /// <param name="ffmpeg">FFmpeg resolver service.</param>
    /// <param name="subtitleMuxer">Subtitle muxer service.</param>
    /// <param name="safeSync">Safe database sync service.</param>
    /// <param name="logger">Logger.</param>
    public ContainerConversionService(
        ILibraryManager libraryManager,
        FFmpegResolverService ffmpeg,
        SubtitleMuxerService subtitleMuxer,
        SafeDatabaseSyncService safeSync,
        ILogger<ContainerConversionService> logger)
    {
        _libraryManager = libraryManager;
        _ffmpeg = ffmpeg;
        _subtitleMuxer = subtitleMuxer;
        _safeSync = safeSync;
        _logger = logger;
    }

    /// <summary>
    /// Scans the library for video files using legacy or non-MKV containers.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of conversion candidates.</returns>
    public List<ConversionCandidateDto> ScanNonMkvFiles(CancellationToken cancellationToken)
    {
        var videos = _libraryManager.GetItemList(new InternalItemsQuery
        {
            MediaTypes = new[] { Jellyfin.Data.Enums.MediaType.Video },
            IsVirtualItem = false,
            Recursive = true
        });

        var results = new List<ConversionCandidateDto>();
        foreach (var item in videos)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(item.Path) || !File.Exists(item.Path))
            {
                continue;
            }

            var ext = Path.GetExtension(item.Path);
            if (!IsNonMkv(ext))
            {
                continue;
            }

            var subs = _subtitleMuxer.FindSubtitlesForVideo(item.Path);
            var fileInfo = new FileInfo(item.Path);

            results.Add(new ConversionCandidateDto
            {
                ItemId = item.Id,
                Title = item.Name ?? Path.GetFileNameWithoutExtension(item.Path),
                CurrentPath = item.Path,
                CurrentContainer = ext.TrimStart('.').ToUpperInvariant(),
                FileSizeBytes = fileInfo.Exists ? fileInfo.Length : 0,
                VideoCodec = item.Container ?? ext,
                ExternalSubtitlesCount = subs.Count,
                SubtitleLanguages = subs.Select(s => s.LanguageCode).ToList()
            });
        }

        return results;
    }

    /// <summary>
    /// Converts or re-muxes a video file into Matroska (.mkv) container.
    /// </summary>
    /// <param name="itemId">Item ID to convert.</param>
    /// <param name="remuxOnly">Whether to do fast stream copy (-c copy) or transcode.</param>
    /// <param name="includeSubtitles">Whether to auto-mux external subtitles.</param>
    /// <param name="onProgress">Progress reporting callback.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if conversion succeeded.</returns>
    public async Task<bool> ConvertToMkvAsync(
        Guid itemId,
        bool remuxOnly,
        bool includeSubtitles,
        Action<double, string>? onProgress,
        CancellationToken cancellationToken)
    {
        var item = _libraryManager.GetItemById(itemId);
        if (item == null || string.IsNullOrWhiteSpace(item.Path) || !File.Exists(item.Path))
        {
            LogItemNotFound(_logger, itemId);
            return false;
        }

        var sourcePath = item.Path;
        var dir = Path.GetDirectoryName(sourcePath) ?? string.Empty;
        var stem = Path.GetFileNameWithoutExtension(sourcePath);
        var targetPath = Path.Combine(dir, stem + ".mkv");
        var tempPath = Path.Combine(dir, string.Concat(stem, ".conv_tmp.mkv"));

        try
        {
            onProgress?.Invoke(5, "Analysiere Untertitel und erstelle Konvertierungsbefehl...");

            var subArgs = string.Empty;
            if (includeSubtitles)
            {
                var subs = _subtitleMuxer.FindSubtitlesForVideo(sourcePath);
                if (subs.Count > 0)
                {
                    subArgs = _subtitleMuxer.BuildFFmpegArguments(subs, startIndex: 1);
                }
            }

            string codecArgs;
            if (remuxOnly)
            {
                codecArgs = "-map 0:v? -map 0:a? -c:v copy -c:a copy ";
            }
            else
            {
                codecArgs = "-map 0:v? -map 0:a? -c:v libx264 -crf 19 -preset medium -c:a aac -b:a 192k ";
            }

            var arguments = string.Format(
                CultureInfo.InvariantCulture,
                "-i \"{0}\" {1}{2}-y \"{3}\"",
                sourcePath,
                subArgs,
                codecArgs,
                tempPath);

            var exitCode = await _ffmpeg.RunFFmpegAsync(
                arguments,
                progress =>
                {
                    var msg = string.Format(CultureInfo.InvariantCulture, "Zeit: {0} | Speed: {1}", progress.CurrentTime, progress.Speed);
                    onProgress?.Invoke(50, msg);
                },
                cancellationToken).ConfigureAwait(false);

            if (exitCode != 0 || !File.Exists(tempPath) || new FileInfo(tempPath).Length < 1024 * 1024)
            {
                LogConversionFailed(_logger, item.Name ?? stem, exitCode);
                CleanupFile(tempPath);
                return false;
            }

            onProgress?.Invoke(90, "Konvertierung abgeschlossen. Ersetze Originaldatei...");

            // If target already existed (rare case of same name), overwrite safely
            if (File.Exists(targetPath) && !string.Equals(sourcePath, targetPath, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(targetPath);
            }

            File.Move(tempPath, targetPath);

            // Delete original non-mkv source file
            if (!string.Equals(sourcePath, targetPath, StringComparison.OrdinalIgnoreCase) && File.Exists(sourcePath))
            {
                File.Delete(sourcePath);
            }

            // Synchronize DB
            item.Path = targetPath;
            item.Container = "mkv";
            await _libraryManager.UpdateItemAsync(item, item.GetParent(), ItemUpdateType.MetadataEdit, cancellationToken).ConfigureAwait(false);

            onProgress?.Invoke(100, "Erfolgreich nach MKV konvertiert!");
            LogConversionSuccess(_logger, item.Name ?? stem, targetPath);
            return true;
        }
        catch (Exception ex)
        {
            LogConversionException(_logger, item.Name ?? stem, ex);
            CleanupFile(tempPath);
            return false;
        }
    }

    private static bool IsNonMkv(string ext)
    {
        foreach (var s in NonMkvExtensions)
        {
            if (string.Equals(s, ext, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static void CleanupFile(string path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Ignored fail-safe
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Item {ItemId} was not found for container conversion.")]
    private static partial void LogItemNotFound(ILogger logger, Guid itemId);

    [LoggerMessage(EventId = 2, Level = LogLevel.Error, Message = "FFmpeg container conversion failed for {Title} with exit code {ExitCode}.")]
    private static partial void LogConversionFailed(ILogger logger, string title, int exitCode);

    [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "Successfully converted {Title} to Matroska: {TargetPath}.")]
    private static partial void LogConversionSuccess(ILogger logger, string title, string targetPath);

    [LoggerMessage(EventId = 4, Level = LogLevel.Error, Message = "Exception during container conversion for {Title}.")]
    private static partial void LogConversionException(ILogger logger, string title, Exception exception);
}
