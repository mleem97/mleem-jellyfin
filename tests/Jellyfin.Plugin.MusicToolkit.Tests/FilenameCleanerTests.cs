using Jellyfin.Plugin.MusicToolkit.Services;
using Xunit;

namespace Jellyfin.Plugin.MusicToolkit.Tests;

/// <summary>
/// Tests for filename cleaning and tag parsing.
/// </summary>
public sealed class FilenameCleanerTests
{
    [Theory]
    [InlineData("1155fe1424d52a22__372499700c573fcb__01_synthsoldier_-_sensory_overload_(extended_mix).flac", 1, "Synthsoldier", "Sensory Overload", "Extended Mix")]
    [InlineData("abcdef12__02_daft_punk_-_one_more_time.flac", 2, "Daft Punk", "One More Time", null)]
    [InlineData("plain_artist_-_plain_title.mp3", null, "Plain Artist", "Plain Title", null)]
    public void Parse_StripsHashesAndExtractsTags(string fileName, int? track, string artist, string title, string? mix)
    {
        var parsed = FilenameCleaner.Parse(fileName);

        Assert.Equal(track, parsed.Track);
        Assert.Equal(artist, parsed.Artist);
        Assert.Equal(title, parsed.Title);
        Assert.Equal(mix, parsed.Mix);
    }

    [Fact]
    public void StripHashPrefixes_RemovesSingleAndDoublePrefixes()
    {
        Assert.Equal("song.flac", FilenameCleaner.StripHashPrefixes("abcdef12__song.flac").Replace(".flac", string.Empty) + ".flac");
        Assert.Equal("track", FilenameCleaner.StripHashPrefixes("1155fe1424d52a22__372499700c573fcb__track"));
    }

    [Fact]
    public void Parse_SanitizesInvalidChars()
    {
        var parsed = FilenameCleaner.Parse("01_artist_-_a:b*c?d.flac");

        Assert.DoesNotContain(":", parsed.Title);
        Assert.DoesNotContain("*", parsed.Title);
        Assert.DoesNotContain("?", parsed.Title);
    }

    [Fact]
    public void BuildRelativePath_AppliesPattern()
    {
        var rel = FilenameCleaner.BuildRelativePath("{Artist}/{Album}/{TrackNumber:02d} - {Title}", "Synthsoldier", "Overload", 1, "Sensory Overload", ".flac");

        Assert.Equal("Synthsoldier/Overload/01 - Sensory Overload.flac", rel);
    }
}
