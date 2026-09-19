using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.GracenoteProvider.Configuration;

/// <summary>
/// Plugin configuration for the Gracenote metadata provider.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets the Gracenote API version to use ("v2" or "v3").
    /// </summary>
    public string ApiVersion { get; set; } = "v2";

    /// <summary>
    /// Gets or sets the Gracenote client id (for GMD v2 and v3).
    /// </summary>
    public string GracenoteClientId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Gracenote user id (auto-registered for GMD v2 if empty).
    /// </summary>
    public string GracenoteUserId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Gracenote API Key (for GMD v3 REST API).
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether Gracenote lookup is enabled.
    /// </summary>
    public bool EnableGracenote { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether album metadata is enabled.
    /// </summary>
    public bool EnableAlbumMetadata { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether artist metadata is enabled.
    /// </summary>
    public bool EnableArtistMetadata { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether cover art lookup is enabled.
    /// </summary>
    public bool EnableImageLookup { get; set; } = true;

    /// <summary>
    /// Gets or sets preferred image size ("MEDIUM", "LARGE", "XLARGE").
    /// </summary>
    public string PreferredImageSize { get; set; } = "LARGE";
}
