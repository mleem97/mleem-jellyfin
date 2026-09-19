using System.Collections.ObjectModel;

namespace Jellyfin.Plugin.MusicToolkit.Api.Models;

/// <summary>
/// Execute-rename request and result.
/// </summary>
public sealed class RenameExecutionDto
{
    /// <summary>
    /// Gets the preview rows to apply.
    /// </summary>
    public Collection<RenamePreviewDto> Items { get; } = new();

    /// <summary>
    /// Gets or sets the number of renamed files.
    /// </summary>
    public int Renamed { get; set; }

    /// <summary>
    /// Gets or sets the number of skipped files.
    /// </summary>
    public int Skipped { get; set; }

    /// <summary>
    /// Gets the error messages.
    /// </summary>
    public Collection<string> Errors { get; } = new();
}
