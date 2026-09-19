using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MusicHoarderzProvider.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MusicHoarderzProvider.Controllers;

/// <summary>
/// MusicHoarderz provider endpoints. Secrets are never returned or logged.
/// </summary>
[ApiController]
[Route("Plugins/MusicHoarderzProvider")]
public partial class ProviderController : ControllerBase
{
    private const int CredentialTimeoutSeconds = 20;

    private readonly CredentialStore _credentialStore;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ProviderController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProviderController"/> class.
    /// </summary>
    /// <param name="credentialStore">Credential store.</param>
    /// <param name="httpClientFactory">HTTP client factory.</param>
    /// <param name="logger">Logger.</param>
    public ProviderController(
        CredentialStore credentialStore,
        IHttpClientFactory httpClientFactory,
        ILogger<ProviderController> logger)
    {
        _credentialStore = credentialStore;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Gets provider status without exposing secrets (masked values and flags only).
    /// </summary>
    /// <returns>Provider status.</returns>
    [HttpGet("Status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<ProviderStatus> GetStatus()
    {
        var configuration = Plugin.Instance?.Configuration;
        return Ok(new ProviderStatus
        {
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            Enabled = configuration?.Enabled ?? false,
            MusicHoarderzEnabled = configuration?.MusicHoarderz.Enabled ?? false,
            AutoSearchEnabled = configuration?.AutoSearchEnabled ?? false,
            SpotifyEnabled = configuration?.Spotify.Enabled ?? false,
            SpotifyConfigured = !string.IsNullOrWhiteSpace(configuration?.Spotify.ClientId)
                && !string.IsNullOrWhiteSpace(configuration?.Spotify.ClientSecretEncrypted),
            SpotifyClientIdMasked = CredentialStore.Mask(configuration?.Spotify.ClientId),
            YouTubeEnabled = configuration?.YouTube.Enabled ?? false,
            YouTubeConfigured = !string.IsNullOrWhiteSpace(configuration?.YouTube.ApiKeyEncrypted),
            YouTubeApiKeyMasked = _credentialStore.MaskEncrypted(configuration?.YouTube.ApiKeyEncrypted),
            WriteMode = configuration?.WriteMode ?? "JellyfinOnly"
        });
    }

    /// <summary>
    /// Saves provider credentials. Empty inputs never overwrite stored values.
    /// </summary>
    /// <param name="request">Credential values (all optional).</param>
    /// <returns>Save result with configured flags only.</returns>
    [HttpPost("SaveCredentials")]
    [Authorize(Policy = "RequiresElevation")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public ActionResult<SaveCredentialsResponse> SaveCredentials([FromBody] CredentialUpdateRequest request)
    {
        var configuration = Plugin.Instance?.Configuration;
        if (configuration is null || request is null)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "Plugin configuration is not available.");
        }

        _credentialStore.ApplyCredentialsUpdate(
            configuration,
            request.SpotifyClientId,
            request.SpotifyClientSecret,
            request.SpotifyEnabled,
            request.SpotifyMarket,
            request.YouTubeApiKey,
            request.YouTubeEnabled,
            request.YouTubeRegionCode);
        Plugin.Instance?.SaveConfiguration();
        LogCredentialsSaved(_logger);
        return Ok(new SaveCredentialsResponse
        {
            Saved = true,
            SpotifyConfigured = !string.IsNullOrWhiteSpace(configuration.Spotify.ClientId)
                && !string.IsNullOrWhiteSpace(configuration.Spotify.ClientSecretEncrypted),
            YouTubeConfigured = !string.IsNullOrWhiteSpace(configuration.YouTube.ApiKeyEncrypted),
        });
    }

    /// <summary>
    /// Tests provider credentials without persisting request values.
    /// </summary>
    /// <param name="request">Credential values to test (falls back to stored values).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Per-provider validity without secrets.</returns>
    [HttpPost("TestCredentials")]
    [Authorize(Policy = "RequiresElevation")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<CredentialTestResponse>> TestCredentials(
        [FromBody] CredentialUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        var configuration = Plugin.Instance?.Configuration;
        if (configuration is null || request is null)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "Plugin configuration is not available.");
        }

        var spotifyId = FirstNonEmpty(request.SpotifyClientId, configuration.Spotify.ClientId);
        var spotifySecret = FirstNonEmpty(request.SpotifyClientSecret, _credentialStore.Decrypt(configuration.Spotify.ClientSecretEncrypted));
        var youTubeKey = FirstNonEmpty(request.YouTubeApiKey, _credentialStore.Decrypt(configuration.YouTube.ApiKeyEncrypted));

        var spotifyValid = false;
        var youTubeValid = false;
        var notes = new List<string>();
        if (!string.IsNullOrWhiteSpace(spotifyId) && !string.IsNullOrWhiteSpace(spotifySecret))
        {
            spotifyValid = await TestSpotifyAsync(spotifyId, spotifySecret, cancellationToken).ConfigureAwait(false);
            notes.Add(spotifyValid ? "spotify:ok" : "spotify:failed");
        }
        else
        {
            notes.Add("spotify:missing");
        }

        if (!string.IsNullOrWhiteSpace(youTubeKey))
        {
            youTubeValid = await TestYouTubeAsync(youTubeKey, cancellationToken).ConfigureAwait(false);
            notes.Add(youTubeValid ? "youtube:ok" : "youtube:failed");
        }
        else
        {
            notes.Add("youtube:missing");
        }

        LogCredentialsTested(_logger, spotifyValid, youTubeValid);
        return Ok(new CredentialTestResponse
        {
            SpotifyValid = spotifyValid,
            YouTubeValid = youTubeValid,
            Message = string.Join("; ", notes),
        });
    }

    /// <summary>
    /// Deletes stored credentials (spotify, youtube or all).
    /// </summary>
    /// <param name="provider">Provider selector: spotify, youtube or all.</param>
    /// <returns>Deletion result.</returns>
    [HttpDelete("Credentials")]
    [Authorize(Policy = "RequiresElevation")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public ActionResult<DeleteCredentialsResponse> DeleteCredentials([FromQuery] string? provider)
    {
        var configuration = Plugin.Instance?.Configuration;
        if (configuration is null)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "Plugin configuration is not available.");
        }

        var selector = (provider ?? "all").Trim();
        var clearSpotify = string.Equals(selector, "all", StringComparison.OrdinalIgnoreCase)
            || string.Equals(selector, "spotify", StringComparison.OrdinalIgnoreCase);
        var clearYouTube = string.Equals(selector, "all", StringComparison.OrdinalIgnoreCase)
            || string.Equals(selector, "youtube", StringComparison.OrdinalIgnoreCase);
        if (!clearSpotify && !clearYouTube)
        {
            return BadRequest("Unknown provider. Use spotify, youtube or all.");
        }

        _credentialStore.ClearCredentials(configuration, clearSpotify, clearYouTube);
        Plugin.Instance?.SaveConfiguration();
        LogCredentialsDeleted(_logger, selector);
        return Ok(new DeleteCredentialsResponse
        {
            DeletedSpotify = clearSpotify,
            DeletedYouTube = clearYouTube,
        });
    }

    private static string? FirstNonEmpty(string? primary, string? fallback)
    {
        if (!string.IsNullOrWhiteSpace(primary))
        {
            return primary.Trim();
        }

        return string.IsNullOrWhiteSpace(fallback) ? null : fallback;
    }

    private async Task<bool> TestSpotifyAsync(string clientId, string clientSecret, CancellationToken cancellationToken)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("musichoarderz-credentials");
            client.Timeout = TimeSpan.FromSeconds(CredentialTimeoutSeconds);
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes(clientId + ":" + clientSecret));
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://accounts.spotify.com/api/token");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", basic);
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "client_credentials" });
            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            return response.StatusCode == HttpStatusCode.OK;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            LogCredentialTestFailed(_logger, "spotify", ex);
            return false;
        }
    }

    private async Task<bool> TestYouTubeAsync(string apiKey, CancellationToken cancellationToken)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("musichoarderz-credentials");
            client.Timeout = TimeSpan.FromSeconds(CredentialTimeoutSeconds);
            var url = "https://www.googleapis.com/youtube/v3/videos?part=id&id=dQw4w9WgXcQ&key=" + Uri.EscapeDataString(apiKey);
            using var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
            return response.StatusCode == HttpStatusCode.OK;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            LogCredentialTestFailed(_logger, "youtube", ex);
            return false;
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Provider credentials saved.")]
    private static partial void LogCredentialsSaved(ILogger logger);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Provider credentials tested (spotify {SpotifyValid}, youtube {YouTubeValid}).")]
    private static partial void LogCredentialsTested(ILogger logger, bool spotifyValid, bool youTubeValid);

    [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "Provider credentials deleted ({Selector}).")]
    private static partial void LogCredentialsDeleted(ILogger logger, string selector);

    [LoggerMessage(EventId = 4, Level = LogLevel.Warning, Message = "Credential test request failed for {Provider}.")]
    private static partial void LogCredentialTestFailed(ILogger logger, string provider, Exception exception);
}

/// <summary>
/// Credential update request. All fields optional; empty strings never overwrite stored values.
/// </summary>
public class CredentialUpdateRequest
{
    /// <summary>
    /// Gets or sets the Spotify client id.
    /// </summary>
    public string? SpotifyClientId { get; set; }

    /// <summary>
    /// Gets or sets the Spotify client secret (plaintext in transit, encrypted at rest).
    /// </summary>
    public string? SpotifyClientSecret { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether Spotify is enabled.
    /// </summary>
    public bool? SpotifyEnabled { get; set; }

    /// <summary>
    /// Gets or sets the Spotify market.
    /// </summary>
    public string? SpotifyMarket { get; set; }

    /// <summary>
    /// Gets or sets the YouTube API key (plaintext in transit, encrypted at rest).
    /// </summary>
    public string? YouTubeApiKey { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether YouTube is enabled.
    /// </summary>
    public bool? YouTubeEnabled { get; set; }

    /// <summary>
    /// Gets or sets the YouTube region code.
    /// </summary>
    public string? YouTubeRegionCode { get; set; }
}

/// <summary>
/// Save credentials response (flags only, no secrets).
/// </summary>
public class SaveCredentialsResponse
{
    /// <summary>
    /// Gets or sets a value indicating whether the save succeeded.
    /// </summary>
    public bool Saved { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether Spotify credentials are configured.
    /// </summary>
    public bool SpotifyConfigured { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether YouTube credentials are configured.
    /// </summary>
    public bool YouTubeConfigured { get; set; }
}

/// <summary>
/// Credential test response (flags only, no secrets).
/// </summary>
public class CredentialTestResponse
{
    /// <summary>
    /// Gets or sets a value indicating whether Spotify credentials are valid.
    /// </summary>
    public bool SpotifyValid { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the YouTube key is valid.
    /// </summary>
    public bool YouTubeValid { get; set; }

    /// <summary>
    /// Gets or sets a summary message without secrets.
    /// </summary>
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Delete credentials response.
/// </summary>
public class DeleteCredentialsResponse
{
    /// <summary>
    /// Gets or sets a value indicating whether Spotify credentials were deleted.
    /// </summary>
    public bool DeletedSpotify { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether YouTube credentials were deleted.
    /// </summary>
    public bool DeletedYouTube { get; set; }
}

/// <summary>
/// Provider status response (masked values and flags only, never plaintext secrets).
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
    /// Gets or sets a value indicating whether automatic search is enabled.
    /// </summary>
    public bool AutoSearchEnabled { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether Spotify is enabled.
    /// </summary>
    public bool SpotifyEnabled { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether Spotify credentials are configured.
    /// </summary>
    public bool SpotifyConfigured { get; set; }

    /// <summary>
    /// Gets or sets the masked Spotify client id.
    /// </summary>
    public string SpotifyClientIdMasked { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether YouTube is enabled.
    /// </summary>
    public bool YouTubeEnabled { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether YouTube credentials are configured.
    /// </summary>
    public bool YouTubeConfigured { get; set; }

    /// <summary>
    /// Gets or sets the masked YouTube API key.
    /// </summary>
    public string YouTubeApiKeyMasked { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the active write mode.
    /// </summary>
    public string WriteMode { get; set; } = string.Empty;
}
