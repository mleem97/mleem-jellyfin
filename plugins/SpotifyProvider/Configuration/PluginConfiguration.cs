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
    /// Gets or sets a value indicating whether music album &amp; artist metadata lookup is enabled.
    /// </summary>
    public bool EnableMusicMetadata { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether audiobook metadata lookup is enabled.
    /// </summary>
    public bool EnableAudiobookMetadata { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether podcast &amp; show metadata lookup is enabled.
    /// </summary>
    public bool EnablePodcastMetadata { get; set; } = true;

    /// <summary>
    /// Gets or sets the Spotify market for searches.
    /// </summary>
    public string Market { get; set; } = "DE";
}
