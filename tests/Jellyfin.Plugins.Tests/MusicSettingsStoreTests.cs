using Jellyfin.Plugin.MusicSuite.Configuration;
using Jellyfin.Plugin.MusicSuite.Services;
using Xunit;

namespace Jellyfin.Plugins.Tests;

public sealed class MusicSettingsStoreTests
{
    [Fact]
    public void SettingsRoundTripPreservesPreferencesAndIsolatesUsers()
    {
        using var directory = new TemporaryDirectory();
        var store = new UserMusicSettingsStore(directory.Path);
        var userId = Guid.NewGuid();
        var settings = new UserMusicViewSettings { TileSize = "Large", AlbumLayout = "List" };
        settings.SortPreferences["albums"] = "DateCreated";
        settings.EnabledSections["artists"] = false;
        store.Save(userId, settings);

        var restarted = new UserMusicSettingsStore(directory.Path);
        var loaded = restarted.Get(userId);
        Assert.Equal("Large", loaded.TileSize);
        Assert.Equal("List", loaded.AlbumLayout);
        Assert.Equal("DateCreated", loaded.SortPreferences["albums"]);
        Assert.False(loaded.EnabledSections["artists"]);
        Assert.Equal("Medium", restarted.Get(Guid.NewGuid()).TileSize);
    }

    [Fact]
    public void NewUsersResetsAndDisabledCustomizationUseAdministratorDefaults()
    {
        using var directory = new TemporaryDirectory();
        var configuration = new PluginConfiguration { DefaultTileSize = "Small" };
        var store = new UserMusicSettingsStore(directory.Path, () => configuration);
        var userId = Guid.NewGuid();
        Assert.Equal("Small", store.Get(userId).TileSize);
        store.Save(userId, new UserMusicViewSettings { TileSize = "Large" });
        Assert.Equal("Large", store.Get(userId).TileSize);

        configuration.AllowUserCustomization = false;
        Assert.Equal("Small", store.Get(userId).TileSize);
        configuration.AllowUserCustomization = true;
        Assert.Equal("Large", store.Get(userId).TileSize);
        store.Delete(userId);
        Assert.Equal("Small", store.Get(userId).TileSize);
    }

    [Fact]
    public void SaveOverridesBodyUserIdAndNormalizesUnknownChoices()
    {
        using var directory = new TemporaryDirectory();
        var store = new UserMusicSettingsStore(directory.Path);
        var routeUserId = Guid.NewGuid();

        var settings = new UserMusicViewSettings
        {
            UserId = Guid.NewGuid(),
            LandingPage = "unknown",
            AlbumLayout = "unknown",
            ArtistLayout = "list",
            SongLayout = "unknown",
            TileSize = "huge",
        };
        settings.SortPreferences["albums"] = "DateCreated";
        settings.SortPreferences["unsupported"] = "ignored";
        settings.EnabledSections["albums"] = true;
        settings.EnabledSections["unsupported"] = true;

        var saved = store.Save(routeUserId, settings);

        Assert.Equal(routeUserId, saved.UserId);
        Assert.Equal(1, saved.SchemaVersion);
        Assert.Equal("Suggestions", saved.LandingPage);
        Assert.Equal("Grid", saved.AlbumLayout);
        Assert.Equal("List", saved.ArtistLayout);
        Assert.Equal("VirtualTable", saved.SongLayout);
        Assert.Equal("Medium", saved.TileSize);
        Assert.DoesNotContain("unsupported", saved.SortPreferences.Keys);
        Assert.DoesNotContain("unsupported", saved.EnabledSections.Keys);
    }

    [Fact]
    public void ConcurrentSavesLeaveOneValidAtomicFile()
    {
        using var directory = new TemporaryDirectory();
        var store = new UserMusicSettingsStore(directory.Path);
        var userId = Guid.NewGuid();
        var tileSizes = new[] { "Small", "Medium", "Large" };

        Parallel.For(0, 30, index =>
        {
            store.Save(userId, new UserMusicViewSettings
            {
                TileSize = tileSizes[index % tileSizes.Length]
            });
        });

        var persisted = store.Get(userId);
        Assert.Contains(persisted.TileSize, tileSizes);
        var settingsDirectory = Path.Combine(directory.Path, "user-settings");
        Assert.Single(Directory.GetFiles(settingsDirectory, "*.json"));
        Assert.Empty(Directory.GetFiles(settingsDirectory, "*.tmp"));
    }

    [Fact]
    public void CorruptFileIsQuarantinedAndDefaultsAreReturned()
    {
        using var directory = new TemporaryDirectory();
        var store = new UserMusicSettingsStore(directory.Path);
        var userId = Guid.NewGuid();
        var settingsDirectory = Path.Combine(directory.Path, "user-settings");
        Directory.CreateDirectory(settingsDirectory);
        var path = Path.Combine(settingsDirectory, string.Concat(userId.ToString("N"), ".json"));
        File.WriteAllText(path, "{not-json");

        var settings = store.Get(userId);

        Assert.Equal(userId, settings.UserId);
        Assert.False(File.Exists(path));
        Assert.Single(Directory.GetFiles(settingsDirectory, "*.corrupt-*"));
    }

    [Fact]
    public void EmptyUserIdIsRejected()
    {
        using var directory = new TemporaryDirectory();
        var store = new UserMusicSettingsStore(directory.Path);

        Assert.Throws<ArgumentException>(() => store.Get(Guid.Empty));
    }
}
