using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.SpotifyProvider.Configuration;

/// <summary>
/// Plugin configuration for the Spotify provider.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets the Spotify application client id.
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Spotify application client secret.
    /// </summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether cover lookup is enabled.
    /// </summary>
    public bool EnableImageLookup { get; set; } = true;

    /// <summary>
    /// Gets or sets the Spotify market for searches.
    /// </summary>
    public string Market { get; set; } = "DE";
}
