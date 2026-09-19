using System.Collections.ObjectModel;

namespace Jellyfin.Plugin.MusicToolkit.Api.Models;

/// <summary>
/// Single rename preview row for DryRun.
/// </summary>
public sealed class RenamePreviewDto
{
    /// <summary>
    /// Gets or sets the current absolute path.
    /// </summary>
    public string OldPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the planned absolute path.
    /// </summary>
    public string NewPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the status (Rename, Skip, Conflict).
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the parse confidence 0-100.
    /// </summary>
    public int Confidence { get; set; }

    /// <summary>
    /// Gets or sets the detected artist.
    /// </summary>
    public string Artist { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the detected title.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the detected track number.
    /// </summary>
    public int? TrackNumber { get; set; }
}
