using System;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.MusicHoarderzProvider.Controllers;

/// <summary>
/// MusicHoarderz provider endpoints.
/// </summary>
[ApiController]
[Route("Plugins/MusicHoarderzProvider")]
public class ProviderController : ControllerBase
{
    /// <summary>
    /// Gets provider status without exposing secrets.
    /// </summary>
    /// <returns>Provider status.</returns>
    [HttpGet("Status")]
    public ActionResult<ProviderStatus> GetStatus()
    {
        var configuration = Plugin.Instance?.Configuration;
        return Ok(new ProviderStatus
        {
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            Enabled = configuration?.Enabled ?? false,
            MusicHoarderzEnabled = configuration?.MusicHoarderz.Enabled ?? false,
            SpotifyConfigured = !string.IsNullOrWhiteSpace(configuration?.Spotify.ClientId)
                && !string.IsNullOrWhiteSpace(configuration?.Spotify.ClientSecretEncrypted),
            YouTubeConfigured = !string.IsNullOrWhiteSpace(configuration?.YouTube.ApiKeyEncrypted),
            WriteMode = configuration?.WriteMode ?? "JellyfinOnly"
        });
    }
}

/// <summary>
/// Provider status response.
/// </summary>
public class ProviderStatus
{
    /// <summary>
    /// Gets or sets generation timestamp.
    /// </summary>
    public DateTimeOffset GeneratedAtUtc { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the plugin is enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether MusicHoarderz/COV is enabled.
    /// </summary>
    public bool MusicHoarderzEnabled { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether Spotify credentials are configured.
    /// </summary>
    public bool SpotifyConfigured { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether YouTube credentials are configured.
    /// </summary>
    public bool YouTubeConfigured { get; set; }

    /// <summary>
    /// Gets or sets the active write mode.
    /// </summary>
    public string WriteMode { get; set; } = string.Empty;
}
