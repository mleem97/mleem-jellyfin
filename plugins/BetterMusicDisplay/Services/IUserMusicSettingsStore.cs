using System;
using Jellyfin.Plugin.BetterMusicDisplay.Configuration;

namespace Jellyfin.Plugin.BetterMusicDisplay.Services;

/// <summary>
/// Persists Better MusicDisplay settings per Jellyfin user.
/// </summary>
public interface IUserMusicSettingsStore
{
    /// <summary>
    /// Gets settings for a user.
    /// </summary>
    UserMusicViewSettings Get(Guid userId);

    /// <summary>
    /// Validates and atomically saves settings for a user.
    /// </summary>
    UserMusicViewSettings Save(Guid userId, UserMusicViewSettings settings);

    /// <summary>
    /// Deletes persisted settings for a user.
    /// </summary>
    bool Delete(Guid userId);
}
