using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.GracenoteProvider.Configuration;
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
            ApiVersion = configuration?.ApiVersion ?? "v2",
            AlbumMetadataEnabled = configuration?.EnableAlbumMetadata ?? true,
            ArtistMetadataEnabled = configuration?.EnableArtistMetadata ?? true,
            ImageLookupEnabled = configuration?.EnableImageLookup ?? true,
        });
    }

    /// <summary>
    /// Validates the Gracenote client id via test registration or connection check.
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
        var id = !string.IsNullOrWhiteSpace(clientId) ? clientId : Plugin.Instance?.Configuration?.GracenoteClientId;
        if (string.IsNullOrWhiteSpace(id))
        {
            return BadRequest(new { valid = false, message = "Client-ID ist erforderlich." });
        }

        try
        {
            var userId = await _gracenote.RegisterAsync(id, cancellationToken).ConfigureAwait(false);
            if (Plugin.Instance?.Configuration != null)
            {
                Plugin.Instance.Configuration.GracenoteUserId = userId;
                Plugin.Instance.SaveConfiguration();
            }

            return Ok(new { valid = true, userId, message = "Gracenote User-ID erfolgreich registriert!" });
        }
        catch (Exception ex)
        {
            return BadRequest(new { valid = false, message = "Registrierung fehlgeschlagen: " + ex.Message });
        }
    }

    /// <summary>
    /// Live search test for admin verification.
    /// </summary>
    /// <param name="query">Search query.</param>
    /// <param name="type">Search type (album or artist).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Search results.</returns>
    [HttpGet("SearchTest")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult> SearchTest(
        [FromQuery] string query,
        [FromQuery] string type = "album",
        CancellationToken cancellationToken = default)
    {
        var config = Plugin.Instance?.Configuration;
        if (config is null)
        {
            return Ok(Array.Empty<object>());
        }

        if (string.Equals(type, "artist", StringComparison.OrdinalIgnoreCase))
        {
            var artists = await _gracenote.SearchArtistAsync(config, query, cancellationToken).ConfigureAwait(false);
            return Ok(artists);
        }

        var albums = await _gracenote.SearchAlbumAsync(config, string.Empty, query, cancellationToken).ConfigureAwait(false);
        return Ok(albums);
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

    /// <summary>
    /// Gets or sets the API version.
    /// </summary>
    public string ApiVersion { get; set; } = "v2";

    /// <summary>
    /// Gets or sets a value indicating whether album metadata is enabled.
    /// </summary>
    public bool AlbumMetadataEnabled { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether artist metadata is enabled.
    /// </summary>
    public bool ArtistMetadataEnabled { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether image lookup is enabled.
    /// </summary>
    public bool ImageLookupEnabled { get; set; }
}
