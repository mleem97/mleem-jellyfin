using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Template.Configuration;

/// <summary>
/// Plugin configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
    /// </summary>
    public PluginConfiguration()
    {
        IncludeSystemDrive = true;
    }

    /// <summary>
    /// Gets or sets a value indicating whether system/root drive should be shown.
    /// </summary>
    public bool IncludeSystemDrive { get; set; }
}
