using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.SpotifyProvider.Services;
using Microsoft.AspNetCore.Authorization;
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
    private readonly SpotifyAuthService _auth;
    private readonly SpotifyApiClient _apiClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProviderController"/> class.
    /// </summary>
    /// <param name="auth">Spotify auth service.</param>
    /// <param name="apiClient">Spotify API client.</param>
    public ProviderController(SpotifyAuthService auth, SpotifyApiClient apiClient)
    {
        _auth = auth;
        _apiClient = apiClient;
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
            Enabled = configuration?.EnableImageLookup ?? false,
            Configured = !string.IsNullOrWhiteSpace(configuration?.ClientId)
                && !string.IsNullOrWhiteSpace(configuration?.ClientSecret),
            MusicMetadataEnabled = configuration?.EnableMusicMetadata ?? false,
            AudiobookMetadataEnabled = configuration?.EnableAudiobookMetadata ?? false,
            PodcastMetadataEnabled = configuration?.EnablePodcastMetadata ?? false,
            Market = configuration?.Market ?? "DE",
        });
    }

    /// <summary>
    /// Tests Spotify credentials by requesting an access token.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result.</returns>
    [HttpPost("TestCredentials")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CredentialsTestResult>> TestCredentials(CancellationToken cancellationToken)
    {
        try
        {
            _auth.Invalidate();
            var token = await _auth.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
            return Ok(new CredentialsTestResult
            {
                Success = !string.IsNullOrWhiteSpace(token),
                Message = "Spotify-Verbindung erfolgreich hergestellt! Token wurde validiert.",
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new CredentialsTestResult
            {
                Success = false,
                Message = "Verbindungsfehler: " + ex.Message,
            });
        }
    }

    /// <summary>
    /// Live search test for admin troubleshooting.
    /// </summary>
    /// <param name="query">Search term.</param>
    /// <param name="type">Type (album, artist, audiobook, track).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Search results.</returns>
    [HttpGet("SearchTest")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult> SearchTest([FromQuery] string query, [FromQuery] string type = "album", CancellationToken cancellationToken = default)
    {
        var config = Plugin.Instance?.Configuration;
        var market = config?.Market ?? "DE";
        var result = await _apiClient.SearchAsync(query, type, market, 5, cancellationToken).ConfigureAwait(false);
        return Ok(result);
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

    /// <summary>
    /// Gets or sets a value indicating whether music metadata is enabled.
    /// </summary>
    public bool MusicMetadataEnabled { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether audiobook metadata is enabled.
    /// </summary>
    public bool AudiobookMetadataEnabled { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether podcast metadata is enabled.
    /// </summary>
    public bool PodcastMetadataEnabled { get; set; }

    /// <summary>
    /// Gets or sets the configured market.
    /// </summary>
    public string Market { get; set; } = "DE";
}

/// <summary>
/// Credentials test result.
/// </summary>
public class CredentialsTestResult
{
    /// <summary>
    /// Gets or sets a value indicating whether the test succeeded.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Gets or sets the response message.
    /// </summary>
    public string Message { get; set; } = string.Empty;
}
