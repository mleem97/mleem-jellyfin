using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.MediaTools.Controllers.Models;

/// <summary>
/// Status overview for the MediaTools system.
/// </summary>
public class MediaToolsStatusDto
{
    public bool Enabled { get; set; }

    public string FFmpegPath { get; set; } = string.Empty;

    public bool FFmpegAvailable { get; set; }

    public string FFprobePath { get; set; } = string.Empty;

    public bool FFprobeAvailable { get; set; }

    public int ActiveJobsCount { get; set; }

    public int CompletedJobsCount { get; set; }

    public string QuarantineFolder { get; set; } = string.Empty;
}

/// <summary>
/// Preview item for a suggested file rename operation.
/// </summary>
public class RenamePreviewDto
{
    public Guid ItemId { get; set; }

    public string ItemName { get; set; } = string.Empty;

    public string MediaType { get; set; } = string.Empty;

    public string CurrentPath { get; set; } = string.Empty;

    public string CurrentFilename { get; set; } = string.Empty;

    public string ProposedPath { get; set; } = string.Empty;

    public string ProposedFilename { get; set; } = string.Empty;

    public bool HasChange { get; set; }

    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// Request to execute one or more rename operations.
/// </summary>
public class ExecuteRenameRequest
{
    public List<Guid> ItemIds { get; set; } = new();
}

/// <summary>
/// Result of an executed rename batch.
/// </summary>
public class ExecuteRenameResultDto
{
    public int TotalRequested { get; set; }

    public int Succeeded { get; set; }

    public int Failed { get; set; }

    public List<string> Errors { get; set; } = new();
}

/// <summary>
/// Multi-part split movie group (e.g. CD1, CD2).
/// </summary>
public class SplitMovieGroupDto
{
    public string GroupKey { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public int? Year { get; set; }

    public string TargetOutputFilename { get; set; } = string.Empty;

    public string DirectoryPath { get; set; } = string.Empty;

    public List<SplitPartItemDto> Parts { get; set; } = new();

    public bool CanSmartConcat { get; set; }

    public string CompatibilityReason { get; set; } = string.Empty;
}

/// <summary>
/// Individual part of a split movie.
/// </summary>
public class SplitPartItemDto
{
    public Guid ItemId { get; set; }

    public string Path { get; set; } = string.Empty;

    public string Filename { get; set; } = string.Empty;

    public int PartIndex { get; set; }

    public long FileSizeBytes { get; set; }

    public double DurationSeconds { get; set; }

    public string VideoCodec { get; set; } = string.Empty;

    public string AudioCodec { get; set; } = string.Empty;

    public int Width { get; set; }

    public int Height { get; set; }
}

/// <summary>
/// Request to merge a multi-part movie.
/// </summary>
public class MergeSplitMovieRequest
{
    public string GroupKey { get; set; } = string.Empty;

    public bool ForceReencode { get; set; }
}

/// <summary>
/// Media file candidate for container conversion into Matroska (.mkv).
/// </summary>
public class ConversionCandidateDto
{
    public Guid ItemId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string CurrentPath { get; set; } = string.Empty;

    public string CurrentContainer { get; set; } = string.Empty;

    public long FileSizeBytes { get; set; }

    public string VideoCodec { get; set; } = string.Empty;

    public string AudioCodec { get; set; } = string.Empty;

    public int ExternalSubtitlesCount { get; set; }

    public List<string> SubtitleLanguages { get; set; } = new();
}

/// <summary>
/// Request to convert a container or batch of files to MKV.
/// </summary>
public class StartConversionRequest
{
    public List<Guid> ItemIds { get; set; } = new();

    public bool RemuxOnly { get; set; } = true;

    public bool IncludeExternalSubtitles { get; set; } = true;
}

/// <summary>
/// Group of identical duplicate media files based on content stream hash.
/// </summary>
public class DuplicateGroupDto
{
    public string StreamHash { get; set; } = string.Empty;

    public string MediaTitle { get; set; } = string.Empty;

    public string MediaType { get; set; } = string.Empty;

    public List<DuplicateFileItemDto> Files { get; set; } = new();

    public string MasterPath { get; set; } = string.Empty;
}

/// <summary>
/// File entry in a duplicate group.
/// </summary>
public class DuplicateFileItemDto
{
    public Guid ItemId { get; set; }

    public string Path { get; set; } = string.Empty;

    public long FileSizeBytes { get; set; }

    public string Codec { get; set; } = string.Empty;

    public int Bitrate { get; set; }

    public int QualityScore { get; set; }

    public bool IsMaster { get; set; }
}

/// <summary>
/// Request to quarantine duplicate files.
/// </summary>
public class QuarantineDuplicatesRequest
{
    public List<string> FilePathsToQuarantine { get; set; } = new();
}

/// <summary>
/// Live job status in the background processing queue.
/// </summary>
public class JobProgressDto
{
    public string JobId { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Status { get; set; } = "Queued"; // Queued, Running, Completed, Failed, Cancelled

    public double PercentComplete { get; set; }

    public double Fps { get; set; }

    public string Speed { get; set; } = string.Empty;

    public string CurrentTime { get; set; } = string.Empty;

    public string EstimatedTimeRemaining { get; set; } = string.Empty;

    public string ErrorMessage { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }
}
