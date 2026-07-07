using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.HddDisplay.Configuration;

/// <summary>
/// Configuration for HDD Display.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
    /// </summary>
    public PluginConfiguration()
    {
        IncludeSystemDrive = false;
        DashboardRefreshSeconds = 5;
        StorageScanCacheMinutes = 15;
    }

    /// <summary>
    /// Gets or sets a value indicating whether the system/root drive should be shown.
    /// </summary>
    public bool IncludeSystemDrive { get; set; }

    /// <summary>
    /// Gets or sets the Admin Dashboard widget refresh interval in seconds.
    /// </summary>
    public int DashboardRefreshSeconds { get; set; }

    /// <summary>
    /// Gets or sets the storage scan cache lifetime in minutes.
    /// </summary>
    public int StorageScanCacheMinutes { get; set; }
}
