using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.MediaTools.Configuration;

/// <summary>
/// Configuration for the MediaTools plugin.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets a value indicating whether MediaTools is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the custom FFmpeg path override, if any.
    /// </summary>
    public string FFmpegPathOverride { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the custom FFprobe path override, if any.
    /// </summary>
    public string FFprobePathOverride { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the quarantine directory name for duplicates.
    /// </summary>
    public string QuarantineFolderName { get; set; } = "_duplicates";

    /// <summary>
    /// Gets or sets the movie rename pattern.
    /// </summary>
    public string MovieRenamePattern { get; set; } = "{Title} ({Year})/{Title} ({Year}) [{Resolution} {VideoCodec}].{Ext}";

    /// <summary>
    /// Gets or sets the series episode rename pattern.
    /// </summary>
    public string EpisodeRenamePattern { get; set; } = "{SeriesTitle}/Season {SeasonNumber:02d}/{SeriesTitle} - S{SeasonNumber:02d}E{EpisodeNumber:02d} - {Title}.{Ext}";

    /// <summary>
    /// Gets or sets the audio track rename pattern.
    /// </summary>
    public string AudioRenamePattern { get; set; } = "{Artist}/{Album}/{TrackNumber:02d} - {Title}.{Ext}";

    /// <summary>
    /// Gets or sets a value indicating whether subtitle files should be automatically muxed into converted MKV containers.
    /// </summary>
    public bool AutoMuxSubtitles { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether original files should be quarantined instead of permanently deleted.
    /// </summary>
    public bool QuarantineOnDeduplicate { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum concurrent conversion/merging jobs.
    /// </summary>
    public int MaxConcurrentJobs { get; set; } = 2;
}
