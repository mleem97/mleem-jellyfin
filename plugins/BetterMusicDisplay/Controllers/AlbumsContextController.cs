using System;
using System.Linq;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.BetterMusicDisplay.Controllers;

/// <summary>
/// Validates whether a Jellyfin route targets a music library that may host the Albums MVP.
/// </summary>
[ApiController]
[Authorize]
[Route("Plugins/BetterMusicDisplay/Albums/Context")]
public sealed class AlbumsContextController : ControllerBase
{
    private readonly ILibraryManager _libraryManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="AlbumsContextController"/> class.
    /// </summary>
    /// <param name="libraryManager">Jellyfin library manager.</param>
    public AlbumsContextController(ILibraryManager libraryManager)
    {
        _libraryManager = libraryManager;
    }

    /// <summary>
    /// Gets activation context for one library parent.
    /// </summary>
    /// <param name="parentId">Candidate Jellyfin music-library id.</param>
    /// <returns>Activation context.</returns>
    [HttpGet]
    public ActionResult<AlbumsViewContext> GetContext([FromQuery] Guid parentId)
    {
        if (parentId == Guid.Empty)
        {
            return BadRequest("A non-empty parentId is required.");
        }

        var folder = _libraryManager.GetVirtualFolders()
            .FirstOrDefault(candidate => Guid.TryParse(candidate.ItemId, out var itemId)
                && itemId == parentId);
        var isMusicLibrary = folder is not null
            && string.Equals(
                folder.CollectionType?.ToString(),
                "music",
                StringComparison.OrdinalIgnoreCase);
        var configuration = Plugin.Instance?.Configuration;

        return Ok(new AlbumsViewContext
        {
            ParentId = parentId,
            LibraryName = folder?.Name ?? string.Empty,
            IsMusicLibrary = isMusicLibrary,
            Enabled = configuration?.Enabled ?? true,
            EnableFallback = configuration?.EnableFallbackToDefaultJellyfin ?? true,
            BatchSize = Math.Clamp(configuration?.BatchSize ?? 100, 1, 200),
            SearchDebounceMs = Math.Clamp(configuration?.SearchDebounceMs ?? 300, 100, 2000)
        });
    }
}

/// <summary>
/// Describes whether the Albums MVP may activate for a route.
/// </summary>
public sealed class AlbumsViewContext
{
    /// <summary>
    /// Gets or sets the music-library parent id.
    /// </summary>
    public Guid ParentId { get; set; }

    /// <summary>
    /// Gets or sets the library name.
    /// </summary>
    public string LibraryName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the parent is a configured music library.
    /// </summary>
    public bool IsMusicLibrary { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the plugin is globally enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether native fallback is enabled.
    /// </summary>
    public bool EnableFallback { get; set; }

    /// <summary>
    /// Gets or sets the bounded default batch size.
    /// </summary>
    public int BatchSize { get; set; }

    /// <summary>
    /// Gets or sets the search debounce interval.
    /// </summary>
    public int SearchDebounceMs { get; set; }
}
