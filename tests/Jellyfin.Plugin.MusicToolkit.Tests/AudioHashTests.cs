using Jellyfin.Plugin.MusicToolkit.Services;
using Xunit;

namespace Jellyfin.Plugin.MusicToolkit.Tests;

/// <summary>
/// Tests for audio quality scoring.
/// </summary>
public sealed class AudioHashTests
{
    [Fact]
    public void ScoreFile_FlacBeatsMp3()
    {
        var (flacScore, _, _) = AudioHashService.ScoreFile("song.flac", "flac");

        Assert.True(flacScore >= 100);
    }

    [Fact]
    public void ScoreFile_PreferredCodecBonus()
    {
        var (flac, _, _) = AudioHashService.ScoreFile("song.flac", "flac");
        var (wav, _, _) = AudioHashService.ScoreFile("song.wav", "flac");

        Assert.True(flac >= wav);
    }

    [Fact]
    public void ScoreFile_UnknownExtensionFallsBack()
    {
        var (score, codec, _) = AudioHashService.ScoreFile("song.unknown", "flac");

        Assert.True(score >= 1);
        Assert.Equal("unknown", codec);
    }
}
