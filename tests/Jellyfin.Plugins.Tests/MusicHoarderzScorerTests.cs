using Jellyfin.Plugin.MusicHoarderzProvider.Models;
using Jellyfin.Plugin.MusicHoarderzProvider.Services;
using Xunit;

namespace Jellyfin.Plugins.Tests;

public class MusicHoarderzScorerTests
{
    private readonly CoverMatchScorer _scorer = new();

    [Fact]
    public void Score_ExactMatch_ScoresHighWithReason()
    {
        var query = new CoverSearchQuery("Test Album", "Test Artist", 1999, "DE");
        var candidate = Result("https://covers.example.com/a.jpg", "Test Album", "Test Artist", 1999, 1200, 1200, "musichoarderz");

        var scored = _scorer.Score(query, new[] { candidate }, 1000, 1000);

        Assert.Single(scored);
        Assert.Equal(95, scored[0].Score);
        Assert.Contains("title+40", scored[0].ScoreReason);
        Assert.Contains("artist+25", scored[0].ScoreReason);
        Assert.Contains("year+10", scored[0].ScoreReason);
    }

    [Fact]
    public void Score_DeterministicOrder_TieBreakByUrl()
    {
        var query = new CoverSearchQuery("Album", "Artist", null, "DE");
        var first = Result("https://covers.example.com/b.jpg", "Album", "Artist", null, 1200, 1200, "cov");
        var second = Result("https://covers.example.com/a.jpg", "Album", "Artist", null, 1200, 1200, "cov");

        var run1 = _scorer.Score(query, new[] { first, second }, 1000, 1000);
        var run2 = _scorer.Score(query, new[] { second, first }, 1000, 1000);

        Assert.Equal(run1[0].Url, run2[0].Url);
        Assert.Equal(run1[1].Url, run2[1].Url);
        Assert.Equal("https://covers.example.com/a.jpg", run1[0].Url);
        Assert.Equal(run1[0].Score, run1[1].Score);
    }

    [Fact]
    public void Score_DiacriticsAndPunctuation_MatchExactly()
    {
        var query = new CoverSearchQuery("Motörhead", "AC/DC", null, "DE");
        var candidate = Result("https://covers.example.com/a.jpg", "Motorhead", "AC DC", null, 1200, 1200, "cov");

        var scored = _scorer.Score(query, new[] { candidate }, 1000, 1000);

        Assert.Contains("title+40", scored[0].ScoreReason);
        Assert.Contains("artist+25", scored[0].ScoreReason);
    }

    [Fact]
    public void Score_EditionSuffixes_AreIgnored()
    {
        var query = new CoverSearchQuery("Ace of Spades (Remastered)", "Artist", null, "DE");
        var candidate = Result("https://covers.example.com/a.jpg", "Ace of Spades", "Artist", null, 1200, 1200, "cov");

        var scored = _scorer.Score(query, new[] { candidate }, 1000, 1000);

        Assert.Contains("title+40", scored[0].ScoreReason);
    }

    [Fact]
    public void Score_DeluxeEdition_AreIgnored()
    {
        var query = new CoverSearchQuery("Album", "Artist", null, "DE");
        var candidate = Result("https://covers.example.com/a.jpg", "Album Deluxe Edition", "Artist", null, 1200, 1200, "cov");

        var scored = _scorer.Score(query, new[] { candidate }, 1000, 1000);

        Assert.Contains("title+40", scored[0].ScoreReason);
    }

    [Fact]
    public void Score_PartialTitle_ScoresLessThanExact()
    {
        var query = new CoverSearchQuery("Abbey Road", "Artist", null, "DE");
        var exact = Result("https://covers.example.com/a.jpg", "Abbey Road", "Artist", null, 1200, 1200, "cov");
        var partial = Result("https://covers.example.com/b.jpg", "Abbey", "Artist", null, 1200, 1200, "cov");

        var scored = _scorer.Score(query, new[] { partial, exact }, 1000, 1000);

        Assert.Equal(exact.Url, scored[0].Url);
        Assert.True(scored[0].Score > scored[1].Score);
    }

    [Fact]
    public void Score_YearOffByOne_ScoresPartial()
    {
        var query = new CoverSearchQuery("Album", "Artist", 2000, "DE");
        var candidate = Result("https://covers.example.com/a.jpg", "Album", "Artist", 2001, 1200, 1200, "cov");

        var scored = _scorer.Score(query, new[] { candidate }, 1000, 1000);

        Assert.Contains("year+4", scored[0].ScoreReason);
    }

    [Fact]
    public void Score_BelowMinimumResolution_ScoresZeroResolution()
    {
        var query = new CoverSearchQuery("Album", "Artist", null, "DE");
        var candidate = Result("https://covers.example.com/a.jpg", "Album", "Artist", null, 300, 300, "cov");

        var scored = _scorer.Score(query, new[] { candidate }, 1000, 1000);

        Assert.Contains("resolution+0", scored[0].ScoreReason);
        Assert.Contains("aspect+5", scored[0].ScoreReason);
    }

    [Fact]
    public void Score_NonSquare_ScoresNoAspectBonus()
    {
        var query = new CoverSearchQuery("Album", "Artist", null, "DE");
        var candidate = Result("https://covers.example.com/a.jpg", "Album", "Artist", null, 1200, 800, "cov");

        var scored = _scorer.Score(query, new[] { candidate }, 1000, 1000);

        Assert.Contains("aspect+0", scored[0].ScoreReason);
    }

    [Fact]
    public void Score_SourcePriority_PrefersMusicHoarderz()
    {
        var query = new CoverSearchQuery("Album", "Artist", null, "DE");
        var other = Result("https://covers.example.com/a.jpg", "Album", "Artist", null, 1200, 1200, "other");
        var own = Result("https://covers.example.com/b.jpg", "Album", "Artist", null, 1200, 1200, "musichoarderz");

        var scored = _scorer.Score(query, new[] { other, own }, 1000, 1000);

        Assert.Equal(own.Url, scored[0].Url);
        Assert.Equal(scored[1].Score + 5, scored[0].Score);
    }

    [Fact]
    public void Score_DoesNotMutateInput()
    {
        var query = new CoverSearchQuery("B Album", "Artist", null, "DE");
        var first = Result("https://covers.example.com/b.jpg", "B Album", "Artist", null, 1200, 1200, "cov");
        var second = Result("https://covers.example.com/a.jpg", "A Album", "Artist", null, 1200, 1200, "cov");
        var input = new[] { first, second };

        var scored = _scorer.Score(query, input, 1000, 1000);

        Assert.Same(first, input[0]);
        Assert.Same(second, input[1]);
        Assert.Equal(0, first.Score);
        Assert.Equal(first.Url, scored[0].Url);
    }

    private static NormalizedCoverResult Result(
        string url,
        string title,
        string artist,
        int? year,
        int? width,
        int? height,
        string source)
    {
        return new NormalizedCoverResult(url, width, height, source, "image/jpeg", 0, "unscored", title, artist, year);
    }
}
