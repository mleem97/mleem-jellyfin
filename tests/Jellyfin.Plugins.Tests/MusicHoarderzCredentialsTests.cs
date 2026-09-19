using Jellyfin.Plugin.MusicHoarderzProvider.Configuration;
using Jellyfin.Plugin.MusicHoarderzProvider.Controllers;
using Jellyfin.Plugin.MusicHoarderzProvider.Services;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Jellyfin.Plugins.Tests;

public class MusicHoarderzCredentialsTests : IDisposable
{
    private readonly TemporaryDirectory _temp = new();

    [Fact]
    public void EncryptDecrypt_RoundTrips()
    {
        var store = CreateStore();
        const string secret = "super-secret-value-123";

        var encrypted = store.Encrypt(secret);

        Assert.NotEqual(secret, encrypted);
        Assert.DoesNotContain(secret, encrypted);
        Assert.Equal(secret, store.Decrypt(encrypted));
    }

    [Fact]
    public void Decrypt_InvalidPayload_ReturnsNull()
    {
        var store = CreateStore();

        Assert.Null(store.Decrypt(null));
        Assert.Null(store.Decrypt(string.Empty));
        Assert.Null(store.Decrypt("!!!not-base64!!!"));
        Assert.Null(store.Decrypt(Convert.ToBase64String(new byte[] { 9, 9, 9 })));
    }

    [Fact]
    public void Decrypt_TamperedPayload_ReturnsNull()
    {
        var store = CreateStore();
        var encrypted = store.Encrypt("secret-value");
        var raw = Convert.FromBase64String(encrypted);
        raw[raw.Length - 1] ^= 0xFF;

        Assert.Null(store.Decrypt(Convert.ToBase64String(raw)));
    }

    [Fact]
    public void Mask_KeepsOnlyLastFourCharacters()
    {
        const string value = "secretvalue123";

        var masked = CredentialStore.Mask(value);

        Assert.NotEqual(value, masked);
        Assert.Equal(value.Length, masked.Length);
        Assert.EndsWith("e123", masked);
        Assert.DoesNotContain("secretvalue", masked);
        Assert.Equal(string.Empty, CredentialStore.Mask(null));
        Assert.Equal(string.Empty, CredentialStore.Mask(string.Empty));
        Assert.Equal("**", CredentialStore.Mask("ab"));
    }

    [Fact]
    public void MaskEncrypted_NeverExposesPlaintext()
    {
        var store = CreateStore();
        const string secret = "yt-api-key-abcdef-987654";
        var encrypted = store.Encrypt(secret);

        var masked = store.MaskEncrypted(encrypted);

        Assert.NotEqual(secret, masked);
        Assert.DoesNotContain(secret, masked);
        Assert.EndsWith(secret.Substring(secret.Length - 4), masked);
        Assert.Equal(string.Empty, store.MaskEncrypted(null));
    }

    [Fact]
    public void ApplyCredentialsUpdate_EmptyInputsNeverOverwrite()
    {
        var store = CreateStore();
        var config = new PluginConfiguration();
        config.Spotify.ClientId = "original-id";
        config.Spotify.ClientSecretEncrypted = store.Encrypt("original-secret");
        config.Spotify.Market = "DE";

        store.ApplyCredentialsUpdate(config, " ", "", null, null, null, null, null);

        Assert.Equal("original-id", config.Spotify.ClientId);
        Assert.Equal("original-secret", store.Decrypt(config.Spotify.ClientSecretEncrypted));
        Assert.Equal("DE", config.Spotify.Market);
    }

    [Fact]
    public void ApplyCredentialsUpdate_NewValuesAreEncrypted()
    {
        var store = CreateStore();
        var config = new PluginConfiguration();

        store.ApplyCredentialsUpdate(config, "client-id", "client-secret", true, "US", "yt-key", true, "US");

        Assert.Equal("client-id", config.Spotify.ClientId);
        Assert.Equal("client-secret", store.Decrypt(config.Spotify.ClientSecretEncrypted));
        Assert.True(config.Spotify.Enabled);
        Assert.Equal("US", config.Spotify.Market);
        Assert.Equal("yt-key", store.Decrypt(config.YouTube.ApiKeyEncrypted));
        Assert.True(config.YouTube.Enabled);
        Assert.Equal("US", config.YouTube.RegionCode);
    }

    [Fact]
    public void ClearCredentials_RemovesStoredValues()
    {
        var store = CreateStore();
        var config = new PluginConfiguration();
        config.Spotify.ClientId = "id";
        config.Spotify.ClientSecretEncrypted = store.Encrypt("secret");
        config.YouTube.ApiKeyEncrypted = store.Encrypt("key");

        store.ClearCredentials(config, clearSpotify: true, clearYouTube: true);

        Assert.Equal(string.Empty, config.Spotify.ClientId);
        Assert.Equal(string.Empty, config.Spotify.ClientSecretEncrypted);
        Assert.Equal(string.Empty, config.YouTube.ApiKeyEncrypted);
    }

    [Fact]
    public void KeyFile_IsCreatedInOverrideDirectory()
    {
        var store = CreateStore();

        var keyPath = Path.Combine(store.Directory, "credentials.key");

        Assert.Equal(_temp.Path, store.Directory);
        Assert.True(File.Exists(keyPath));
        if (OperatingSystem.IsLinux())
        {
            var mode = File.GetUnixFileMode(keyPath);
            Assert.True((mode & UnixFileMode.UserRead) != 0);
            Assert.True((mode & UnixFileMode.UserWrite) != 0);
            Assert.True((mode & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.OtherRead | UnixFileMode.OtherWrite)) == 0);
        }
    }

    [Fact]
    public void Status_ContainsNoPlaintextSecrets()
    {
        var controller = CreateController();

        var status = GetStatusValue(controller);

        Assert.False(status.SpotifyConfigured);
        Assert.False(status.YouTubeConfigured);
        Assert.Equal(string.Empty, status.SpotifyClientIdMasked);
        Assert.Equal(string.Empty, status.YouTubeApiKeyMasked);
        Assert.Equal("JellyfinOnly", status.WriteMode);
    }

    [Fact]
    public void SaveCredentials_WithoutPluginInstance_ReturnsServerError()
    {
        var controller = CreateController();

        var result = controller.SaveCredentials(new CredentialUpdateRequest { SpotifyClientId = "x" });

        var statusResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(500, statusResult.StatusCode);
    }

    [Fact]
    public void DeleteCredentials_WithoutPluginInstance_ReturnsServerError()
    {
        var controller = CreateController();

        var result = controller.DeleteCredentials("all");

        var statusResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(500, statusResult.StatusCode);
    }

    public void Dispose()
    {
        _temp.Dispose();
    }

    private CredentialStore CreateStore()
    {
        return new CredentialStore(
            MusicHoarderzNoopLogger<CredentialStore>.Instance,
            _temp.Path);
    }

    private ProviderController CreateController()
    {
        return new ProviderController(
            CreateStore(),
            new MusicHoarderzTestHttpClientFactory(
                new MusicHoarderzQueueHandler((_, _) => new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK))),
            MusicHoarderzNoopLogger<ProviderController>.Instance);
    }

    private static ProviderStatus GetStatusValue(ProviderController controller)
    {
        var result = controller.GetStatus();
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        return Assert.IsType<ProviderStatus>(ok.Value);
    }
}
