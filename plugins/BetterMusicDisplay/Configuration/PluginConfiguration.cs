using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.BetterMusicDisplay.Configuration;

/// <summary>
/// Global Better MusicDisplay configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets a value indicating whether the plugin is globally enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether all users must use the enhanced view.
    /// </summary>
    public bool ForceForAllUsers { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether users may customize their layout.
    /// </summary>
    public bool AllowUserCustomization { get; set; } = true;

    /// <summary>
    /// Gets or sets the default landing page.
    /// </summary>
    public string DefaultLandingPage { get; set; } = "Suggestions";

    /// <summary>
    /// Gets or sets the default album layout.
    /// </summary>
    public string DefaultAlbumLayout { get; set; } = "Grid";

    /// <summary>
    /// Gets or sets the default batch size for music queries.
    /// </summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>
    /// Gets or sets the search debounce interval in milliseconds.
    /// </summary>
    public int SearchDebounceMs { get; set; } = 300;

    /// <summary>
    /// Gets or sets a value indicating whether server-side caching is enabled.
    /// </summary>
    public bool EnableServerCache { get; set; } = true;

    /// <summary>
    /// Gets or sets the cache lifetime in hours.
    /// </summary>
    public int CacheTtlHours { get; set; } = 12;

    /// <summary>
    /// Gets or sets a value indicating whether the default Jellyfin view may be used as fallback.
    /// </summary>
    public bool EnableFallbackToDefaultJellyfin { get; set; } = true;
}
