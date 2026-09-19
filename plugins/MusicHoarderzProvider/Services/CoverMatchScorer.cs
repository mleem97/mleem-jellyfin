using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Jellyfin.Plugin.MusicHoarderzProvider.Models;

namespace Jellyfin.Plugin.MusicHoarderzProvider.Services;

/// <summary>
/// Deterministic cover match scoring. Never applies results automatically;
/// it only annotates and sorts candidates.
/// </summary>
public sealed class CoverMatchScorer
{
    private static readonly string[] EditionKeywords = new[]
    {
        "remaster", "remastered", "remasterise", "remasterize",
        "deluxe", "expanded", "anniversary", "special", "edition",
        "bonus", "reissue", "explicit", "clean", "stereo", "mono",
        "version", "tribute", "karaoke", "instrumental",
    };

    /// <summary>
    /// Scores and sorts candidates deterministically (score, width, URL).
    /// </summary>
    /// <param name="query">Original search query.</param>
    /// <param name="candidates">Unscored candidates.</param>
    /// <param name="minimumWidth">Configured minimum width.</param>
    /// <param name="minimumHeight">Configured minimum height.</param>
    /// <returns>Candidates with score and reason, sorted best first.</returns>
    public IReadOnlyList<NormalizedCoverResult> Score(
        CoverSearchQuery query,
        IEnumerable<NormalizedCoverResult> candidates,
        int minimumWidth,
        int minimumHeight)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(candidates);

        var scored = new List<NormalizedCoverResult>();
        foreach (var candidate in candidates)
        {
            if (candidate is null)
            {
                continue;
            }

            scored.Add(ScoreOne(query, candidate, minimumWidth, minimumHeight));
        }

        scored.Sort(static (left, right) =>
        {
            var score = right.Score.CompareTo(left.Score);
            if (score != 0)
            {
                return score;
            }

            var leftWidth = left.Width ?? -1;
            var rightWidth = right.Width ?? -1;
            var width = rightWidth.CompareTo(leftWidth);
            if (width != 0)
            {
                return width;
            }

            return string.Compare(left.Url, right.Url, StringComparison.Ordinal);
        });

        return scored;
    }

    private static NormalizedCoverResult ScoreOne(
        CoverSearchQuery query,
        NormalizedCoverResult candidate,
        int minimumWidth,
        int minimumHeight)
    {
        var reason = new StringBuilder();
        var score = 0;

        var titleScore = MatchScore(Normalize(query.Title), Normalize(TitleOf(candidate)), 40, 25, 12);
        score += titleScore;
        _ = reason.Append("title+").Append(titleScore.ToString(CultureInfo.InvariantCulture)).Append(';');

        var artistScore = MatchScore(Normalize(query.Artist), Normalize(ArtistOf(candidate)), 25, 15, 8);
        score += artistScore;
        _ = reason.Append("artist+").Append(artistScore.ToString(CultureInfo.InvariantCulture)).Append(';');

        var yearScore = YearScore(query.Year, candidate);
        score += yearScore;
        _ = reason.Append("year+").Append(yearScore.ToString(CultureInfo.InvariantCulture)).Append(';');

        var resolutionScore = ResolutionScore(candidate, minimumWidth, minimumHeight);
        score += resolutionScore;
        _ = reason.Append("resolution+").Append(resolutionScore.ToString(CultureInfo.InvariantCulture)).Append(';');

        var aspectScore = AspectScore(candidate);
        score += aspectScore;
        _ = reason.Append("aspect+").Append(aspectScore.ToString(CultureInfo.InvariantCulture)).Append(';');

        var sourceScore = SourceScore(candidate.Source);
        score += sourceScore;
        _ = reason.Append("source+").Append(sourceScore.ToString(CultureInfo.InvariantCulture));

        if (score < 0)
        {
            score = 0;
        }

        if (score > 100)
        {
            score = 100;
        }

        return candidate with { Score = score, ScoreReason = reason.ToString() };
    }

    private static int MatchScore(string query, string candidate, int exact, int contains, int partial)
    {
        if (string.IsNullOrEmpty(query) || string.IsNullOrEmpty(candidate))
        {
            return 0;
        }

        if (string.Equals(query, candidate, StringComparison.Ordinal))
        {
            return exact;
        }

        if (query.Contains(candidate, StringComparison.Ordinal)
            || candidate.Contains(query, StringComparison.Ordinal))
        {
            return contains;
        }

        var queryTokens = new HashSet<string>(query.Split(' ', StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);
        var candidateTokens = new HashSet<string>(candidate.Split(' ', StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);
        if (queryTokens.Count == 0 || candidateTokens.Count == 0)
        {
            return 0;
        }

        var overlap = 0;
        foreach (var token in queryTokens)
        {
            if (candidateTokens.Contains(token))
            {
                overlap++;
            }
        }

        var ratio = (double)overlap / Math.Max(queryTokens.Count, candidateTokens.Count);
        return ratio >= 0.5 ? partial : 0;
    }

    private static int YearScore(int? queryYear, NormalizedCoverResult candidate)
    {
        if (!queryYear.HasValue || !candidate.Year.HasValue)
        {
            return 0;
        }

        if (queryYear.Value == candidate.Year.Value)
        {
            return 10;
        }

        if (Math.Abs(queryYear.Value - candidate.Year.Value) == 1)
        {
            return 4;
        }

        return 0;
    }

    private static int ResolutionScore(NormalizedCoverResult candidate, int minimumWidth, int minimumHeight)
    {
        if (!candidate.Width.HasValue || !candidate.Height.HasValue)
        {
            return 0;
        }

        var meetsWidth = candidate.Width.Value >= minimumWidth;
        var meetsHeight = candidate.Height.Value >= minimumHeight;
        if (meetsWidth && meetsHeight)
        {
            return 10;
        }

        if (meetsWidth || meetsHeight)
        {
            return 5;
        }

        return 0;
    }

    private static int AspectScore(NormalizedCoverResult candidate)
    {
        if (!candidate.Width.HasValue || !candidate.Height.HasValue)
        {
            return 0;
        }

        var width = candidate.Width.Value;
        var height = candidate.Height.Value;
        if (width <= 0 || height <= 0)
        {
            return 0;
        }

        var max = Math.Max(width, height);
        var diff = Math.Abs(width - height);
        if (diff * 100 <= max * 5)
        {
            return 5;
        }

        if (diff * 100 <= max * 15)
        {
            return 2;
        }

        return 0;
    }

    private static int SourceScore(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return 0;
        }

        if (source.Contains("musichoarderz", StringComparison.OrdinalIgnoreCase)
            || string.Equals(source, "cov", StringComparison.OrdinalIgnoreCase))
        {
            return 5;
        }

        if (source.Contains("spotify", StringComparison.OrdinalIgnoreCase))
        {
            return 3;
        }

        if (source.Contains("youtube", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        return 0;
    }

    private static string TitleOf(NormalizedCoverResult candidate)
    {
        return candidate.Title ?? string.Empty;
    }

    private static string ArtistOf(NormalizedCoverResult candidate)
    {
        return candidate.Artist ?? string.Empty;
    }

    /// <summary>
    /// Normalizes text for deterministic comparison: lowercase, diacritics removed,
    /// edition keywords dropped, punctuation folded to spaces.
    /// </summary>
    /// <param name="value">Raw value.</param>
    /// <returns>Normalized value.</returns>
    internal static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var lowered = value.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(lowered.Length);
        foreach (var ch in lowered)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(ch))
            {
                _ = builder.Append(ch);
            }
            else
            {
                _ = builder.Append(' ');
            }
        }

        var tokens = builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var kept = new List<string>(tokens.Length);
        foreach (var token in tokens)
        {
            var isEdition = false;
            foreach (var keyword in EditionKeywords)
            {
                if (string.Equals(token, keyword, StringComparison.Ordinal))
                {
                    isEdition = true;
                    break;
                }
            }

            if (!isEdition)
            {
                kept.Add(token);
            }
        }

        return string.Join(" ", kept);
    }
}
