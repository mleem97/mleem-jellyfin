using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.MusicToolkit.Configuration;

/// <summary>
/// Plugin configuration for Music Toolkit.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets the Gracenote client id.
    /// </summary>
    public string GracenoteClientId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the registered Gracenote user id.
    /// </summary>
    public string GracenoteUserId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether Gracenote lookups are enabled.
    /// </summary>
    public bool EnableGracenote { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether MusicHoarderz lookups are enabled.
    /// </summary>
    public bool EnableMusicHoarders { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether duplicates are quarantined instead of deleted.
    /// </summary>
    public bool QuarantineDuplicates { get; set; } = true;

    /// <summary>
    /// Gets or sets the quarantine folder name for duplicates.
    /// </summary>
    public string QuarantineFolder { get; set; } = "_duplicates";

    /// <summary>
    /// Gets or sets the preferred codec for master selection.
    /// </summary>
    public string PreferredCodec { get; set; } = "flac";

    /// <summary>
    /// Gets or sets the rename pattern.
    /// </summary>
    public string RenamePattern { get; set; } = "{Artist}/{Album}/{TrackNumber:02d} - {Title}";
}
