using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.MusicToolkit.Services;

/// <summary>
/// Parsed filename result.
/// </summary>
/// <param name="Track">Track number or null.</param>
/// <param name="Artist">Artist name.</param>
/// <param name="Title">Title.</param>
/// <param name="Mix">Mix name or null.</param>
/// <param name="CleanFileName">Cleaned filename without extension.</param>
public sealed record ParsedName(int? Track, string Artist, string Title, string? Mix, string CleanFileName);

/// <summary>
/// Cleans cryptic filenames (hex-hash prefixes) and parses artist/title/track metadata.
/// </summary>
public sealed partial class FilenameCleaner
{
    [GeneratedRegex("^[a-f0-9]{8,32}__([a-f0-9]{8,32}__)?", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex HashPrefixRegex();

    [GeneratedRegex("^(?<track>\\d{1,3})?[_\\s-]*(?<artist>.+?)[_\\s-]+(?<title>.+?)(?:_\\((?<mix>.+)\\))?$", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex TagFallbackRegex();

    [GeneratedRegex("([a-z0-9])([A-Z])", RegexOptions.Compiled)]
    private static partial Regex CamelCaseRegex();

    private static readonly char[] InvalidFileNameChars =
        { '/', '\\', ':', '*', '?', '"', '<', '>', '|' };

    /// <summary>
    /// Strips leading hex-hash prefixes from a filename stem.
    /// </summary>
    /// <param name="stem">Filename without extension.</param>
    /// <returns>Stem without hash prefixes.</returns>
    public static string StripHashPrefixes(string stem)
    {
        ArgumentNullException.ThrowIfNull(stem);
        var result = stem;
        string previous;
        do
        {
            previous = result;
            result = HashPrefixRegex().Replace(result, string.Empty);
        }
        while (!string.Equals(previous, result, StringComparison.Ordinal));

        return result;
    }

    /// <summary>
    /// Parses a filename into track/artist/title/mix parts.
    /// Separator priority: "_-_", " - ", " — " (first occurrence wins),
    /// then the single-underscore fallback pattern.
    /// </summary>
    /// <param name="fileName">Full filename with extension.</param>
    /// <returns>Parsed name.</returns>
    public static ParsedName Parse(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var stripped = StripHashPrefixes(stem);

        string? mix = null;
        var mixMatch = Regex.Match(stripped, @"_?\((?<mix>.+)\)$");
        if (mixMatch.Success)
        {
            mix = Humanize(mixMatch.Groups["mix"].Value);
            stripped = stripped.Substring(0, mixMatch.Index);
        }

        int? track = null;
        string artist;
        string title;

        var sepIndex = IndexOfArtistTitleSeparator(stripped);
        if (sepIndex.HasValue)
        {
            var sep = sepIndex.Value.Index;
            var len = sepIndex.Value.Length;
            var left = stripped.Substring(0, sep).Trim('_', ' ', '-');
            var right = stripped.Substring(sep + len).Trim('_', ' ', '-');
            (track, artist) = ParseTrackAndArtist(left);
            title = Humanize(right);
            artist = Sanitize(artist);
        }
        else
        {
            var match = TagFallbackRegex().Match(stripped);
            if (match.Success)
            {
                var trackGroup = match.Groups["track"];
                if (trackGroup.Success && int.TryParse(trackGroup.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                {
                    track = parsed;
                }

                artist = Humanize(match.Groups["artist"].Value);
                title = Humanize(match.Groups["title"].Value);
            }
            else
            {
                artist = "Unknown Artist";
                title = Humanize(stripped);
            }

            artist = Sanitize(artist);
            title = Sanitize(title);
        }

        title = Sanitize(title);
        mix = mix is null ? null : Sanitize(mix);

        var clean = track.HasValue
            ? string.Format(CultureInfo.InvariantCulture, "{0:00} - {1} - {2}", track.Value, artist, title)
            : string.Format(CultureInfo.InvariantCulture, "{0} - {1}", artist, title);

        if (!string.IsNullOrWhiteSpace(mix))
        {
            clean += $" ({mix})";
        }

        return new ParsedName(track, artist, title, mix, clean);
    }

    /// <summary>
    /// Builds the destination relative path from a rename pattern.
    /// </summary>
    /// <param name="pattern">Pattern like {Artist}/{Album}/{TrackNumber:02d} - {Title}.</param>
    /// <param name="artist">Artist.</param>
    /// <param name="album">Album.</param>
    /// <param name="trackNumber">Track number.</param>
    /// <param name="title">Title.</param>
    /// <param name="extension">File extension including dot.</param>
    /// <returns>Relative path.</returns>
    public static string BuildRelativePath(string pattern, string artist, string album, int trackNumber, string title, string extension)
    {
        var result = (pattern ?? "{Artist}/{Album}/{TrackNumber:02d} - {Title}")
            .Replace("{Artist}", Sanitize(artist), StringComparison.Ordinal)
            .Replace("{Album}", Sanitize(album), StringComparison.Ordinal)
            .Replace("{TrackNumber:02d}", trackNumber.ToString("00", CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{TrackNumber}", trackNumber.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{Title}", Sanitize(title), StringComparison.Ordinal);

        return result + extension;
    }

    private static (int Index, int Length)? IndexOfArtistTitleSeparator(string value)
    {
        var candidates = new[] { "_-_", " - ", " — ", " – " };
        foreach (var sep in candidates)
        {
            var idx = value.IndexOf(sep, StringComparison.Ordinal);
            if (idx >= 0)
            {
                return (idx, sep.Length);
            }
        }

        return null;
    }

    private static (int? Track, string Artist) ParseTrackAndArtist(string left)
    {
        var match = Regex.Match(left, @"^(?<track>\d{1,3})[_\s-]+(?<artist>.+)$");
        if (match.Success && int.TryParse(match.Groups["track"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var track))
        {
            return (track, Humanize(match.Groups["artist"].Value));
        }

        if (int.TryParse(left, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        {
            return (null, "Unknown Artist");
        }

        return (null, Humanize(left));
    }

    private static string Humanize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var spaced = value.Replace('_', ' ').Replace('-', ' ').Trim();
        spaced = CamelCaseRegex().Replace(spaced, "$1 $2");
        spaced = Regex.Replace(spaced, "\\s+", " ");
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(spaced.ToLowerInvariant());
    }

    private static string Sanitize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Unknown";
        }

        var result = value.Trim();
        foreach (var c in InvalidFileNameChars)
        {
            result = result.Replace(c, '-');
        }

        result = Regex.Replace(result, "\\s+", " ").Trim();
        result = result.Trim('.', ' ');
        return string.IsNullOrWhiteSpace(result) ? "Unknown" : result;
    }
}
