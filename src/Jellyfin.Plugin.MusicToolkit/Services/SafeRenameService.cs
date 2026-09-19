using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MusicToolkit.Services;

/// <summary>
/// Renames audio files physically while keeping the Jellyfin ItemId stable,
/// avoiding SQLite Error 19 FOREIGN KEY failures in UserData.
/// </summary>
public sealed partial class SafeRenameService
{
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<SafeRenameService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SafeRenameService"/> class.
    /// </summary>
    /// <param name="libraryManager">Library manager.</param>
    /// <param name="logger">Logger.</param>
    public SafeRenameService(ILibraryManager libraryManager, ILogger<SafeRenameService> logger)
    {
        _libraryManager = libraryManager;
        _logger = logger;
    }

    /// <summary>
    /// Moves a file and updates the linked Jellyfin Audio item in place.
    /// Only callable from the admin-elevated MusicToolkit API.
    /// </summary>
    /// <param name="sourcePath">Existing file path.</param>
    /// <param name="destPath">Target file path.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the Jellyfin item was found and updated.</returns>
    [SuppressMessage("Security", "CA3003:Review code for file path injection vulnerabilities", Justification = "Admin-only API (RequiresElevation); source paths originate from the server-side library scan, not from anonymous input.")]
    public async Task<bool> RenameAsync(string sourcePath, string destPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destPath);

        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("Source audio file not found.", sourcePath);
        }

        var destDir = Path.GetDirectoryName(destPath);
        if (!string.IsNullOrWhiteSpace(destDir))
        {
            Directory.CreateDirectory(destDir);
        }

        if (File.Exists(destPath) && !string.Equals(sourcePath, destPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException($"Destination already exists: {destPath}");
        }

        File.Move(sourcePath, destPath, overwrite: false);
        LogMovedAudioFile(_logger, sourcePath, destPath);

        var audio = FindAudioByPath(sourcePath) ?? FindAudioByPath(destPath);
        if (audio is null)
        {
            LogNoAudioItem(_logger, sourcePath);
            return false;
        }

        audio.Path = destPath;
        await _libraryManager.UpdateItemAsync(audio, audio.GetParent(), ItemUpdateType.MetadataEdit, cancellationToken).ConfigureAwait(false);
        LogUpdatedItem(_logger, audio.Id, destPath);
        return true;
    }

    private Audio? FindAudioByPath(string path)
    {
        var query = new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.Audio },
            Recursive = true,
        };

        foreach (var item in _libraryManager.GetItemList(query))
        {
            if (item is Audio audio && string.Equals(audio.Path, path, StringComparison.OrdinalIgnoreCase))
            {
                return audio;
            }
        }

        return null;
    }

    [LoggerMessage(EventId = 0, Level = LogLevel.Information, Message = "Moved audio file {Source} to {Dest}")]
    private static partial void LogMovedAudioFile(ILogger logger, string source, string dest);

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "No Jellyfin Audio item found for {Source}; file kept on disk, rescan will pick it up")]
    private static partial void LogNoAudioItem(ILogger logger, string source);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Updated Jellyfin item {Id} path to {Dest} (ItemId stable)")]
    private static partial void LogUpdatedItem(ILogger logger, Guid id, string dest);
}
