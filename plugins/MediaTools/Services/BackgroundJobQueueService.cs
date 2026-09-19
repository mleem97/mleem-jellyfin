using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Jellyfin.Plugin.MediaTools.Controllers.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MediaTools.Services;

/// <summary>
/// Non-blocking sequential/concurrent background job worker queue.
/// Tracks live job status, cancellation, and progress updates.
/// </summary>
public sealed class BackgroundJobQueueService : IDisposable
{
    private readonly ILogger<BackgroundJobQueueService> _logger;
    private readonly Channel<JobQueueItem> _channel;
    private readonly ConcurrentDictionary<string, JobProgressDto> _jobs = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _ctsMap = new();
    private readonly CancellationTokenSource _serviceCts = new();
    private readonly Task _processingTask;
    private bool _disposed;

    private sealed record JobQueueItem(
        JobProgressDto Job,
        Func<JobProgressDto, CancellationToken, Task> WorkItem,
        CancellationTokenSource Cts);

    /// <summary>
    /// Initializes a new instance of the <see cref="BackgroundJobQueueService"/> class.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    public BackgroundJobQueueService(ILogger<BackgroundJobQueueService> logger)
    {
        _logger = logger;
        _channel = Channel.CreateUnbounded<JobQueueItem>(new UnboundedChannelOptions
        {
            SingleReader = true
        });
        _processingTask = Task.Run(ProcessQueueAsync);
    }

    /// <summary>
    /// Enqueues a background job.
    /// </summary>
    /// <param name="type">Job type label (e.g. SplitMerge, ConvertMkv, Renamer).</param>
    /// <param name="title">Human-readable description.</param>
    /// <param name="workItem">Action taking the live progress DTO and cancellation token.</param>
    /// <returns>Unique job ID.</returns>
    public string EnqueueJob(
        string type,
        string title,
        Func<JobProgressDto, CancellationToken, Task> workItem)
    {
        ArgumentNullException.ThrowIfNull(workItem);

        var jobId = Guid.NewGuid().ToString("N")[..12];
        var jobDto = new JobProgressDto
        {
            JobId = jobId,
            Type = type,
            Title = title,
            Status = "Queued",
            PercentComplete = 0.0,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var jobCts = new CancellationTokenSource();
        _jobs[jobId] = jobDto;
        _ctsMap[jobId] = jobCts;

        var item = new JobQueueItem(jobDto, workItem, jobCts);
        if (!_channel.Writer.TryWrite(item))
        {
            _logger.LogWarning("Failed to enqueue job {JobId} into channel", jobId);
            jobDto.Status = "Failed";
            jobDto.ErrorMessage = "Queue channel rejected write";
        }

        return jobId;
    }

    /// <summary>
    /// Cancels a running or queued job.
    /// </summary>
    /// <param name="jobId">Job ID.</param>
    /// <returns>True if cancellation requested.</returns>
    public bool CancelJob(string jobId)
    {
        if (_ctsMap.TryGetValue(jobId, out var cts))
        {
            try
            {
                cts.Cancel();
                if (_jobs.TryGetValue(jobId, out var job) && job.Status == "Queued")
                {
                    job.Status = "Cancelled";
                    job.FinishedAt = DateTimeOffset.UtcNow;
                }

                _logger.LogInformation("Cancellation requested for job {JobId}", jobId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to signal cancellation for job {JobId}", jobId);
            }
        }

        return false;
    }

    /// <summary>
    /// Gets all jobs tracked in memory, ordered newest first.
    /// </summary>
    public IReadOnlyList<JobProgressDto> GetAllJobs()
    {
        return _jobs.Values
            .OrderByDescending(j => j.CreatedAt)
            .Take(100)
            .ToList();
    }

    /// <summary>
    /// Gets the count of currently active or queued jobs.
    /// </summary>
    public int GetActiveJobsCount()
    {
        return _jobs.Values.Count(j => j.Status is "Running" or "Queued");
    }

    /// <summary>
    /// Gets the count of completed jobs.
    /// </summary>
    public int GetCompletedJobsCount()
    {
        return _jobs.Values.Count(j => j.Status == "Completed");
    }

    private async Task ProcessQueueAsync()
    {
        var reader = _channel.Reader;

        while (!_serviceCts.Token.IsCancellationRequested)
        {
            JobQueueItem item;
            try
            {
                item = await reader.ReadAsync(_serviceCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (item.Cts.IsCancellationRequested)
            {
                item.Job.Status = "Cancelled";
                item.Job.FinishedAt = DateTimeOffset.UtcNow;
                continue;
            }

            item.Job.Status = "Running";
            _logger.LogInformation("Starting background job {JobId} ({Type}): {Title}", item.Job.JobId, item.Job.Type, item.Job.Title);

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(item.Cts.Token, _serviceCts.Token);
            try
            {
                await item.WorkItem(item.Job, linkedCts.Token).ConfigureAwait(false);

                if (item.Job.Status == "Running")
                {
                    item.Job.Status = "Completed";
                    item.Job.PercentComplete = 100.0;
                }
            }
            catch (OperationCanceledException)
            {
                item.Job.Status = "Cancelled";
                _logger.LogInformation("Job {JobId} was cancelled", item.Job.JobId);
            }
            catch (Exception ex)
            {
                item.Job.Status = "Failed";
                item.Job.ErrorMessage = ex.Message;
                _logger.LogError(ex, "Error occurred during job {JobId}", item.Job.JobId);
            }
            finally
            {
                item.Job.FinishedAt = DateTimeOffset.UtcNow;
                _ctsMap.TryRemove(item.Job.JobId, out _);
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _channel.Writer.TryComplete();
        _serviceCts.Cancel();
        _serviceCts.Dispose();
    }
}
