using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.SpotifyProvider.Controllers;

/// <summary>
/// Spotify provider endpoints.
/// </summary>
[ApiController]
[Route("Plugins/SpotifyProvider")]
public class ProviderController : ControllerBase
{
    /// <summary>
    /// Gets provider status without exposing secrets.
    /// </summary>
    /// <returns>Provider status.</returns>
    [HttpGet("Status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<ProviderStatus> GetStatus()
    {
        var configuration = Plugin.Instance?.Configuration;
        return Ok(new ProviderStatus
        {
            Enabled = configuration?.EnableImageLookup ?? false,
            Configured = !string.IsNullOrWhiteSpace(configuration?.ClientId)
                && !string.IsNullOrWhiteSpace(configuration?.ClientSecret),
        });
    }
}

/// <summary>
/// Provider status response.
/// </summary>
public class ProviderStatus
{
    /// <summary>
    /// Gets or sets a value indicating whether the provider is enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether credentials are configured.
    /// </summary>
    public bool Configured { get; set; }
}
