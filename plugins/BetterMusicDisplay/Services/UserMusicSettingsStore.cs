using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Jellyfin.Plugin.BetterMusicDisplay.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.BetterMusicDisplay.Services;

/// <summary>
/// Persists Better MusicDisplay settings per Jellyfin user.
/// </summary>
public sealed class UserMusicSettingsStore : IUserMusicSettingsStore
{
    private const int CurrentSchemaVersion = 1;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };
    private static readonly HashSet<string> LandingPages = new(StringComparer.OrdinalIgnoreCase)
    {
        "Suggestions",
        "Albums"
    };
    private static readonly HashSet<string> GridLayouts = new(StringComparer.OrdinalIgnoreCase)
    {
        "Grid",
        "List"
    };
    private static readonly HashSet<string> SongLayouts = new(StringComparer.OrdinalIgnoreCase)
    {
        "VirtualTable",
        "Table"
    };
    private static readonly HashSet<string> TileSizes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Small",
        "Medium",
        "Large"
    };
    private static readonly HashSet<string> PreferenceKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "albums",
        "artists",
        "songs"
    };
    private static readonly HashSet<string> SectionKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "albums",
        "artists",
        "albumArtists",
        "playlists",
        "genres",
        "songs",
        "suggestions"
    };

    private readonly ConcurrentDictionary<Guid, object> _userLocks = new();
    private readonly Func<string> _dataFolderProvider;
    private readonly ILogger<UserMusicSettingsStore> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="UserMusicSettingsStore"/> class for dependency injection.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    public UserMusicSettingsStore(ILogger<UserMusicSettingsStore> logger)
        : this(
            () => Plugin.Instance?.DataFolderPath
                ?? Path.Combine(AppContext.BaseDirectory, "BetterMusicDisplay"),
            logger)
    {
    }

    /// <summary>
    /// Initializes a testable store rooted in a supplied data folder.
    /// </summary>
    /// <param name="dataFolderPath">Plugin data folder.</param>
    public UserMusicSettingsStore(string dataFolderPath)
        : this(
            () => dataFolderPath,
            NullLogger<UserMusicSettingsStore>.Instance)
    {
        if (string.IsNullOrWhiteSpace(dataFolderPath))
        {
            throw new ArgumentException("A data folder is required.", nameof(dataFolderPath));
        }
    }

    private UserMusicSettingsStore(
        Func<string> dataFolderProvider,
        ILogger<UserMusicSettingsStore> logger)
    {
        _dataFolderProvider = dataFolderProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public UserMusicViewSettings Get(Guid userId)
    {
        ValidateUserId(userId);
        lock (GetUserLock(userId))
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
            catch (JsonException exception)
            {
                QuarantineCorruptFile(path, exception);
                return CreateDefault(userId);
            }
            catch (IOException exception)
            {
                _logger.LogWarning(exception, "Failed to read Better MusicDisplay settings for user {UserId}", userId);
                return CreateDefault(userId);
            }
            catch (UnauthorizedAccessException exception)
            {
                _logger.LogWarning(exception, "Access denied reading Better MusicDisplay settings for user {UserId}", userId);
                return CreateDefault(userId);
            }
        }
    }

    /// <inheritdoc />
    public UserMusicViewSettings Save(Guid userId, UserMusicViewSettings settings)
    {
        ValidateUserId(userId);
        ArgumentNullException.ThrowIfNull(settings);
        lock (GetUserLock(userId))
        {
            var normalized = Normalize(settings, userId);
            var directory = GetSettingsDirectory();
            Directory.CreateDirectory(directory);
            var targetPath = GetSettingsPath(userId);
            var temporaryPath = string.Concat(targetPath, ".", Guid.NewGuid().ToString("N"), ".tmp");
            try
            {
                var json = JsonSerializer.Serialize(normalized, SerializerOptions);
                File.WriteAllText(temporaryPath, json);
                File.Move(temporaryPath, targetPath, overwrite: true);
                return Clone(normalized);
            }
            finally
            {
                TryDeleteTemporaryFile(temporaryPath);
            }
        }
    }

    /// <inheritdoc />
    public bool Delete(Guid userId)
    {
        ValidateUserId(userId);
        lock (GetUserLock(userId))
        {
            var path = GetSettingsPath(userId);
            if (!File.Exists(path))
            {
                return false;
            }

            File.Delete(path);
            return true;
        }
    }

    private static void ValidateUserId(Guid userId)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("A non-empty Jellyfin user id is required.", nameof(userId));
        }
    }

    private static UserMusicViewSettings CreateDefault(Guid userId)
    {
        var configuration = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        return new UserMusicViewSettings
        {
            SchemaVersion = CurrentSchemaVersion,
            UserId = userId,
            LandingPage = NormalizeChoice(configuration.DefaultLandingPage, LandingPages, "Suggestions"),
            AlbumLayout = NormalizeChoice(configuration.DefaultAlbumLayout, GridLayouts, "Grid"),
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
        return new UserMusicViewSettings
        {
            SchemaVersion = CurrentSchemaVersion,
            UserId = userId,
            LandingPage = NormalizeChoice(settings.LandingPage, LandingPages, "Suggestions"),
            AlbumLayout = NormalizeChoice(settings.AlbumLayout, GridLayouts, "Grid"),
            ArtistLayout = NormalizeChoice(settings.ArtistLayout, GridLayouts, "Grid"),
            SongLayout = NormalizeChoice(settings.SongLayout, SongLayouts, "VirtualTable"),
            TileSize = NormalizeChoice(settings.TileSize, TileSizes, "Medium"),
            MarkMissingCovers = settings.MarkMissingCovers,
            SortPreferences = NormalizeStringDictionary(settings.SortPreferences, PreferenceKeys),
            EnabledSections = NormalizeBooleanDictionary(settings.EnabledSections, SectionKeys)
        };
    }

    private static string NormalizeChoice(
        string? value,
        IReadOnlySet<string> allowedValues,
        string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var trimmed = value.Trim();
        return allowedValues.FirstOrDefault(item => string.Equals(item, trimmed, StringComparison.OrdinalIgnoreCase))
            ?? fallback;
    }

    private static Dictionary<string, string> NormalizeStringDictionary(
        IReadOnlyDictionary<string, string>? source,
        IReadOnlySet<string> allowedKeys)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (source is null)
        {
            return result;
        }

        foreach (var item in source)
        {
            if (allowedKeys.Contains(item.Key)
                && !string.IsNullOrWhiteSpace(item.Value)
                && item.Value.Length <= 64)
            {
                result[item.Key] = item.Value.Trim();
            }
        }

        return result;
    }

    private static Dictionary<string, bool> NormalizeBooleanDictionary(
        IReadOnlyDictionary<string, bool>? source,
        IReadOnlySet<string> allowedKeys)
    {
        var result = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        if (source is null)
        {
            return result;
        }

        foreach (var item in source)
        {
            if (allowedKeys.Contains(item.Key))
            {
                result[item.Key] = item.Value;
            }
        }

        return result;
    }

    private static UserMusicViewSettings Clone(UserMusicViewSettings settings)
    {
        return new UserMusicViewSettings
        {
            SchemaVersion = settings.SchemaVersion,
            UserId = settings.UserId,
            LandingPage = settings.LandingPage,
            AlbumLayout = settings.AlbumLayout,
            ArtistLayout = settings.ArtistLayout,
            SongLayout = settings.SongLayout,
            TileSize = settings.TileSize,
            MarkMissingCovers = settings.MarkMissingCovers,
            SortPreferences = new Dictionary<string, string>(
                settings.SortPreferences,
                StringComparer.OrdinalIgnoreCase),
            EnabledSections = new Dictionary<string, bool>(
                settings.EnabledSections,
                StringComparer.OrdinalIgnoreCase)
        };
    }

    private object GetUserLock(Guid userId)
    {
        return _userLocks.GetOrAdd(userId, static _ => new object());
    }

    private string GetSettingsDirectory()
    {
        return Path.Combine(_dataFolderProvider(), "user-settings");
    }

    private string GetSettingsPath(Guid userId)
    {
        return Path.Combine(
            GetSettingsDirectory(),
            string.Concat(userId.ToString("N"), ".json"));
    }

    private void QuarantineCorruptFile(string path, JsonException exception)
    {
        _logger.LogWarning(exception, "Corrupt Better MusicDisplay settings file detected: {Path}", path);
        try
        {
            var quarantinePath = string.Concat(
                path,
                ".corrupt-",
                DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            File.Move(path, quarantinePath, overwrite: true);
        }
        catch (IOException quarantineException)
        {
            _logger.LogWarning(quarantineException, "Failed to quarantine corrupt settings file {Path}", path);
        }
        catch (UnauthorizedAccessException quarantineException)
        {
            _logger.LogWarning(quarantineException, "Access denied quarantining settings file {Path}", path);
        }
    }

    private static void TryDeleteTemporaryFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // A stale temporary file is safer than corrupting the target settings file.
        }
        catch (UnauthorizedAccessException)
        {
            // A stale temporary file is safer than corrupting the target settings file.
        }
    }
}
