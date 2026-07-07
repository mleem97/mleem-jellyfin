using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.MusicHoarderzProvider.Configuration;

/// <summary>
/// Configuration for MusicHoarderz Cover and Metadata Provider.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets a value indicating whether the provider plugin is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether manual cover search is allowed.
    /// </summary>
    public bool AllowManualSearch { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether non-admin users may search covers.
    /// </summary>
    public bool AllowNonAdminManualSearch { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether automatic search is enabled.
    /// </summary>
    public bool AutoSearchEnabled { get; set; }

    /// <summary>
    /// Gets or sets the minimum cover width.
    /// </summary>
    public int MinimumWidth { get; set; } = 1000;

    /// <summary>
    /// Gets or sets the minimum cover height.
    /// </summary>
    public int MinimumHeight { get; set; } = 1000;

    /// <summary>
    /// Gets or sets the minimum score required for automatic apply.
    /// </summary>
    public int AutoApplyMinimumScore { get; set; } = 85;

    /// <summary>
    /// Gets or sets the write mode.
    /// </summary>
    public string WriteMode { get; set; } = "JellyfinOnly";

    /// <summary>
    /// Gets or sets MusicHoarderz/COV settings.
    /// </summary>
    public MusicHoarderzSettings MusicHoarderz { get; set; } = new();

    /// <summary>
    /// Gets or sets Spotify settings.
    /// </summary>
    public SpotifySettings Spotify { get; set; } = new();

    /// <summary>
    /// Gets or sets YouTube settings.
    /// </summary>
    public YouTubeSettings YouTube { get; set; } = new();
}

/// <summary>
/// MusicHoarderz/COV settings.
/// </summary>
public class MusicHoarderzSettings
{
    /// <summary>
    /// Gets or sets a value indicating whether COV is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the base URL.
    /// </summary>
    public string BaseUrl { get; set; } = "https://covers.musichoarders.xyz";

    /// <summary>
    /// Gets or sets the preferred country.
    /// </summary>
    public string Country { get; set; } = "DE";
}

/// <summary>
/// Spotify provider settings.
/// </summary>
public class SpotifySettings
{
    /// <summary>
    /// Gets or sets a value indicating whether Spotify is enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets the Spotify client id.
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the encrypted Spotify client secret.
    /// </summary>
    public string ClientSecretEncrypted { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the market.
    /// </summary>
    public string Market { get; set; } = "DE";
}

/// <summary>
/// YouTube provider settings.
/// </summary>
public class YouTubeSettings
{
    /// <summary>
    /// Gets or sets a value indicating whether YouTube is enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets the encrypted API key.
    /// </summary>
    public string ApiKeyEncrypted { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the region code.
    /// </summary>
    public string RegionCode { get; set; } = "DE";
}
