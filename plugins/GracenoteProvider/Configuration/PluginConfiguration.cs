using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.GracenoteProvider.Configuration;

/// <summary>
/// Plugin configuration for the Gracenote metadata provider.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets the Gracenote client id.
    /// </summary>
    public string GracenoteClientId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Gracenote user id (auto-registered if empty).
    /// </summary>
    public string GracenoteUserId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether Gracenote lookup is enabled.
    /// </summary>
    public bool EnableGracenote { get; set; } = true;
}
