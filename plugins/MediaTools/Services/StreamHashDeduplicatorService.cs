using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MediaTools.Controllers.Models;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MediaTools.Services;

/// <summary>
/// Service to compute byte-accurate stream hashes (ignoring ID3/metadata headers) and detect duplicates.
/// </summary>
public partial class StreamHashDeduplicatorService
{
    private readonly ILibraryManager _libraryManager;
    private readonly SafeDatabaseSyncService _safeSync;
    private readonly ILogger<StreamHashDeduplicatorService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="StreamHashDeduplicatorService"/> class.
    /// </summary>
    /// <param name="libraryManager">Library manager.</param>
    /// <param name="safeSync">Safe database sync service.</param>
    /// <param name="logger">Logger.</param>
    public StreamHashDeduplicatorService(
        ILibraryManager libraryManager,
        SafeDatabaseSyncService safeSync,
        ILogger<StreamHashDeduplicatorService> logger)
    {
        _libraryManager = libraryManager;
        _safeSync = safeSync;
        _logger = logger;
    }

    /// <summary>
    /// Scans the library for exact duplicate media streams.
    /// </summary>
    /// <param name="mediaType">"Audio" or "Video".</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of duplicate groups.</returns>
    public async Task<List<DuplicateGroupDto>> ScanDuplicatesAsync(
        string mediaType,
        CancellationToken cancellationToken)
    {
        var targetType = string.Equals(mediaType, "Video", StringComparison.OrdinalIgnoreCase)
            ? Jellyfin.Data.Enums.MediaType.Video
            : Jellyfin.Data.Enums.MediaType.Audio;

        var items = _libraryManager.GetItemList(new InternalItemsQuery
        {
            MediaTypes = new[] { targetType },
            IsVirtualItem = false,
            Recursive = true
        });

        var hashMap = new ConcurrentDictionary<string, List<BaseItem>>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(item.Path) || !File.Exists(item.Path))
            {
                continue;
            }

            var hash = await ComputeStreamHashAsync(item.Path, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(hash))
            {
                continue;
            }

            hashMap.AddOrUpdate(
                hash,
                _ => new List<BaseItem> { item },
                (_, list) =>
                {
                    lock (list)
                    {
                        list.Add(item);
                    }

                    return list;
                });
        }

        var groups = new List<DuplicateGroupDto>();
        foreach (var kvp in hashMap)
        {
            if (kvp.Value.Count < 2)
            {
                continue;
            }

            var fileItems = new List<DuplicateFileItemDto>();
            foreach (var item in kvp.Value)
            {
                var fileInfo = new FileInfo(item.Path);
                var score = ComputeQualityScore(item, fileInfo.Exists ? fileInfo.Length : 0);
                fileItems.Add(new DuplicateFileItemDto
                {
                    ItemId = item.Id,
                    Path = item.Path,
                    FileSizeBytes = fileInfo.Exists ? fileInfo.Length : 0,
                    Codec = item.Container ?? Path.GetExtension(item.Path).TrimStart('.'),
                    QualityScore = score
                });
            }

            // Elect master: highest quality score, then largest file
            fileItems.Sort((a, b) =>
            {
                var sc = b.QualityScore.CompareTo(a.QualityScore);
                return sc != 0 ? sc : b.FileSizeBytes.CompareTo(a.FileSizeBytes);
            });

            fileItems[0].IsMaster = true;

            var firstItem = kvp.Value[0];
            groups.Add(new DuplicateGroupDto
            {
                StreamHash = kvp.Key,
                MediaTitle = firstItem.Name ?? Path.GetFileNameWithoutExtension(firstItem.Path),
                MediaType = targetType.ToString(),
                MasterPath = fileItems[0].Path,
                Files = fileItems
            });
        }

        return groups;
    }

    /// <summary>
    /// Moves specified duplicate files into the quarantine directory.
    /// </summary>
    /// <param name="filePaths">Files to quarantine.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of files successfully quarantined.</returns>
    public int QuarantineDuplicates(IReadOnlyList<string> filePaths, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filePaths);
        var quarantinedCount = 0;
        var folderName = Plugin.Instance?.Configuration?.QuarantineFolderName ?? "_duplicates";

        foreach (var path in filePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                continue;
            }

            try
            {
                var dir = Path.GetDirectoryName(path);
                if (string.IsNullOrEmpty(dir))
                {
                    continue;
                }

                var qDir = Path.Combine(dir, folderName);
                if (!Directory.Exists(qDir))
                {
                    Directory.CreateDirectory(qDir);
                }

                var filename = Path.GetFileName(path);
                var targetPath = Path.Combine(qDir, filename);

                File.Move(path, targetPath, overwrite: true);
                quarantinedCount++;
                LogFileQuarantined(_logger, path, targetPath);
            }
            catch (Exception ex)
            {
                LogQuarantineFailed(_logger, path, ex);
            }
        }

        return quarantinedCount;
    }

    /// <summary>
    /// Computes an MD5 hash of the raw stream, skipping metadata headers when possible.
    /// </summary>
    /// <param name="filePath">Path to media file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Uppercase hex hash.</returns>
    public static async Task<string> ComputeStreamHashAsync(string filePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return string.Empty;
        }

        try
        {
            // Use TagLibSharp to find start and length of media stream (skipping ID3v2/Vorbis tags)
            long startOffset = 0;
            long streamLength = -1;

            try
            {
                using var tagFile = TagLib.File.Create(filePath);
                if (tagFile != null)
                {
                    startOffset = tagFile.InvariantStartPosition;
                    streamLength = tagFile.InvariantEndPosition - startOffset;
                }
            }
            catch
            {
                // Fallback to direct file read if tag parsing fails
                startOffset = 0;
                streamLength = -1;
            }

            await using var stream = File.OpenRead(filePath);
            if (startOffset > 0 && startOffset < stream.Length)
            {
                stream.Seek(startOffset, SeekOrigin.Begin);
            }

            using var md5 = MD5.Create();
            byte[] hashBytes;
            if (streamLength > 0 && startOffset + streamLength <= stream.Length)
            {
                // Read exact invariant portion
                using var limited = new SubStream(stream, streamLength);
                hashBytes = await md5.ComputeHashAsync(limited, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                hashBytes = await md5.ComputeHashAsync(stream, cancellationToken).ConfigureAwait(false);
            }

            return Convert.ToHexString(hashBytes);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static int ComputeQualityScore(BaseItem item, long fileSizeBytes)
    {
        if (item is Audio audio)
        {
            var ext = Path.GetExtension(audio.Path).ToLowerInvariant();
            if (ext is ".flac" or ".wav" or ".alac" or ".aiff")
            {
                return 100;
            }

            var kbps = (audio.TotalBitrate ?? 0) / 1000;
            if (kbps >= 320)
            {
                return 80;
            }

            if (kbps >= 256)
            {
                return 70;
            }

            if (kbps >= 192)
            {
                return 60;
            }

            return Math.Max(10, kbps / 4);
        }

        if (item is Movie movie)
        {
            var width = movie.Width;
            if (width >= 3800)
            {
                return 100;
            }

            if (width >= 1900)
            {
                return 80;
            }

            if (width >= 1200)
            {
                return 60;
            }

            return 40;
        }

        return (int)Math.Min(100, fileSizeBytes / (1024 * 1024));
    }

    private sealed class SubStream : Stream
    {
        private readonly Stream _baseStream;
        private readonly long _length;
        private long _position;

        public SubStream(Stream baseStream, long length)
        {
            _baseStream = baseStream;
            _length = length;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => _length;

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override void Flush() => _baseStream.Flush();

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_position >= _length)
            {
                return 0;
            }

            var toRead = (int)Math.Min(count, _length - _position);
            var read = _baseStream.Read(buffer, offset, toRead);
            _position += read;
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _baseStream.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Quarantined duplicate file {Original} to {Target}.")]
    private static partial void LogFileQuarantined(ILogger logger, string original, string target);

    [LoggerMessage(EventId = 2, Level = LogLevel.Error, Message = "Failed to quarantine file {Path}.")]
    private static partial void LogQuarantineFailed(ILogger logger, string path, Exception exception);
}
