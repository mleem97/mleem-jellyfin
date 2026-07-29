using Jellyfin.Plugin.BetterMusicDisplay.Configuration;
using Jellyfin.Plugin.BetterMusicDisplay.Services;
using Xunit;

namespace Jellyfin.Plugins.Tests;

public sealed class MusicSettingsStoreTests
{
    [Fact]
    public void SaveOverridesBodyUserIdAndNormalizesUnknownChoices()
    {
        using var directory = new TemporaryDirectory();
        var store = new UserMusicSettingsStore(directory.Path);
        var routeUserId = Guid.NewGuid();

        var saved = store.Save(routeUserId, new UserMusicViewSettings
        {
            UserId = Guid.NewGuid(),
            LandingPage = "unknown",
            AlbumLayout = "unknown",
            ArtistLayout = "list",
            SongLayout = "unknown",
            TileSize = "huge",
            SortPreferences = new Dictionary<string, string>
            {
                ["albums"] = "DateCreated",
                ["unsupported"] = "ignored"
            },
            EnabledSections = new Dictionary<string, bool>
            {
                ["albums"] = true,
                ["unsupported"] = true
            }
        });

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
