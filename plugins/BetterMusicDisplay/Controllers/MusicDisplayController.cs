using System;
using System.Linq;
using Jellyfin.Plugin.BetterMusicDisplay.Configuration;
using Jellyfin.Plugin.BetterMusicDisplay.Services;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.BetterMusicDisplay.Controllers;

/// <summary>
/// Better MusicDisplay endpoints.
/// </summary>
[ApiController]
[Authorize]
[Route("Plugins/BetterMusicDisplay")]
public class MusicDisplayController : ControllerBase
{
    /// <summary>
    /// Gets the current music library overview.
    /// </summary>
    /// <returns>Music overview.</returns>
    [HttpGet("Overview")]
    public ActionResult<MusicDisplayOverview> GetOverview()
    {
        var libraryManager = HttpContext.RequestServices.GetService<ILibraryManager>();
        if (libraryManager is null)
        {
            return StatusCode(500, "ILibraryManager service is not available.");
        }

        var musicLibraries = libraryManager.GetVirtualFolders()
            .Where(folder => string.Equals(
                folder.CollectionType?.ToString(),
                "music",
                StringComparison.OrdinalIgnoreCase))
            .Select(folder => new MusicLibrarySummary
            {
                Name = string.IsNullOrWhiteSpace(folder.Name) ? "Music" : folder.Name,
                PathCount = folder.Locations?.Length ?? 0
            })
            .OrderBy(folder => folder.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Ok(new MusicDisplayOverview
        {
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            LibraryCount = musicLibraries.Length,
            Libraries = musicLibraries
        });
    }

    /// <summary>
    /// Gets persisted Better MusicDisplay settings for one user.
    /// </summary>
    /// <param name="userId">Jellyfin user id.</param>
    /// <returns>User settings response.</returns>
    [HttpGet("Users/{userId:guid}/Settings")]
    public ActionResult<UserMusicSettingsResponse> GetUserSettings(Guid userId)
    {
        if (!UserSettingsAccessEvaluator.CanAccess(User, userId))
        {
            return Forbid();
        }

        return Ok(new UserMusicSettingsResponse
        {
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            UserId = userId,
            CanCustomize = Plugin.Instance?.Configuration.AllowUserCustomization ?? true,
            Settings = UserMusicSettingsStore.Get(userId)
        });
    }

    /// <summary>
    /// Saves Better MusicDisplay settings for one user.
    /// </summary>
    /// <param name="userId">Jellyfin user id.</param>
    /// <param name="settings">User settings.</param>
    /// <returns>Saved user settings response.</returns>
    [HttpPut("Users/{userId:guid}/Settings")]
    public ActionResult<UserMusicSettingsResponse> SaveUserSettings(
        Guid userId,
        [FromBody] UserMusicViewSettings settings)
    {
        if (!UserSettingsAccessEvaluator.CanAccess(User, userId))
        {
            return Forbid();
        }

        if (!(Plugin.Instance?.Configuration.AllowUserCustomization ?? true))
        {
            return StatusCode(
                403,
                "User customization is disabled by the Better MusicDisplay configuration.");
        }

        var saved = UserMusicSettingsStore.Save(userId, settings);
        return Ok(new UserMusicSettingsResponse
        {
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            UserId = userId,
            CanCustomize = true,
            Settings = saved
        });
    }

    /// <summary>
    /// Resets Better MusicDisplay settings for one user.
    /// </summary>
    /// <param name="userId">Jellyfin user id.</param>
    /// <returns>Reset response.</returns>
    [HttpDelete("Users/{userId:guid}/Settings")]
    public ActionResult<UserMusicSettingsResetResponse> ResetUserSettings(Guid userId)
    {
        if (!UserSettingsAccessEvaluator.CanAccess(User, userId))
        {
            return Forbid();
        }

        if (!(Plugin.Instance?.Configuration.AllowUserCustomization ?? true))
        {
            return StatusCode(
                403,
                "User customization is disabled by the Better MusicDisplay configuration.");
        }

        var deleted = UserMusicSettingsStore.Delete(userId);
        return Ok(new UserMusicSettingsResetResponse
        {
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            UserId = userId,
            Deleted = deleted,
            Settings = UserMusicSettingsStore.Get(userId)
        });
    }
}

/// <summary>
/// Music display overview response.
/// </summary>
public class MusicDisplayOverview
{
    /// <summary>
    /// Gets or sets generation timestamp.
    /// </summary>
    public DateTimeOffset GeneratedAtUtc { get; set; }

    /// <summary>
    /// Gets or sets music library count.
    /// </summary>
    public int LibraryCount { get; set; }

    /// <summary>
    /// Gets or sets libraries.
    /// </summary>
    public MusicLibrarySummary[] Libraries { get; set; } = Array.Empty<MusicLibrarySummary>();
}

/// <summary>
/// Better MusicDisplay user settings response.
/// </summary>
public class UserMusicSettingsResponse
{
    /// <summary>
    /// Gets or sets generation timestamp.
    /// </summary>
    public DateTimeOffset GeneratedAtUtc { get; set; }

    /// <summary>
    /// Gets or sets Jellyfin user id.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this user may customize the enhanced music view.
    /// </summary>
    public bool CanCustomize { get; set; }

    /// <summary>
    /// Gets or sets user settings.
    /// </summary>
    public UserMusicViewSettings Settings { get; set; } = new();
}

/// <summary>
/// Better MusicDisplay user settings reset response.
/// </summary>
public class UserMusicSettingsResetResponse
{
    /// <summary>
    /// Gets or sets generation timestamp.
    /// </summary>
    public DateTimeOffset GeneratedAtUtc { get; set; }

    /// <summary>
    /// Gets or sets Jellyfin user id.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether a persisted settings file was deleted.
    /// </summary>
    public bool Deleted { get; set; }

    /// <summary>
    /// Gets or sets reset user settings.
    /// </summary>
    public UserMusicViewSettings Settings { get; set; } = new();
}

/// <summary>
/// Music library summary.
/// </summary>
public class MusicLibrarySummary
{
    /// <summary>
    /// Gets or sets library name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets path count.
    /// </summary>
    public int PathCount { get; set; }
}
