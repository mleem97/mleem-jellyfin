using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SpotifyProvider.Services;

/// <summary>
/// Spotify access token from the Client Credentials flow.
/// </summary>
/// <param name="AccessToken">Bearer token.</param>
/// <param name="ExpiresAtUtc">Expiry timestamp.</param>
public sealed record SpotifyToken(string AccessToken, DateTimeOffset ExpiresAtUtc);

/// <summary>
/// Handles Spotify Client Credentials authentication with in-memory token caching.
/// </summary>
public sealed partial class SpotifyAuthService : IDisposable
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SpotifyAuthService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private SpotifyToken? _cached;

    /// <summary>
    /// Initializes a new instance of the <see cref="SpotifyAuthService"/> class.
    /// </summary>
    /// <param name="httpClientFactory">HTTP client factory.</param>
    /// <param name="logger">Logger.</param>
    public SpotifyAuthService(IHttpClientFactory httpClientFactory, ILogger<SpotifyAuthService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Gets a valid bearer token, refreshing it when expired.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Bearer token.</returns>
    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        var config = Plugin.Instance?.Configuration
            ?? throw new InvalidOperationException("Spotify plugin configuration is unavailable.");

        if (string.IsNullOrWhiteSpace(config.ClientId) || string.IsNullOrWhiteSpace(config.ClientSecret))
        {
            throw new InvalidOperationException("Spotify ClientId/ClientSecret are not configured.");
        }

        if (_cached is not null && _cached.ExpiresAtUtc > DateTimeOffset.UtcNow.AddMinutes(1))
        {
            return _cached.AccessToken;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cached is not null && _cached.ExpiresAtUtc > DateTimeOffset.UtcNow.AddMinutes(1))
            {
                return _cached.AccessToken;
            }

            var client = _httpClientFactory.CreateClient("spotify-accounts");
            client.Timeout = TimeSpan.FromSeconds(20);
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes(config.ClientId + ":" + config.ClientSecret));
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://accounts.spotify.com/api/token");
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "client_credentials" });
            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var payload = await response.Content.ReadFromJsonAsync<SpotifyTokenResponse>(cancellationToken: cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Empty Spotify token response.");

            _cached = new SpotifyToken(payload.AccessToken, DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, payload.ExpiresIn - 60)));
            LogTokenRefreshed(_logger, _cached.ExpiresAtUtc);
            return _cached.AccessToken;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Clears the cached token (e.g. after a 401 response).
    /// </summary>
    public void Invalidate()
    {
        _cached = null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _gate.Dispose();
        GC.SuppressFinalize(this);
    }

    [LoggerMessage(EventId = 0, Level = LogLevel.Information, Message = "Spotify access token refreshed, valid until {ExpiresAtUtc}")]
    private static partial void LogTokenRefreshed(ILogger logger, DateTimeOffset expiresAtUtc);

    private sealed class SpotifyTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
    }
}
