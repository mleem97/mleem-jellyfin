using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Jellyfin.Plugin.BetterMusicDisplay.Configuration;

namespace Jellyfin.Plugin.BetterMusicDisplay.Services;

/// <summary>
/// Persists Better MusicDisplay settings per Jellyfin user.
/// </summary>
public static class UserMusicSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Gets settings for the supplied user id.
    /// </summary>
    /// <param name="userId">Jellyfin user id.</param>
    /// <returns>User settings.</returns>
    public static UserMusicViewSettings Get(Guid userId)
    {
        var path = GetSettingsPath(userId);
        if (!File.Exists(path))
        {
            return CreateDefault(userId);
        }

        try
        {
            var json = File.ReadAllText(path);
            var settings = JsonSerializer.Deserialize<UserMusicViewSettings>(json, SerializerOptions);
            return Normalize(settings ?? CreateDefault(userId), userId);
        }
        catch (JsonException)
        {
            return CreateDefault(userId);
        }
        catch (IOException)
        {
            return CreateDefault(userId);
        }
        catch (UnauthorizedAccessException)
        {
            return CreateDefault(userId);
        }
    }

    /// <summary>
    /// Saves settings for the supplied user id.
    /// </summary>
    /// <param name="userId">Jellyfin user id.</param>
    /// <param name="settings">Settings to persist.</param>
    /// <returns>Saved settings.</returns>
    public static UserMusicViewSettings Save(Guid userId, UserMusicViewSettings settings)
    {
        var normalized = Normalize(settings, userId);
        var directory = GetSettingsDirectory();
        Directory.CreateDirectory(directory);
        var json = JsonSerializer.Serialize(normalized, SerializerOptions);
        File.WriteAllText(GetSettingsPath(userId), json);
        return normalized;
    }

    /// <summary>
    /// Deletes settings for the supplied user id.
    /// </summary>
    /// <param name="userId">Jellyfin user id.</param>
    /// <returns>A value indicating whether a file was deleted.</returns>
    public static bool Delete(Guid userId)
    {
        var path = GetSettingsPath(userId);
        if (!File.Exists(path))
        {
            return false;
        }

        File.Delete(path);
        return true;
    }

    private static UserMusicViewSettings CreateDefault(Guid userId)
    {
        var configuration = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        return new UserMusicViewSettings
        {
            UserId = userId,
            LandingPage = configuration.DefaultLandingPage,
            AlbumLayout = configuration.DefaultAlbumLayout,
            ArtistLayout = "Grid",
            SongLayout = "VirtualTable",
            TileSize = "Medium",
            MarkMissingCovers = true,
            SortPreferences = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["albums"] = "SortName",
                ["artists"] = "SortName",
                ["songs"] = "SortName"
            },
            EnabledSections = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
            {
                ["albums"] = true,
                ["artists"] = true,
                ["albumArtists"] = true,
                ["playlists"] = true,
                ["genres"] = true,
                ["songs"] = true,
                ["suggestions"] = true
            }
        };
    }

    private static UserMusicViewSettings Normalize(UserMusicViewSettings settings, Guid userId)
    {
        settings.UserId = userId;
        settings.LandingPage = NormalizeChoice(settings.LandingPage, "Suggestions");
        settings.AlbumLayout = NormalizeChoice(settings.AlbumLayout, "Grid");
        settings.ArtistLayout = NormalizeChoice(settings.ArtistLayout, "Grid");
        settings.SongLayout = NormalizeChoice(settings.SongLayout, "VirtualTable");
        settings.TileSize = NormalizeChoice(settings.TileSize, "Medium");
        settings.SortPreferences ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        settings.EnabledSections ??= new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        return settings;
    }

    private static string NormalizeChoice(string value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static string GetSettingsDirectory()
    {
        var basePath = Plugin.Instance?.DataFolderPath ?? Path.Combine(AppContext.BaseDirectory, "BetterMusicDisplay");
        return Path.Combine(basePath, "user-settings");
    }

    private static string GetSettingsPath(Guid userId)
    {
        return Path.Combine(GetSettingsDirectory(), string.Concat(userId.ToString("N"), ".json"));
    }
}
