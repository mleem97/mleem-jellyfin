using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.MetadataDashboard.Configuration;

/// <summary>
/// Configuration options for the MetadataDashboard plugin.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
    /// </summary>
    public PluginConfiguration()
    {
        EnableCoverArtArchiveProvider = true;
        MusicBrainzMirror = "https://musicbrainz.org";
        CoverArtArchiveMirror = "https://coverartarchive.org";
        RateLimitDelayMs = 1000;
        EnableAutomaticAudit = true;
    }

    /// <summary>
    /// Gets or sets a value indicating whether CoverArtArchive remote image provider is active.
    /// </summary>
    public bool EnableCoverArtArchiveProvider { get; set; }

    /// <summary>
    /// Gets or sets the MusicBrainz API base URL.
    /// </summary>
    public string MusicBrainzMirror { get; set; }

    /// <summary>
    /// Gets or sets the CoverArtArchive API base URL.
    /// </summary>
    public string CoverArtArchiveMirror { get; set; }

    /// <summary>
    /// Gets or sets the minimum delay between MusicBrainz requests in milliseconds.
    /// </summary>
    public int RateLimitDelayMs { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether automatic library health audits are performed.
    /// </summary>
    public bool EnableAutomaticAudit { get; set; }
}
