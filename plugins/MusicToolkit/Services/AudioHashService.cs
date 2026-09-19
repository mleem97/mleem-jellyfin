using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MusicToolkit.Services;

/// <summary>
/// Duplicate group result.
/// </summary>
/// <param name="AudioHash">MD5 of the audio stream.</param>
/// <param name="MasterPath">Best-quality file kept as master.</param>
/// <param name="MasterScore">Quality score of the master.</param>
/// <param name="DuplicatePaths">Files marked as duplicates.</param>
public sealed record DuplicateGroup(string AudioHash, string MasterPath, int MasterScore, IReadOnlyList<string> DuplicatePaths);

/// <summary>
/// Quality metadata for one file.
/// </summary>
/// <param name="Path">File path.</param>
/// <param name="AudioHash">Audio hash.</param>
/// <param name="Score">Quality score.</param>
/// <param name="Codec">Codec description.</param>
/// <param name="Bitrate">Audio bitrate in kbps.</param>
public sealed record ScoredAudio(string Path, string AudioHash, int Score, string Codec, int Bitrate);

/// <summary>
/// Computes content hashes over the pure audio stream and groups duplicates by quality.
/// </summary>
public sealed class AudioHashService
{
    private readonly ILogger<AudioHashService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AudioHashService"/> class.
    /// </summary>
    /// <param name="logger">Logger.</param>
    public AudioHashService(ILogger<AudioHashService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Computes the MD5 hash of the pure audio stream, skipping tag headers.
    /// MD5 is used as a non-cryptographic content fingerprint for duplicate detection only.
    /// </summary>
    /// <param name="path">Audio file path.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Lowercase hex MD5.</returns>
    [SuppressMessage("Security", "CA5351:Do Not Use Broken Cryptographic Algorithms", Justification = "MD5 is used as an audio-content fingerprint for duplicate detection, not for security purposes.")]
    public async Task<string> ComputeAudioHashAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(path);
        return await Task.Run(() => ComputeAudioHash(path), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Groups files by identical audio hash and selects the master file.
    /// </summary>
    /// <param name="paths">Candidate audio files.</param>
    /// <param name="preferredCodec">Preferred codec (e.g. flac).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Duplicate groups (only groups with more than one file).</returns>
    public async Task<IReadOnlyList<DuplicateGroup>> FindDuplicatesAsync(
        IEnumerable<string> paths,
        string preferredCodec = "flac",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var scored = new List<ScoredAudio>();
        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var hash = await ComputeAudioHashAsync(path, cancellationToken).ConfigureAwait(false);
                var (score, codec, bitrate) = ScoreFile(path, preferredCodec);
                scored.Add(new ScoredAudio(path, hash, score, codec, bitrate));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Skipping unhashable audio file {Path}", path);
            }
        }

        return scored
            .GroupBy(s => s.AudioHash, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g =>
            {
                var ordered = g.OrderByDescending(s => s.Score).ThenBy(s => s.Path, StringComparer.Ordinal).ToList();
                var master = ordered[0];
                return new DuplicateGroup(
                    g.Key,
                    master.Path,
                    master.Score,
                    ordered.Skip(1).Select(s => s.Path).ToList());
            })
            .ToList();
    }

    /// <summary>
    /// Scores one file: FLAC/lossless 100, MP3 320 = 80, otherwise bitrate/10.
    /// </summary>
    /// <param name="path">File path.</param>
    /// <param name="preferredCodec">Preferred codec bonus.</param>
    /// <returns>Score, codec and bitrate.</returns>
    public static (int Score, string Codec, int Bitrate) ScoreFile(string path, string preferredCodec = "flac")
    {
        var extension = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        int bitrate = 0;
        string codec = extension;

        try
        {
            using var tagFile = TagLib.File.Create(path);
            var codecs = tagFile.Properties.Codecs.ToList();
            codec = codecs.Count > 0
                ? string.Join("+", codecs.Select(c => c.Description ?? c.MediaTypes.ToString()))
                : extension;
            bitrate = tagFile.Properties.AudioBitrate;

            var isLossless = extension is "flac" or "alac" or "wav" or "aiff"
                || codec.Contains("FLAC", StringComparison.OrdinalIgnoreCase)
                || codec.Contains("ALAC", StringComparison.OrdinalIgnoreCase)
                || codec.Contains("PCM", StringComparison.OrdinalIgnoreCase);

            if (isLossless)
            {
                var score = 100;
                if (!string.IsNullOrWhiteSpace(preferredCodec) && extension.Equals(preferredCodec, StringComparison.OrdinalIgnoreCase))
                {
                    score += 5;
                }

                return (score, codec, bitrate);
            }

            if (bitrate >= 320)
            {
                return (80, codec, bitrate);
            }

            if (bitrate > 0)
            {
                return (Math.Max(1, bitrate / 10), codec, bitrate);
            }
        }
        catch (Exception)
        {
            // Fall through to extension-based fallback.
        }

        return extension switch
        {
            "flac" or "alac" or "wav" => (100, codec, bitrate),
            "mp3" => (bitrate >= 320 ? 80 : Math.Max(1, bitrate / 10), codec, bitrate),
            _ => (Math.Max(1, bitrate / 10), codec, bitrate),
        };
    }

    [SuppressMessage("Security", "CA5351:Do Not Use Broken Cryptographic Algorithms", Justification = "MD5 is used as an audio-content fingerprint for duplicate detection, not for security purposes.")]
    private static string ComputeAudioHash(string path)
    {
        var audioRegion = ExtractAudioRegion(path);
        var hash = MD5.HashData(audioRegion);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static byte[] ExtractAudioRegion(string path)
    {
        using var stream = System.IO.File.OpenRead(path);

        long audioStart = 0;
        long audioEnd = stream.Length;

        // ID3v2 header: "ID3" + 2 version bytes + flags + 4 syncsafe size bytes.
        var header = new byte[10];
        stream.Seek(0, SeekOrigin.Begin);
        if (stream.Read(header, 0, 10) == 10
            && header[0] == (byte)'I' && header[1] == (byte)'D' && header[2] == (byte)'3')
        {
            int size = ((header[6] & 0x7F) << 21) | ((header[7] & 0x7F) << 14) | ((header[8] & 0x7F) << 7) | (header[9] & 0x7F);
            audioStart = 10 + size;
        }

        // Skip trailing ID3v1 (128 bytes, "TAG" magic).
        if (stream.Length >= 128)
        {
            stream.Seek(-128, SeekOrigin.End);
            var tail = new byte[3];
            if (stream.Read(tail, 0, 3) == 3 && tail[0] == (byte)'T' && tail[1] == (byte)'A' && tail[2] == (byte)'G')
            {
                audioEnd = stream.Length - 128;
            }
        }

        var length = Math.Max(0, audioEnd - audioStart);
        var buffer = new byte[length];
        stream.Seek(audioStart, SeekOrigin.Begin);
        var read = 0;
        while (read < length)
        {
            var n = stream.Read(buffer, read, (int)Math.Min(81920, length - read));
            if (n == 0)
            {
                break;
            }

            read += n;
        }

        if (read != length)
        {
            Array.Resize(ref buffer, read);
        }

        return buffer;
    }
}
