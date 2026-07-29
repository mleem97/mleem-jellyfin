using Jellyfin.Plugin.HddDisplay.Services;
using Xunit;

namespace Jellyfin.Plugins.Tests;

public sealed class HddMountResolverTests
{
    [Fact]
    public void DeepestContainerMountWins()
    {
        var resolution = MountResolver.ResolveFromMountInfo(
            "/media/tv/Series/Episode.mkv",
            new[]
            {
                "36 25 0:32 / / rw,relatime - overlay overlay rw",
                "42 36 8:1 /media /media rw,relatime - ext4 /dev/sda1 rw",
                "43 36 8:2 /tv /media/tv rw,relatime - ext4 /dev/sdb1 rw"
            });

        Assert.True(resolution.IsResolved);
        Assert.Equal("/media/tv", resolution.MountPath);
        Assert.Equal("/dev/sdb1", resolution.Source);
        Assert.Equal("ext4", resolution.FileSystemType);
        Assert.Equal("mountinfo", resolution.ResolutionProvider);
    }

    [Fact]
    public void MountInfoEscapesAreDecoded()
    {
        var resolution = MountResolver.ResolveFromMountInfo(
            "/media/My Music/Album/track.flac",
            new[]
            {
                "36 25 0:32 / / rw,relatime - overlay overlay rw",
                "50 36 8:3 /music /media/My\\040Music rw,relatime - ext4 /dev/sdc1 rw"
            });

        Assert.Equal("/media/My Music", resolution.MountPath);
        Assert.Equal("/dev/sdc1", resolution.Source);
    }

    [Fact]
    public void RootMountIsPreservedAndMatchesPaths()
    {
        var resolution = MountResolver.ResolveFromMountInfo(
            "/var/lib/jellyfin/data",
            new[]
            {
                "36 25 0:32 / / rw,relatime - overlay overlay rw"
            });

        Assert.Equal("/", resolution.MountPath);
        Assert.Equal("mountinfo", resolution.ResolutionProvider);
    }

    [Fact]
    public void MissingMountInfoFallsBackToRoot()
    {
        var resolution = MountResolver.ResolveFromMountInfo(
            "/unmatched/library",
            Array.Empty<string>());

        Assert.True(resolution.IsResolved);
        Assert.Equal("/", resolution.MountPath);
        Assert.Equal("fallback", resolution.ResolutionProvider);
    }
}
