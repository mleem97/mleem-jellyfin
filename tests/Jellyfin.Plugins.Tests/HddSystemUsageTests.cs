using Jellyfin.Plugin.HddDisplay.Services;
using Xunit;
using HddPlugin = Jellyfin.Plugin.HddDisplay.Plugin;

namespace Jellyfin.Plugins.Tests;

public sealed class HddSystemUsageTests
{
    [Fact]
    public void NestedSystemPathsAreCountedExclusively()
    {
        using var directory = new TemporaryDirectory();
        var nestedPath = Path.Combine(directory.Path, "images");
        Directory.CreateDirectory(nestedPath);
        File.WriteAllBytes(Path.Combine(directory.Path, "cache.bin"), new byte[10]);
        File.WriteAllBytes(Path.Combine(nestedPath, "cover.bin"), new byte[20]);

        var result = SystemUsageAggregator.Calculate(
            new[]
            {
                new SystemUsageScanInput
                {
                    Category = "image-cache",
                    Path = nestedPath,
                    MountPath = "/"
                },
                new SystemUsageScanInput
                {
                    Category = "cache",
                    Path = directory.Path,
                    MountPath = "/"
                }
            },
            cacheMinutes: 0,
            forceRefresh: true);

        var imageCache = Assert.Single(result.Entries, entry => entry.Category == "image-cache");
        var cache = Assert.Single(result.Entries, entry => entry.Category == "cache");
        Assert.Equal(20, imageCache.UsedBytes);
        Assert.Equal(10, cache.UsedBytes);
        Assert.Equal(30, result.Entries.Sum(entry => entry.UsedBytes));
    }

    [Fact]
    public void SystemUsageExtensionIsEmbedded()
    {
        var resources = typeof(HddPlugin).Assembly.GetManifestResourceNames();

        Assert.Contains(
            "Jellyfin.Plugin.HddDisplay.Web.system-usage-extension.js",
            resources);
    }
}
