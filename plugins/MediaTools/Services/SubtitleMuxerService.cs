using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.MediaTools.Services;

/// <summary>
/// Service to locate external subtitle files and build FFmpeg mapping parameters.
/// </summary>
public class SubtitleMuxerService
{
    private static readonly string[] SubtitleExtensions = { ".srt", ".vtt", ".ass", ".ssa", ".sub" };

    private static readonly Dictionary<string, string> LanguageMapping = new(StringComparer.OrdinalIgnoreCase)
    {
        ["de"] = "ger",
        ["ger"] = "ger",
        ["deu"] = "ger",
        ["deutsch"] = "ger",
        ["german"] = "ger",
        ["en"] = "eng",
        ["eng"] = "eng",
        ["english"] = "eng",
        ["fr"] = "fra",
        ["fra"] = "fra",
        ["fre"] = "fra",
        ["es"] = "spa",
        ["spa"] = "spa",
        ["it"] = "ita",
        ["ita"] = "ita",
        ["nl"] = "nld",
        ["nld"] = "nld",
        ["dut"] = "nld",
        ["ru"] = "rus",
        ["rus"] = "rus",
        ["ja"] = "jpn",
        ["jpn"] = "jpn",
        ["ko"] = "kor",
        ["kor"] = "kor",
        ["zh"] = "chi",
        ["chi"] = "chi",
        ["zho"] = "chi",
    };

    /// <summary>
    /// Represents a discovered external subtitle track.
    /// </summary>
    public sealed class DiscoveredSubtitle
    {
        public string FilePath { get; set; } = string.Empty;

        public string LanguageCode { get; set; } = "und";

        public string Title { get; set; } = string.Empty;

        public bool IsForced { get; set; }

        public bool IsDefault { get; set; }
    }

    /// <summary>
    /// Finds all external subtitle files associated with a video file in the same directory.
    /// </summary>
    /// <param name="videoPath">Absolute path to video file.</param>
    /// <returns>List of discovered subtitles.</returns>
    public List<DiscoveredSubtitle> FindSubtitlesForVideo(string videoPath)
    {
        var results = new List<DiscoveredSubtitle>();
        if (string.IsNullOrWhiteSpace(videoPath) || !File.Exists(videoPath))
        {
            return results;
        }

        var dir = Path.GetDirectoryName(videoPath);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
        {
            return results;
        }

        var stem = Path.GetFileNameWithoutExtension(videoPath);
        var files = Directory.GetFiles(dir);

        foreach (var file in files)
        {
            var ext = Path.GetExtension(file);
            if (!IsSubtitleExtension(ext))
            {
                continue;
            }

            var subFilename = Path.GetFileNameWithoutExtension(file);
            if (!subFilename.StartsWith(stem, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var suffix = subFilename.Substring(stem.Length).TrimStart('.', '_', '-', ' ');
            var parsed = ParseLanguageAndFlags(suffix);
            parsed.FilePath = file;
            results.Add(parsed);
        }

        return results;
    }

    /// <summary>
    /// Builds the FFmpeg input and mapping argument string for external subtitles.
    /// </summary>
    /// <param name="subtitles">List of discovered subtitles.</param>
    /// <param name="startIndex">Input index offset (video is typically index 0).</param>
    /// <returns>Arguments snippet to append to the FFmpeg command.</returns>
    public string BuildFFmpegArguments(IReadOnlyList<DiscoveredSubtitle> subtitles, int startIndex = 1)
    {
        if (subtitles == null || subtitles.Count == 0)
        {
            return string.Empty;
        }

        var inputs = new System.Text.StringBuilder();
        var maps = new System.Text.StringBuilder();

        for (var i = 0; i < subtitles.Count; i++)
        {
            var sub = subtitles[i];
            var inputIndex = startIndex + i;
            _ = inputs.AppendFormat(CultureInfo.InvariantCulture, "-i \"{0}\" ", sub.FilePath);
            _ = maps.AppendFormat(CultureInfo.InvariantCulture, "-map {0}:s? ", inputIndex);
            _ = maps.AppendFormat(CultureInfo.InvariantCulture, "-metadata:s:s:{0} language={1} ", i, sub.LanguageCode);
            if (!string.IsNullOrWhiteSpace(sub.Title))
            {
                _ = maps.AppendFormat(CultureInfo.InvariantCulture, "-metadata:s:s:{0} title=\"{1}\" ", i, sub.Title);
            }

            if (sub.IsForced)
            {
                _ = maps.AppendFormat(CultureInfo.InvariantCulture, "-disposition:s:{0} forced ", i);
            }
        }

        return string.Format(CultureInfo.InvariantCulture, "{0}{1}-c:s srt ", inputs, maps);
    }

    private static bool IsSubtitleExtension(string ext)
    {
        foreach (var s in SubtitleExtensions)
        {
            if (string.Equals(s, ext, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static DiscoveredSubtitle ParseLanguageAndFlags(string suffix)
    {
        var sub = new DiscoveredSubtitle();
        if (string.IsNullOrWhiteSpace(suffix))
        {
            return sub;
        }

        var tokens = suffix.Split(new[] { '.', '_', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var token in tokens)
        {
            if (string.Equals(token, "forced", StringComparison.OrdinalIgnoreCase))
            {
                sub.IsForced = true;
                continue;
            }

            if (string.Equals(token, "default", StringComparison.OrdinalIgnoreCase))
            {
                sub.IsDefault = true;
                continue;
            }

            if (LanguageMapping.TryGetValue(token, out var mapped))
            {
                sub.LanguageCode = mapped;
                sub.Title = char.ToUpperInvariant(mapped[0]) + mapped.Substring(1);
            }
        }

        return sub;
    }
}
