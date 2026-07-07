using System;
using System.Linq;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.BetterMusicDisplay.Controllers;

/// <summary>
/// Better MusicDisplay endpoints.
/// </summary>
[ApiController]
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
            .Where(folder => string.Equals(folder.CollectionType?.ToString(), "music", StringComparison.OrdinalIgnoreCase))
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
