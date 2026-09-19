using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MediaTools.Services;

/// <summary>
/// Service to perform safe physical renames and synchronize SQLite records directly without foreign-key errors.
/// </summary>
public partial class SafeDatabaseSyncService
{
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<SafeDatabaseSyncService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SafeDatabaseSyncService"/> class.
    /// </summary>
    /// <param name="libraryManager">Library manager.</param>
    /// <param name="logger">Logger.</param>
    public SafeDatabaseSyncService(
        ILibraryManager libraryManager,
        ILogger<SafeDatabaseSyncService> logger)
    {
        _libraryManager = libraryManager;
        _logger = logger;
    }

    /// <summary>
    /// Safely moves a media file and updates the existing Jellyfin database entry, preserving UserData and foreign keys.
    /// </summary>
    /// <param name="item">Jellyfin media item.</param>
    /// <param name="destinationPath">Target physical file path.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the move and DB update succeeded.</returns>
    public async Task<bool> SafeRenameAsync(
        BaseItem item,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        var sourcePath = item.Path;
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            LogSourceFileNotFound(_logger, sourcePath ?? "empty");
            return false;
        }

        if (string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(destinationPath), StringComparison.OrdinalIgnoreCase))
        {
            // Case-only rename or identical path
            if (string.Equals(sourcePath, destinationPath, StringComparison.Ordinal))
            {
                return true;
            }
        }

        try
        {
            var destDir = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            // 1. Physical move
            File.Move(sourcePath, destinationPath, overwrite: true);

            // 2. Synchronize item in database
            item.Path = destinationPath;
            item.Container = Path.GetExtension(destinationPath).TrimStart('.');

            await _libraryManager.UpdateItemAsync(
                item,
                item.GetParent(),
                ItemUpdateType.MetadataEdit,
                cancellationToken).ConfigureAwait(false);

            LogRenameSuccess(_logger, item.Id, sourcePath, destinationPath);
            return true;
        }
        catch (Exception ex)
        {
            LogRenameFailed(_logger, item.Id, sourcePath, destinationPath, ex);
            return false;
        }
    }

    /// <summary>
    /// Removes secondary part records from Jellyfin after a split movie merge without deleting physical files.
    /// </summary>
    /// <param name="partItem">The redundant part item to remove from the DB.</param>
    public void SafeRemovePartRecord(BaseItem partItem)
    {
        ArgumentNullException.ThrowIfNull(partItem);
        try
        {
            _libraryManager.DeleteItem(
                partItem,
                new DeleteOptions { DeleteFileLocation = false });
            LogPartRemoved(_logger, partItem.Id, partItem.Path);
        }
        catch (Exception ex)
        {
            LogPartRemoveFailed(_logger, partItem.Id, ex);
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Source file {SourcePath} does not exist for rename.")]
    private static partial void LogSourceFileNotFound(ILogger logger, string sourcePath);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Successfully renamed item {ItemId} from {Source} to {Destination}.")]
    private static partial void LogRenameSuccess(ILogger logger, Guid itemId, string source, string destination);

    [LoggerMessage(EventId = 3, Level = LogLevel.Error, Message = "Failed to safely rename item {ItemId} from {Source} to {Destination}.")]
    private static partial void LogRenameFailed(ILogger logger, Guid itemId, string source, string destination, Exception exception);

    [LoggerMessage(EventId = 4, Level = LogLevel.Information, Message = "Safely removed redundant part item {ItemId} ({Path}) from database.")]
    private static partial void LogPartRemoved(ILogger logger, Guid itemId, string path);

    [LoggerMessage(EventId = 5, Level = LogLevel.Warning, Message = "Failed to remove part item {ItemId} from database.")]
    private static partial void LogPartRemoveFailed(ILogger logger, Guid itemId, Exception exception);
}
