using System;
using Jellyfin.Plugin.MusicSuite.Configuration;

namespace Jellyfin.Plugin.MusicSuite.Services;

/// <summary>
/// Persists MusicSuite settings per Jellyfin user.
/// </summary>
public interface IUserMusicSettingsStore
{
    /// <summary>
    /// Gets settings for a user.
    /// </summary>
    /// <param name="userId">Jellyfin user id.</param>
    /// <returns>The persisted settings, or defaults when none exist.</returns>
    UserMusicViewSettings Get(Guid userId);

    /// <summary>
    /// Validates and atomically saves settings for a user.
    /// </summary>
    /// <param name="userId">Jellyfin user id.</param>
    /// <param name="settings">User settings to validate and persist.</param>
    /// <returns>The normalized settings that were persisted.</returns>
    UserMusicViewSettings Save(Guid userId, UserMusicViewSettings settings);

    /// <summary>
    /// Deletes persisted settings for a user.
    /// </summary>
    /// <param name="userId">Jellyfin user id.</param>
    /// <returns>Whether a persisted settings file was deleted.</returns>
    bool Delete(Guid userId);
}
