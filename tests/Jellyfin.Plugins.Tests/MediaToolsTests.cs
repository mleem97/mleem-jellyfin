using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MediaTools.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugins.Tests;

public class MediaToolsTests
{
    [Fact]
    public void CleanStem_StripsReleaseGroupTagsAndFormatting()
    {
        // Scene release tags
        var input = "The.Matrix.1999.1080p.BluRay.x264-SPARKS";
        var cleaned = MediaRenamerService.CleanStem(input);
        Assert.Contains("The Matrix", cleaned, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("1999", cleaned, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SPARKS", cleaned, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("x264", cleaned, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CleanStem_StripsHashPrefixes()
    {
        var input = "a1b2c3d4e5f67890__Track 01 - Intro";
        var cleaned = MediaRenamerService.CleanStem(input);
        Assert.StartsWith("Track 01", cleaned, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("a1b2c3d4e5f67890__", cleaned, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SanitizeFileName_ReplacesIllegalFilesystemCharacters()
    {
        var raw = "Movie: Title / Subtitle * [2024]? <Special>";
        var sanitized = MediaRenamerService.SanitizeFileName(raw);
        Assert.DoesNotContain(":", sanitized);
        Assert.DoesNotContain("/", sanitized);
        Assert.DoesNotContain("*", sanitized);
        Assert.DoesNotContain("?", sanitized);
        Assert.DoesNotContain("<", sanitized);
        Assert.DoesNotContain(">", sanitized);
    }

    [Fact]
    public void SubtitleMuxer_BuildsValidArguments()
    {
        var muxer = new SubtitleMuxerService();
        var subs = new[]
        {
            new SubtitleMuxerService.DiscoveredSubtitle
            {
                FilePath = "movie.en.srt",
                LanguageCode = "eng",
                Title = "English",
                IsForced = false,
                IsDefault = true
            }
        };

        var args = muxer.BuildFFmpegArguments(subs, startIndex: 1);
        Assert.Contains("-i \"movie.en.srt\"", args, StringComparison.Ordinal);
        Assert.Contains("-map 1:s?", args, StringComparison.Ordinal);
        Assert.Contains("-metadata:s:s:0 language=eng", args, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ComputeStreamHash_ProducesDeterministicHash()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"mt_test_{Guid.NewGuid():N}.bin");
        try
        {
            var content = Encoding.UTF8.GetBytes("JELLYFIN_MEDIATOOLS_STREAM_TEST_CONTENT_12345");
            await File.WriteAllBytesAsync(tempFile, content);

            var hash1 = await StreamHashDeduplicatorService.ComputeStreamHashAsync(tempFile, CancellationToken.None);
            var hash2 = await StreamHashDeduplicatorService.ComputeStreamHashAsync(tempFile, CancellationToken.None);

            Assert.False(string.IsNullOrWhiteSpace(hash1));
            Assert.Equal(hash1, hash2);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public async Task BackgroundJobQueue_ProcessesAndCompletesJob()
    {
        using var queue = new BackgroundJobQueueService(NullLogger<BackgroundJobQueueService>.Instance);

        var executed = false;
        var jobId = queue.EnqueueJob("TestJob", "Testing Job Queue", (job, ct) =>
        {
            job.PercentComplete = 50.0;
            job.Speed = "1.5x";
            executed = true;
            return Task.CompletedTask;
        });

        Assert.False(string.IsNullOrWhiteSpace(jobId));

        // Allow queue processor to execute
        var timeout = DateTime.UtcNow.AddSeconds(3);
        while (!executed && DateTime.UtcNow < timeout)
        {
            await Task.Delay(50);
        }

        Assert.True(executed, "Job should have executed within timeout.");

        // Wait brief moment for status update to Completed
        await Task.Delay(100);
        var jobs = queue.GetAllJobs();
        var jobDto = Assert.Single(jobs, j => j.JobId == jobId);
        Assert.Equal("Completed", jobDto.Status);
        Assert.Equal(100.0, jobDto.PercentComplete);
    }
}
