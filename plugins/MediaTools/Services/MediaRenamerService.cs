using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MediaTools.Controllers.Models;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MediaTools.Services;

/// <summary>
/// Service to clean cryptic filenames and generate standardized paths based on Jellyfin metadata.
/// </summary>
public partial class MediaRenamerService
{
    private static readonly Regex HashPrefixRegex = new(
        @"^[a-f0-9]{8,32}__([a-f0-9]{8,32}__)?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex SceneTagsRegex = new(
        @"(?i)(?<=[\.\[\(_\s-]|^)(?:PROPER|REPACK|REAL|UNRATED|DIRECTORS\.CUT|EXTENDED|THEATRICAL|LIMITED|INTERNAL|DOCU|x264|x265|h264|h265|HEVC|AVC|XviD|DivX|BluRay|BDRip|BRRip|DVDRip|DVD|HDTV|WEB-DL|WEBRip|AAC|AC3|DTS|FLAC|DDP5\.1|1080p|720p|2160p|4K|UHD)(?=[\.\]\)_ -]|$)",
        RegexOptions.Compiled);

    private static readonly Regex ReleaseGroupRegex = new(
        @"[-_]([a-zA-Z0-9]+)$",
        RegexOptions.Compiled);

    private readonly ILibraryManager _libraryManager;
    private readonly SafeDatabaseSyncService _safeSync;
    private readonly ILogger<MediaRenamerService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="MediaRenamerService"/> class.
    /// </summary>
    /// <param name="libraryManager">Library manager.</param>
    /// <param name="safeSync">Safe DB sync service.</param>
    /// <param name="logger">Logger.</param>
    public MediaRenamerService(
        ILibraryManager libraryManager,
        SafeDatabaseSyncService safeSync,
        ILogger<MediaRenamerService> logger)
    {
        _libraryManager = libraryManager;
        _safeSync = safeSync;
        _logger = logger;
    }

    /// <summary>
    /// Generates previews of proposed file renames based on media types and limits.
    /// </summary>
    /// <param name="mediaTypes">Optional media types to filter.</param>
    /// <param name="limit">Max items to inspect.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of rename previews.</returns>
    public List<RenamePreviewDto> GeneratePreviews(
        IReadOnlyList<string>? mediaTypes,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = new InternalItemsQuery
        {
            MediaTypes = new[] { Jellyfin.Data.Enums.MediaType.Video, Jellyfin.Data.Enums.MediaType.Audio },
            IsVirtualItem = false,
            Recursive = true,
            Limit = limit > 0 ? limit : 100
        };

        var items = _libraryManager.GetItemList(query);
        return GeneratePreviewsCore(items, cancellationToken);
    }

    /// <summary>
    /// Generates previews of proposed file renames for a specific list of item IDs.
    /// </summary>
    /// <param name="itemIds">Specific item IDs, or null to scan library.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of rename previews.</returns>
    public List<RenamePreviewDto> GeneratePreviews(
        IReadOnlyList<Guid>? itemIds,
        CancellationToken cancellationToken)
    {
        IEnumerable<BaseItem> items;

        if (itemIds != null && itemIds.Count > 0)
        {
            var list = new List<BaseItem>();
            foreach (var id in itemIds)
            {
                var item = _libraryManager.GetItemById(id);
                if (item != null)
                {
                    list.Add(item);
                }
            }

            items = list;
        }
        else
        {
            items = _libraryManager.GetItemList(new InternalItemsQuery
            {
                MediaTypes = new[] { Jellyfin.Data.Enums.MediaType.Video, Jellyfin.Data.Enums.MediaType.Audio },
                IsVirtualItem = false,
                Recursive = true
            });
        }

        return GeneratePreviewsCore(items, cancellationToken);
    }

    private List<RenamePreviewDto> GeneratePreviewsCore(
        IEnumerable<BaseItem> items,
        CancellationToken cancellationToken)
    {
        var previews = new List<RenamePreviewDto>();

        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(item.Path) || !File.Exists(item.Path))
            {
                continue;
            }

            var proposed = ComputeProposedPath(item);
            if (string.IsNullOrWhiteSpace(proposed))
            {
                continue;
            }

            var currentFilename = Path.GetFileName(item.Path);
            var proposedFilename = Path.GetFileName(proposed);
            var hasChange = !string.Equals(
                Path.GetFullPath(item.Path),
                Path.GetFullPath(proposed),
                StringComparison.Ordinal);

            previews.Add(new RenamePreviewDto
            {
                ItemId = item.Id,
                ItemName = item.Name ?? currentFilename,
                MediaType = item.MediaType.ToString(),
                CurrentPath = item.Path,
                CurrentFilename = currentFilename,
                ProposedPath = proposed,
                ProposedFilename = proposedFilename,
                HasChange = hasChange,
                Reason = hasChange ? "Cryptic release tags or non-standard formatting detected" : "Already compliant"
            });
        }

        return previews;
    }

    /// <summary>
    /// Executes a batch of renames safely.
    /// </summary>
    /// <param name="itemIds">Item IDs to rename.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Execution summary result.</returns>
    public async Task<ExecuteRenameResultDto> ExecuteRenamesAsync(
        IReadOnlyList<Guid> itemIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(itemIds);
        var result = new ExecuteRenameResultDto
        {
            TotalRequested = itemIds.Count
        };

        foreach (var id in itemIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = _libraryManager.GetItemById(id);
            if (item == null || string.IsNullOrWhiteSpace(item.Path) || !File.Exists(item.Path))
            {
                result.Failed++;
                result.Errors.Add(string.Format(CultureInfo.InvariantCulture, "Item {0} was not found or file is missing.", id));
                continue;
            }

            var proposed = ComputeProposedPath(item);
            if (string.IsNullOrWhiteSpace(proposed) || string.Equals(Path.GetFullPath(item.Path), Path.GetFullPath(proposed), StringComparison.Ordinal))
            {
                result.Succeeded++;
                continue;
            }

            var success = await _safeSync.SafeRenameAsync(item, proposed, cancellationToken).ConfigureAwait(false);
            if (success)
            {
                result.Succeeded++;
            }
            else
            {
                result.Failed++;
                result.Errors.Add(string.Format(CultureInfo.InvariantCulture, "Rename failed for {0}", item.Name));
            }
        }

        return result;
    }

    /// <summary>
    /// Strips cryptic scene tokens, release tags and hash prefixes from a filename string.
    /// </summary>
    /// <param name="rawName">Original filename or stem.</param>
    /// <returns>Cleaned readable name.</returns>
    public static string CleanStem(string rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName))
        {
            return string.Empty;
        }

        // 1. Strip leading hash prefixes (e.g. 1155fe1424d52a22__01_track)
        var cleaned = HashPrefixRegex.Replace(rawName, string.Empty);

        // 2. Strip trailing release group tag (e.g. -SPARKS, -EVO)
        cleaned = ReleaseGroupRegex.Replace(cleaned, string.Empty);

        // 3. Strip scene quality and release tags
        cleaned = SceneTagsRegex.Replace(cleaned, " ");

        // 4. Convert underscores and dots into spaces (preserving brackets)
        cleaned = cleaned.Replace('_', ' ').Replace('.', ' ');

        // 5. Consolidate whitespace
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();

        // 6. Sanitize illegal characters
        return SanitizeFileName(cleaned);
    }

    /// <summary>
    /// Computes the proposed standardized full path for a media item.
    /// </summary>
    /// <param name="item">Jellyfin base item.</param>
    /// <returns>New proposed absolute path.</returns>
    public string ComputeProposedPath(BaseItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var originalPath = item.Path;
        if (string.IsNullOrWhiteSpace(originalPath))
        {
            return string.Empty;
        }

        var directory = Path.GetDirectoryName(originalPath) ?? string.Empty;
        var ext = Path.GetExtension(originalPath);

        if (item is Movie movie)
        {
            var title = SanitizeFileName(movie.Name ?? Path.GetFileNameWithoutExtension(originalPath));
            var yearStr = movie.ProductionYear.HasValue ? string.Format(CultureInfo.InvariantCulture, " ({0})", movie.ProductionYear.Value) : string.Empty;

            var res = movie.Width >= 3800 ? "4K" : movie.Width >= 1900 ? "1080p" : movie.Width >= 1200 ? "720p" : string.Empty;
            var codec = !string.IsNullOrWhiteSpace(movie.VideoType.ToString()) ? movie.Container : "MKV";

            var tag = !string.IsNullOrWhiteSpace(res) ? string.Format(CultureInfo.InvariantCulture, " [{0}]", res) : string.Empty;
            var targetName = string.Format(CultureInfo.InvariantCulture, "{0}{1}{2}{3}", title, yearStr, tag, ext);

            return Path.Combine(directory, targetName);
        }

        if (item is Episode episode)
        {
            var series = SanitizeFileName(episode.SeriesName ?? "Unknown Series");
            var seasonNum = episode.ParentIndexNumber ?? 1;
            var epNum = episode.IndexNumber ?? 1;
            var epTitle = SanitizeFileName(episode.Name ?? string.Empty);

            var targetName = string.Format(
                CultureInfo.InvariantCulture,
                "{0} - S{1:D2}E{2:D2} - {3}{4}",
                series,
                seasonNum,
                epNum,
                epTitle,
                ext);

            return Path.Combine(directory, targetName);
        }

        if (item is Audio audio)
        {
            var trackNum = audio.IndexNumber.HasValue ? string.Format(CultureInfo.InvariantCulture, "{0:D2} - ", audio.IndexNumber.Value) : string.Empty;
            var artist = audio.AlbumArtists.Count > 0 ? audio.AlbumArtists[0] : (audio.Artists.Count > 0 ? audio.Artists[0] : string.Empty);
            var title = SanitizeFileName(audio.Name ?? Path.GetFileNameWithoutExtension(originalPath));

            string targetName;
            if (!string.IsNullOrWhiteSpace(artist))
            {
                targetName = string.Format(CultureInfo.InvariantCulture, "{0}{1} - {2}{3}", trackNum, SanitizeFileName(artist), title, ext);
            }
            else
            {
                targetName = string.Format(CultureInfo.InvariantCulture, "{0}{1}{2}", trackNum, title, ext);
            }

            return Path.Combine(directory, targetName);
        }

        // Generic fallback: clean stem
        var stem = Path.GetFileNameWithoutExtension(originalPath);
        var cleanStem = CleanStem(stem);
        return Path.Combine(directory, cleanStem + ext);
    }

    /// <summary>
    /// Removes invalid filesystem characters across Windows and Linux.
    /// </summary>
    /// <param name="name">Input string.</param>
    /// <returns>Sanitized string.</returns>
    public static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        char[] invalid = { '/', '\\', ':', '*', '?', '"', '<', '>', '|' };
        var sanitized = name;
        foreach (var c in invalid)
        {
            sanitized = sanitized.Replace(c.ToString(), string.Empty);
        }

        return sanitized.Trim();
    }
}
