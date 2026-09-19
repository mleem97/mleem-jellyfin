using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MediaTools.Configuration;
using Jellyfin.Plugin.MediaTools.Controllers.Models;
using Jellyfin.Plugin.MediaTools.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MediaTools.Controllers;

/// <summary>
/// REST API controller for MediaTools operations.
/// </summary>
[ApiController]
[Route("Plugins/MediaTools")]
public class MediaToolsController : MediaToolsAdminControllerBase
{
    private readonly FFmpegResolverService _ffmpegResolver;
    private readonly MediaRenamerService _renamerService;
    private readonly SplitMovieMergerService _splitMergerService;
    private readonly ContainerConversionService _containerConversionService;
    private readonly StreamHashDeduplicatorService _deduplicatorService;
    private readonly BackgroundJobQueueService _jobQueue;
    private readonly ILogger<MediaToolsController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="MediaToolsController"/> class.
    /// </summary>
    public MediaToolsController(
        FFmpegResolverService ffmpegResolver,
        MediaRenamerService renamerService,
        SplitMovieMergerService splitMergerService,
        ContainerConversionService containerConversionService,
        StreamHashDeduplicatorService deduplicatorService,
        BackgroundJobQueueService jobQueue,
        ILogger<MediaToolsController> logger)
    {
        _ffmpegResolver = ffmpegResolver;
        _renamerService = renamerService;
        _splitMergerService = splitMergerService;
        _containerConversionService = containerConversionService;
        _deduplicatorService = deduplicatorService;
        _jobQueue = jobQueue;
        _logger = logger;
    }

    /// <summary>
    /// Gets overall status of MediaTools environment and workers.
    /// </summary>
    [HttpGet("Status")]
    public ActionResult<MediaToolsStatusDto> GetStatus()
    {
        var ffmpeg = _ffmpegResolver.ResolveFFmpegPath();
        var ffprobe = _ffmpegResolver.ResolveFFprobePath();
        var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();

        return Ok(new MediaToolsStatusDto
        {
            Enabled = config.Enabled,
            FFmpegPath = ffmpeg,
            FFmpegAvailable = !string.IsNullOrEmpty(ffmpeg),
            FFprobePath = ffprobe,
            FFprobeAvailable = !string.IsNullOrEmpty(ffprobe),
            ActiveJobsCount = _jobQueue.GetActiveJobsCount(),
            CompletedJobsCount = _jobQueue.GetCompletedJobsCount(),
            QuarantineFolder = config.QuarantineFolderName
        });
    }

    /// <summary>
    /// Gets the current plugin configuration.
    /// </summary>
    [HttpGet("Configuration")]
    public ActionResult<PluginConfiguration> GetConfiguration()
    {
        return Ok(Plugin.Instance?.Configuration ?? new PluginConfiguration());
    }

    /// <summary>
    /// Updates the plugin configuration.
    /// </summary>
    [HttpPost("Configuration")]
    public ActionResult UpdateConfiguration([FromBody] PluginConfiguration config)
    {
        if (config == null)
        {
            return BadRequest("Configuration cannot be null.");
        }

        Plugin.Instance?.UpdateConfiguration(config);
        return Ok(config);
    }

    /// <summary>
    /// Previews proposed rename changes across media libraries.
    /// </summary>
    [HttpGet("Renamer/Preview")]
    public ActionResult<List<RenamePreviewDto>> PreviewRenames(
        [FromQuery] string? itemTypes = null,
        [FromQuery] int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var types = string.IsNullOrWhiteSpace(itemTypes)
            ? Array.Empty<string>()
            : itemTypes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var results = _renamerService.GeneratePreviews(types, limit, cancellationToken);
        return Ok(results);
    }

    /// <summary>
    /// Executes safe database and disk renaming for selected items without foreign-key crashes.
    /// </summary>
    [HttpPost("Renamer/Execute")]
    public async Task<ActionResult<ExecuteRenameResultDto>> ExecuteRenames(
        [FromBody] ExecuteRenameRequest request,
        CancellationToken cancellationToken)
    {
        if (request?.ItemIds == null || request.ItemIds.Count == 0)
        {
            return BadRequest("No items specified for rename.");
        }

        var result = await _renamerService.ExecuteRenamesAsync(request.ItemIds, cancellationToken).ConfigureAwait(false);
        return Ok(result);
    }

    /// <summary>
    /// Scans library for multi-part movies (e.g. CD1/CD2, Part 1/Part 2).
    /// </summary>
    [HttpGet("SplitMovies/Scan")]
    public ActionResult<List<SplitMovieGroupDto>> ScanSplitMovies(CancellationToken cancellationToken)
    {
        var groups = _splitMergerService.ScanSplitMovies(cancellationToken);
        return Ok(groups);
    }

    /// <summary>
    /// Enqueues merging of a multi-part movie group.
    /// </summary>
    [HttpPost("SplitMovies/Merge")]
    public ActionResult MergeSplitMovie(
        [FromBody] MergeSplitMovieRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request?.GroupKey))
        {
            return BadRequest("GroupKey must be provided.");
        }

        var groups = _splitMergerService.ScanSplitMovies(cancellationToken);
        var targetGroup = groups.FirstOrDefault(g => string.Equals(g.GroupKey, request.GroupKey, StringComparison.OrdinalIgnoreCase));
        if (targetGroup == null)
        {
            return NotFound($"Split movie group '{request.GroupKey}' not found.");
        }

        var jobId = _jobQueue.EnqueueJob(
            "SplitMerge",
            $"Merge {targetGroup.Title} ({targetGroup.Parts.Count} parts)",
            async (job, ct) =>
            {
                var success = await _splitMergerService.MergeSplitGroupAsync(
                    targetGroup,
                    request.ForceReencode,
                    (pct, speed) =>
                    {
                        job.PercentComplete = Math.Round(pct, 1);
                        job.Speed = speed;
                    },
                    ct).ConfigureAwait(false);

                if (!success)
                {
                    throw new InvalidOperationException("Failed to merge split movie parts.");
                }
            });

        return Ok(new { JobId = jobId, Message = $"Merge job enqueued for '{targetGroup.Title}'." });
    }

    /// <summary>
    /// Scans library for non-MKV video files eligible for conversion.
    /// </summary>
    [HttpGet("Containers/Scan")]
    public ActionResult<List<ConversionCandidateDto>> ScanContainers(CancellationToken cancellationToken)
    {
        var candidates = _containerConversionService.ScanNonMkvFiles(cancellationToken);
        return Ok(candidates);
    }

    /// <summary>
    /// Enqueues batch MKV container conversion with subtitle muxing.
    /// </summary>
    [HttpPost("Containers/Convert")]
    public ActionResult ConvertContainers([FromBody] StartConversionRequest request)
    {
        if (request?.ItemIds == null || request.ItemIds.Count == 0)
        {
            return BadRequest("No items selected for conversion.");
        }

        var jobIds = new List<string>();
        foreach (var itemId in request.ItemIds)
        {
            var capturedId = itemId;
            var jobId = _jobQueue.EnqueueJob(
                "ConvertMkv",
                $"Convert item {capturedId} to MKV",
                async (job, ct) =>
                {
                    var success = await _containerConversionService.ConvertToMkvAsync(
                        capturedId,
                        request.RemuxOnly,
                        request.IncludeExternalSubtitles,
                        (pct, speed) =>
                        {
                            job.PercentComplete = Math.Round(pct, 1);
                            job.Speed = speed;
                        },
                        ct).ConfigureAwait(false);

                    if (!success)
                    {
                        throw new InvalidOperationException($"Conversion failed for item {capturedId}.");
                    }
                });

            jobIds.Add(jobId);
        }

        return Ok(new { JobIds = jobIds, Message = $"Enqueued {jobIds.Count} conversion job(s)." });
    }

    /// <summary>
    /// Scans library for duplicate files using deep audio/video stream MD5 hashing.
    /// </summary>
    [HttpGet("Deduplicator/Scan")]
    public async Task<ActionResult<List<DuplicateGroupDto>>> ScanDuplicates(
        [FromQuery] string? mediaType = null,
        [FromQuery] long minSizeBytes = 1048576,
        [FromQuery] int limit = 500,
        CancellationToken cancellationToken = default)
    {
        var duplicates = await _deduplicatorService.ScanDuplicatesAsync(mediaType ?? "Audio", cancellationToken)
            .ConfigureAwait(false);
        return Ok(duplicates);
    }

    /// <summary>
    /// Safely quarantines selected duplicate files.
    /// </summary>
    [HttpPost("Deduplicator/Quarantine")]
    public ActionResult QuarantineDuplicates(
        [FromBody] QuarantineDuplicatesRequest request,
        CancellationToken cancellationToken)
    {
        if (request?.FilePathsToQuarantine == null || request.FilePathsToQuarantine.Count == 0)
        {
            return BadRequest("No files provided to quarantine.");
        }

        var count = _deduplicatorService.QuarantineDuplicates(request.FilePathsToQuarantine, cancellationToken);
        var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        return Ok(new { QuarantinedCount = count, QuarantineFolder = config.QuarantineFolderName });
    }

    /// <summary>
    /// Gets all background jobs and real-time statuses.
    /// </summary>
    [HttpGet("Jobs")]
    public ActionResult<IReadOnlyList<JobProgressDto>> GetJobs()
    {
        return Ok(_jobQueue.GetAllJobs());
    }

    /// <summary>
    /// Cancels a running or queued background job.
    /// </summary>
    [HttpPost("Jobs/{jobId}/Cancel")]
    public ActionResult CancelJob([FromRoute] string jobId)
    {
        var cancelled = _jobQueue.CancelJob(jobId);
        return Ok(new { Success = cancelled, JobId = jobId });
    }
}
