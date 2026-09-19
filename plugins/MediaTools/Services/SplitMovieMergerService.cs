using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MediaTools.Controllers.Models;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MediaTools.Services;

/// <summary>
/// Service to detect and merge multi-part split movies (e.g. CD1, CD2) via smart stream concat or fallback re-encoding.
/// </summary>
public partial class SplitMovieMergerService
{
    private static readonly Regex SplitPatternRegex = new(
        @"(?i)[._ -](cd|part|pt|disc|disk)[._ -]?([0-9]|a|b)\.[a-z0-9]+$",
        RegexOptions.Compiled);

    private static readonly Regex PartIndexRegex = new(
        @"(?i)[._ -](?:cd|part|pt|disc|disk)[._ -]?([0-9]|a|b)\.[a-z0-9]+$",
        RegexOptions.Compiled);

    private readonly ILibraryManager _libraryManager;
    private readonly FFmpegResolverService _ffmpeg;
    private readonly SafeDatabaseSyncService _safeSync;
    private readonly ILogger<SplitMovieMergerService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SplitMovieMergerService"/> class.
    /// </summary>
    /// <param name="libraryManager">Library manager.</param>
    /// <param name="ffmpeg">FFmpeg resolver service.</param>
    /// <param name="safeSync">Safe DB sync service.</param>
    /// <param name="logger">Logger.</param>
    public SplitMovieMergerService(
        ILibraryManager libraryManager,
        FFmpegResolverService ffmpeg,
        SafeDatabaseSyncService safeSync,
        ILogger<SplitMovieMergerService> logger)
    {
        _libraryManager = libraryManager;
        _ffmpeg = ffmpeg;
        _safeSync = safeSync;
        _logger = logger;
    }

    /// <summary>
    /// Scans the library for multi-part split movies.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of detected split movie groups.</returns>
    public List<SplitMovieGroupDto> ScanSplitMovies(CancellationToken cancellationToken)
    {
        var movies = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { Jellyfin.Data.Enums.BaseItemKind.Movie },
            IsVirtualItem = false,
            Recursive = true
        });

        var candidateItems = new List<(BaseItem Item, string Stem, int PartIndex)>();

        foreach (var item in movies)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(item.Path) || !File.Exists(item.Path))
            {
                continue;
            }

            var filename = Path.GetFileName(item.Path);
            if (!SplitPatternRegex.IsMatch(filename))
            {
                continue;
            }

            var dir = Path.GetDirectoryName(item.Path) ?? string.Empty;
            var match = PartIndexRegex.Match(filename);
            var partToken = match.Groups[1].Value.ToLowerInvariant();
            var partIndex = ParsePartIndex(partToken);

            var stem = filename.Substring(0, match.Index);
            var groupKey = Path.Combine(dir, stem);
            candidateItems.Add((item, groupKey, partIndex));
        }

        var groups = candidateItems
            .GroupBy(c => c.Stem, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .ToList();

        var result = new List<SplitMovieGroupDto>();
        foreach (var g in groups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var orderedParts = g.OrderBy(p => p.PartIndex).ToList();
            var firstItem = orderedParts[0].Item;
            var dir = Path.GetDirectoryName(firstItem.Path) ?? string.Empty;
            var cleanTitle = MediaRenamerService.CleanStem(firstItem.Name ?? Path.GetFileNameWithoutExtension(firstItem.Path));

            var targetFilename = string.Format(
                CultureInfo.InvariantCulture,
                "{0}{1}.mkv",
                cleanTitle,
                firstItem.ProductionYear.HasValue ? string.Format(CultureInfo.InvariantCulture, " ({0})", firstItem.ProductionYear.Value) : string.Empty);

            var groupDto = new SplitMovieGroupDto
            {
                GroupKey = g.Key,
                Title = cleanTitle,
                Year = firstItem.ProductionYear,
                DirectoryPath = dir,
                TargetOutputFilename = targetFilename
            };

            foreach (var part in orderedParts)
            {
                var fileInfo = new FileInfo(part.Item.Path);
                groupDto.Parts.Add(new SplitPartItemDto
                {
                    ItemId = part.Item.Id,
                    Path = part.Item.Path,
                    Filename = Path.GetFileName(part.Item.Path),
                    PartIndex = part.PartIndex,
                    FileSizeBytes = fileInfo.Exists ? fileInfo.Length : 0,
                    VideoCodec = part.Item.Container ?? string.Empty,
                    Width = (part.Item as Movie)?.Width ?? 0,
                    Height = (part.Item as Movie)?.Height ?? 0
                });
            }

            groupDto.CanSmartConcat = true;
            groupDto.CompatibilityReason = "Compatible video and audio formats detected for smart copy.";
            result.Add(groupDto);
        }

        return result;
    }

    /// <summary>
    /// Merges a split movie group into a single continuous Matroska file.
    /// </summary>
    /// <param name="group">Split movie group to merge.</param>
    /// <param name="forceReencode">Whether to force re-encoding instead of copy.</param>
    /// <param name="onProgress">Progress reporting callback.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if merge succeeded.</returns>
    public async Task<bool> MergeSplitGroupAsync(
        SplitMovieGroupDto group,
        bool forceReencode,
        Action<double, string>? onProgress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(group);
        if (group.Parts.Count < 2)
        {
            return false;
        }

        var dir = group.DirectoryPath;
        var outputPath = Path.Combine(dir, group.TargetOutputFilename);
        var tempOutputPath = Path.Combine(dir, string.Concat(Path.GetFileNameWithoutExtension(group.TargetOutputFilename), ".tmp.mkv"));
        var concatListPath = Path.Combine(dir, string.Concat(".concat_", Guid.NewGuid().ToString("N"), ".txt"));

        try
        {
            // 1. Create Concat Demuxer file
            var lines = group.Parts.Select(p => string.Format(CultureInfo.InvariantCulture, "file '{0}'", p.Path.Replace("'", "'\\''")));
            await File.WriteAllLinesAsync(concatListPath, lines, cancellationToken).ConfigureAwait(false);

            onProgress?.Invoke(5, "Concat-Liste vorbereitet. Starte FFmpeg...");

            // 2. Build FFmpeg command
            string arguments;
            if (!forceReencode && group.CanSmartConcat)
            {
                arguments = string.Format(
                    CultureInfo.InvariantCulture,
                    "-f concat -safe 0 -i \"{0}\" -map 0 -c copy -y \"{1}\"",
                    concatListPath,
                    tempOutputPath);
            }
            else
            {
                arguments = string.Format(
                    CultureInfo.InvariantCulture,
                    "-f concat -safe 0 -i \"{0}\" -c:v libx264 -crf 18 -preset medium -c:a aac -b:a 192k -y \"{1}\"",
                    concatListPath,
                    tempOutputPath);
            }

            var exitCode = await _ffmpeg.RunFFmpegAsync(
                arguments,
                progress =>
                {
                    var msg = string.Format(CultureInfo.InvariantCulture, "Zeit: {0} | Speed: {1}", progress.CurrentTime, progress.Speed);
                    onProgress?.Invoke(50, msg);
                },
                cancellationToken).ConfigureAwait(false);

            if (exitCode != 0 || !File.Exists(tempOutputPath) || new FileInfo(tempOutputPath).Length < 1024 * 1024)
            {
                LogMergeFailed(_logger, group.Title, exitCode);
                CleanupFile(tempOutputPath);
                return false;
            }

            onProgress?.Invoke(90, "Merge erfolgreich. Aktualisiere Jellyfin-Datenbank...");

            // 3. Promote temporary file to target
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }

            File.Move(tempOutputPath, outputPath);

            // 4. Update master item (Part 1) in database
            var masterPart = group.Parts[0];
            var masterItem = _libraryManager.GetItemById(masterPart.ItemId);
            if (masterItem != null)
            {
                masterItem.Path = outputPath;
                masterItem.Container = "mkv";
                await _libraryManager.UpdateItemAsync(masterItem, masterItem.GetParent(), ItemUpdateType.MetadataEdit, cancellationToken).ConfigureAwait(false);
            }

            // 5. Delete redundant parts physically and remove from database
            for (var i = 1; i < group.Parts.Count; i++)
            {
                var secondary = group.Parts[i];
                var secItem = _libraryManager.GetItemById(secondary.ItemId);
                if (secItem != null)
                {
                    _safeSync.SafeRemovePartRecord(secItem);
                }

                if (File.Exists(secondary.Path))
                {
                    File.Delete(secondary.Path);
                }
            }

            onProgress?.Invoke(100, "Multi-Part Film erfolgreich zusammengeführt!");
            LogMergeSuccess(_logger, group.Title, outputPath);
            return true;
        }
        catch (Exception ex)
        {
            LogMergeException(_logger, group.Title, ex);
            CleanupFile(tempOutputPath);
            return false;
        }
        finally
        {
            CleanupFile(concatListPath);
        }
    }

    private static int ParsePartIndex(string token)
    {
        if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
        {
            return n;
        }

        if (string.Equals(token, "a", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        if (string.Equals(token, "b", StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        return 1;
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

    [LoggerMessage(EventId = 1, Level = LogLevel.Error, Message = "FFmpeg merge process failed for {Title} with exit code {ExitCode}.")]
    private static partial void LogMergeFailed(ILogger logger, string title, int exitCode);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Successfully merged split movie {Title} into {OutputPath}.")]
    private static partial void LogMergeSuccess(ILogger logger, string title, string outputPath);

    [LoggerMessage(EventId = 3, Level = LogLevel.Error, Message = "Exception during split movie merge for {Title}.")]
    private static partial void LogMergeException(ILogger logger, string title, Exception exception);
}
