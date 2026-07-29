using System.Threading;
using Jellyfin.Plugin.HddDisplay.Services;
using Xunit;

namespace Jellyfin.Plugins.Tests;

public sealed class HddHardeningTests
{
    [Fact]
    public void CancelledMediaScanReturnsUncachedPartialResult()
    {
        using var directory = new TemporaryDirectory();
        File.WriteAllBytes(Path.Combine(directory.Path, "media.bin"), new byte[32]);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = MediaUsageAggregator.Calculate(
            new[]
            {
                new MediaUsageScanInput
                {
                    LibraryName = "Music",
                    LibraryType = "music",
                    LibraryPath = directory.Path,
                    MountPath = "/"
                }
            },
            cacheMinutes: 15,
            forceRefresh: true,
            cancellationToken: cancellation.Token,
            timeoutSeconds: 30);

        Assert.False(result.Completed);
        Assert.False(result.CacheHit);
        Assert.Contains(result.Diagnostics, item =>
            item.Contains("cancelled", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CancelledSystemScanReturnsUncachedPartialResult()
    {
        using var directory = new TemporaryDirectory();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = SystemUsageAggregator.Calculate(
            new[]
            {
                new SystemUsageScanInput
                {
                    Category = "cache",
                    Path = directory.Path,
                    MountPath = "/"
                }
            },
            cacheMinutes: 30,
            forceRefresh: true,
            cancellationToken: cancellation.Token,
            timeoutSeconds: 30);

        Assert.False(result.Completed);
        Assert.Contains(result.Diagnostics, item =>
            item.Contains("cancelled", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GpuCacheIsIndependentAndReturnsDetachedSnapshots()
    {
        GpuUsageCache.Clear();
        var provider = new CountingGpuProvider();

        var first = GpuUsageCache.GetSnapshot(provider, cacheSeconds: 30, forceRefresh: false);
        first.Devices[0].Name = "mutated";
        var second = GpuUsageCache.GetSnapshot(provider, cacheSeconds: 30, forceRefresh: false);

        Assert.Equal(1, provider.CallCount);
        Assert.False(first.CacheHit);
        Assert.True(second.CacheHit);
        Assert.Equal("Test GPU", second.Devices[0].Name);
    }

    private sealed class CountingGpuProvider : IGpuUsageProvider
    {
        public int CallCount { get; private set; }

        public GpuUsageSnapshot GetSnapshot()
        {
            CallCount++;
            return new GpuUsageSnapshot
            {
                GeneratedAtUtc = DateTimeOffset.UtcNow,
                IsAvailable = true,
                Provider = "test",
                Devices = new[]
                {
                    new GpuDeviceUsage
                    {
                        Name = "Test GPU",
                        MemoryTotalMiB = 1024
                    }
                }
            };
        }
    }
}
