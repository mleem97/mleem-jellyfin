using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.MusicDashboard.Configuration;

/// <summary>
/// Configuration values for Music Dashboard.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets inclusion of non-music libraries in the overview.
    /// </summary>
    public bool IncludeNonMusicLibraries { get; set; }
}
