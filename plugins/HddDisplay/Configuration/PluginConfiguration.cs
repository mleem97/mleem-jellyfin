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
        DashboardRefreshSeconds = 15;
        StorageScanCacheMinutes = 15;
        SystemScanCacheMinutes = 30;
        StorageScanTimeoutSeconds = 120;
        SystemScanTimeoutSeconds = 60;
        GpuCommandTimeoutMilliseconds = 2500;
        GpuCacheSeconds = 5;
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

    /// <summary>
    /// Gets or sets the system-path scan cache lifetime in minutes.
    /// </summary>
    public int SystemScanCacheMinutes { get; set; }

    /// <summary>
    /// Gets or sets the maximum duration of one media scan in seconds.
    /// </summary>
    public int StorageScanTimeoutSeconds { get; set; }

    /// <summary>
    /// Gets or sets the maximum duration of one system-path scan in seconds.
    /// </summary>
    public int SystemScanTimeoutSeconds { get; set; }

    /// <summary>
    /// Gets or sets the hard timeout for one nvidia-smi command.
    /// </summary>
    public int GpuCommandTimeoutMilliseconds { get; set; }

    /// <summary>
    /// Gets or sets the independent GPU snapshot cache lifetime in seconds.
    /// </summary>
    public int GpuCacheSeconds { get; set; }
}
