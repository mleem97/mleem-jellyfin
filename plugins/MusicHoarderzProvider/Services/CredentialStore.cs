using System;
using System.IO;
using System.Security.Cryptography;
using Jellyfin.Plugin.MusicHoarderzProvider.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MusicHoarderzProvider.Services;

/// <summary>
/// AES-GCM credential store. Secrets are encrypted at rest; the key file lives
/// in the plugin data folder (derived from IApplicationPaths) with mode 0600.
/// Secrets are never logged.
/// </summary>
public sealed partial class CredentialStore
{
    private const string KeyFileName = "credentials.key";
    private const int KeyBytes = 32;
    private const int NonceBytes = 12;
    private const int TagBytes = 16;
    private const byte PayloadVersion = 1;

    private readonly ILogger<CredentialStore> _logger;
    private readonly string _directory;
    private readonly byte[] _key;

    /// <summary>
    /// Initializes a new instance of the <see cref="CredentialStore"/> class.
    /// </summary>
    /// <param name="logger">Logger.</param>
    /// <param name="directoryOverride">Optional storage directory override (used by tests).</param>
    public CredentialStore(ILogger<CredentialStore> logger, string? directoryOverride = null)
    {
        _logger = logger;
        _directory = directoryOverride
            ?? Plugin.Instance?.DataFolderPath
            ?? Path.Combine(Path.GetTempPath(), "MusicHoarderzProvider");
        System.IO.Directory.CreateDirectory(_directory);
        _key = LoadOrCreateKey(Path.Combine(_directory, KeyFileName));
    }

    /// <summary>
    /// Gets the storage directory holding the key file.
    /// </summary>
    public string Directory => _directory;

    /// <summary>
    /// Encrypts a plaintext secret for at-rest storage.
    /// </summary>
    /// <param name="plaintext">Secret value.</param>
    /// <returns>Base64 payload.</returns>
    public string Encrypt(string plaintext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plaintext);
        var plainBytes = System.Text.Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(NonceBytes);
        var ciphertext = new byte[plainBytes.Length];
        var tag = new byte[TagBytes];
        using (var aes = new AesGcm(_key, TagBytes))
        {
            aes.Encrypt(nonce, plainBytes, ciphertext, tag);
        }

        CryptographicOperations.ZeroMemory(plainBytes);
        var payload = new byte[1 + NonceBytes + ciphertext.Length + TagBytes];
        payload[0] = PayloadVersion;
        Buffer.BlockCopy(nonce, 0, payload, 1, NonceBytes);
        Buffer.BlockCopy(ciphertext, 0, payload, 1 + NonceBytes, ciphertext.Length);
        Buffer.BlockCopy(tag, 0, payload, 1 + NonceBytes + ciphertext.Length, TagBytes);
        CryptographicOperations.ZeroMemory(ciphertext);
        return Convert.ToBase64String(payload);
    }

    /// <summary>
    /// Decrypts a stored payload.
    /// </summary>
    /// <param name="payload">Base64 payload, if any.</param>
    /// <returns>Plaintext secret, or null when missing or invalid. Never throws for bad input.</returns>
    public string? Decrypt(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        byte[] raw;
        try
        {
            raw = Convert.FromBase64String(payload.Trim());
        }
        catch (FormatException ex)
        {
            LogCryptoFailure(_logger, ex);
            return null;
        }

        if (raw.Length < 1 + NonceBytes + TagBytes || raw[0] != PayloadVersion)
        {
            LogCryptoFailureNoDetails(_logger);
            return null;
        }

        var nonce = new byte[NonceBytes];
        var tag = new byte[TagBytes];
        var ciphertext = new byte[raw.Length - 1 - NonceBytes - TagBytes];
        Buffer.BlockCopy(raw, 1, nonce, 0, NonceBytes);
        Buffer.BlockCopy(raw, 1 + NonceBytes, ciphertext, 0, ciphertext.Length);
        Buffer.BlockCopy(raw, 1 + NonceBytes + ciphertext.Length, tag, 0, TagBytes);
        CryptographicOperations.ZeroMemory(raw);

        var plainBytes = new byte[ciphertext.Length];
        try
        {
            using (var aes = new AesGcm(_key, TagBytes))
            {
                aes.Decrypt(nonce, ciphertext, tag, plainBytes);
            }
        }
        catch (CryptographicException ex)
        {
            CryptographicOperations.ZeroMemory(plainBytes);
            LogCryptoFailure(_logger, ex);
            return null;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(ciphertext);
        }

        var result = System.Text.Encoding.UTF8.GetString(plainBytes);
        CryptographicOperations.ZeroMemory(plainBytes);
        return result;
    }

    /// <summary>
    /// Masks a plaintext value for status responses (keeps the last four characters).
    /// </summary>
    /// <param name="value">Value to mask.</param>
    /// <returns>Masked value, never the plaintext middle.</returns>
    public static string Mask(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (value.Length <= 4)
        {
            return new string('*', value.Length);
        }

        return new string('*', value.Length - 4) + value.AsSpan(value.Length - 4).ToString();
    }

    /// <summary>
    /// Masks a stored encrypted payload for status responses without exposing secrets.
    /// </summary>
    /// <param name="encryptedPayload">Stored encrypted payload, if any.</param>
    /// <returns>Masked value or empty string.</returns>
    public string MaskEncrypted(string? encryptedPayload)
    {
        return Mask(Decrypt(encryptedPayload));
    }

    /// <summary>
    /// Applies a credential update to the configuration. Empty inputs never overwrite stored values.
    /// </summary>
    /// <param name="configuration">Plugin configuration to update.</param>
    /// <param name="spotifyClientId">Spotify client id, if provided.</param>
    /// <param name="spotifyClientSecret">Spotify client secret, if provided.</param>
    /// <param name="spotifyEnabled">Spotify enabled flag, if provided.</param>
    /// <param name="spotifyMarket">Spotify market, if provided.</param>
    /// <param name="youTubeApiKey">YouTube API key, if provided.</param>
    /// <param name="youTubeEnabled">YouTube enabled flag, if provided.</param>
    /// <param name="youTubeRegionCode">YouTube region code, if provided.</param>
    public void ApplyCredentialsUpdate(
        PluginConfiguration configuration,
        string? spotifyClientId,
        string? spotifyClientSecret,
        bool? spotifyEnabled,
        string? spotifyMarket,
        string? youTubeApiKey,
        bool? youTubeEnabled,
        string? youTubeRegionCode)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (!string.IsNullOrWhiteSpace(spotifyClientId))
        {
            configuration.Spotify.ClientId = spotifyClientId.Trim();
        }

        if (!string.IsNullOrWhiteSpace(spotifyClientSecret))
        {
            configuration.Spotify.ClientSecretEncrypted = Encrypt(spotifyClientSecret.Trim());
        }

        if (spotifyEnabled.HasValue)
        {
            configuration.Spotify.Enabled = spotifyEnabled.Value;
        }

        if (!string.IsNullOrWhiteSpace(spotifyMarket))
        {
            configuration.Spotify.Market = spotifyMarket.Trim();
        }

        if (!string.IsNullOrWhiteSpace(youTubeApiKey))
        {
            configuration.YouTube.ApiKeyEncrypted = Encrypt(youTubeApiKey.Trim());
        }

        if (youTubeEnabled.HasValue)
        {
            configuration.YouTube.Enabled = youTubeEnabled.Value;
        }

        if (!string.IsNullOrWhiteSpace(youTubeRegionCode))
        {
            configuration.YouTube.RegionCode = youTubeRegionCode.Trim();
        }

        LogCredentialsUpdated(_logger);
    }

    /// <summary>
    /// Clears stored credentials. Empty inputs never overwrite; explicit clear only.
    /// </summary>
    /// <param name="configuration">Plugin configuration to update.</param>
    /// <param name="clearSpotify">Whether to clear Spotify credentials.</param>
    /// <param name="clearYouTube">Whether to clear YouTube credentials.</param>
    public void ClearCredentials(PluginConfiguration configuration, bool clearSpotify, bool clearYouTube)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (clearSpotify)
        {
            configuration.Spotify.ClientId = string.Empty;
            configuration.Spotify.ClientSecretEncrypted = string.Empty;
        }

        if (clearYouTube)
        {
            configuration.YouTube.ApiKeyEncrypted = string.Empty;
        }

        LogCredentialsCleared(_logger);
    }

    private static byte[] LoadOrCreateKey(string keyPath)
    {
        if (File.Exists(keyPath))
        {
            var existing = File.ReadAllBytes(keyPath);
            if (existing.Length == KeyBytes)
            {
                return existing;
            }
        }

        var directory = Path.GetDirectoryName(keyPath);
        if (!string.IsNullOrEmpty(directory))
        {
            System.IO.Directory.CreateDirectory(directory);
        }

        var fresh = RandomNumberGenerator.GetBytes(KeyBytes);
        File.WriteAllBytes(keyPath, fresh);
        RestrictKeyFile(keyPath);
        var copy = new byte[KeyBytes];
        Buffer.BlockCopy(fresh, 0, copy, 0, KeyBytes);
        CryptographicOperations.ZeroMemory(fresh);
        return copy;
    }

    private static void RestrictKeyFile(string keyPath)
    {
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            File.SetUnixFileMode(keyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Stored credential payload invalid.")]
    private static partial void LogCryptoFailure(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "Stored credential payload invalid.")]
    private static partial void LogCryptoFailureNoDetails(ILogger logger);

    [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "Provider credentials updated.")]
    private static partial void LogCredentialsUpdated(ILogger logger);

    [LoggerMessage(EventId = 4, Level = LogLevel.Information, Message = "Provider credentials cleared.")]
    private static partial void LogCredentialsCleared(ILogger logger);
}
