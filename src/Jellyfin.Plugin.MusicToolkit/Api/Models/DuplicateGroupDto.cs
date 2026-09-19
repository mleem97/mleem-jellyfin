using System.Collections.ObjectModel;

namespace Jellyfin.Plugin.MusicToolkit.Api.Models;

/// <summary>
/// One audio-duplicate group.
/// </summary>
public sealed class DuplicateGroupDto
{
    /// <summary>
    /// Gets or sets the audio content hash.
    /// </summary>
    public string AudioHash { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the master file kept.
    /// </summary>
    public string MasterPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the master quality score.
    /// </summary>
    public int MasterScore { get; set; }

    /// <summary>
    /// Gets the duplicate paths.
    /// </summary>
    public Collection<string> DuplicatePaths { get; } = new();

    /// <summary>
    /// Gets or sets the codec of the master.
    /// </summary>
    public string Codec { get; set; } = string.Empty;
}
