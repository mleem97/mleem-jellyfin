using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.GracenoteProvider.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.GracenoteProvider.Controllers;

/// <summary>
/// Gracenote provider endpoints.
/// </summary>
[ApiController]
[Route("Plugins/GracenoteProvider")]
public class ProviderController : ControllerBase
{
    private readonly GracenoteClient _gracenote;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProviderController"/> class.
    /// </summary>
    /// <param name="gracenote">Gracenote client.</param>
    public ProviderController(GracenoteClient gracenote)
    {
        _gracenote = gracenote;
    }

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
            Enabled = configuration?.EnableGracenote ?? false,
            Configured = !string.IsNullOrWhiteSpace(configuration?.GracenoteClientId),
        });
    }

    /// <summary>
    /// Validates the Gracenote client id via test registration.
    /// </summary>
    /// <param name="clientId">Client id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>User id when valid.</returns>
    [HttpPost("TestGracenote")]
    [Authorize(Policy = "RequiresElevation")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<object>> TestGracenote(
        [FromQuery] string clientId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return BadRequest("clientId required.");
        }

        var userId = await _gracenote.RegisterAsync(clientId, cancellationToken).ConfigureAwait(false);
        return Ok(new { valid = true, userId });
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
    /// Gets or sets a value indicating whether a client id is configured.
    /// </summary>
    public bool Configured { get; set; }
}
