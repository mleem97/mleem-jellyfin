using System.Net;
using Jellyfin.Plugin.BetterMusicDisplay.Services;
using Jellyfin.Plugin.HddDisplay.Services;
using Xunit;
using ProviderConfiguration = Jellyfin.Plugin.MusicHoarderzProvider.Configuration.PluginConfiguration;

namespace Jellyfin.Plugins.Tests;

public sealed class PluginBaselineTests
{
    [Fact]
    public void HddDisplayRejectsBlankLibraryPath()
    {
        var resolution = MountResolver.Resolve("   ");

        Assert.False(resolution.IsResolved);
        Assert.Equal("none", resolution.ResolutionProvider);
        Assert.Contains("normalized", resolution.Diagnostic, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BetterMusicDisplayCreatesIsolatedDefaultUserSettings()
    {
        using var directory = new TemporaryDirectory();
        var store = new UserMusicSettingsStore(directory.Path);
        var userId = Guid.NewGuid();

        var settings = store.Get(userId);

        Assert.Equal(1, settings.SchemaVersion);
        Assert.Equal(userId, settings.UserId);
        Assert.Equal("Suggestions", settings.LandingPage);
        Assert.Equal("Grid", settings.AlbumLayout);
        Assert.Equal("VirtualTable", settings.SongLayout);
        Assert.True(settings.EnabledSections["albums"]);
    }

    [Fact]
    public void MusicHoarderzProviderUsesSafeDefaults()
    {
        var configuration = new ProviderConfiguration();

        Assert.True(configuration.Enabled);
        Assert.True(configuration.MusicHoarderz.Enabled);
        Assert.False(configuration.AutoSearchEnabled);
        Assert.Equal("JellyfinOnly", configuration.WriteMode);
        Assert.Equal(1000, configuration.MinimumWidth);
        Assert.Equal(1000, configuration.MinimumHeight);
    }

    [Fact]
    public async Task SharedHttpStubReturnsConfiguredResponse()
    {
        using var client = new HttpClient(StubHttpMessageHandler.Json("{\"ok\":true}"));

        using var response = await client.GetAsync(new Uri("https://example.invalid/status"));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("{\"ok\":true}", body);
    }

    [Fact]
    public void TemporaryDirectoryIsCreatedAndWritable()
    {
        string directoryPath;
        using (var directory = new TemporaryDirectory())
        {
            directoryPath = directory.Path;
            var filePath = Path.Combine(directory.Path, "probe.txt");
            File.WriteAllText(filePath, "ok");
            Assert.Equal("ok", File.ReadAllText(filePath));
        }

        Assert.False(Directory.Exists(directoryPath));
    }
}
