using System.Threading;
using Jellyfin.Plugin.BetterMusicDisplay.Models;
using Jellyfin.Plugin.BetterMusicDisplay.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.BetterMusicDisplay.Controllers;

/// <summary>
/// Provides bounded album data for the Better MusicDisplay grid.
/// </summary>
[ApiController]
[Authorize]
[Route("Plugins/BetterMusicDisplay/Albums")]
public sealed class AlbumsController : ControllerBase
{
    private readonly IAlbumQueryService _albumQueryService;

    /// <summary>
    /// Initializes a new instance of the <see cref="AlbumsController"/> class.
    /// </summary>
    /// <param name="albumQueryService">Album query service.</param>
    public AlbumsController(IAlbumQueryService albumQueryService)
    {
        _albumQueryService = albumQueryService;
    }

    /// <summary>
    /// Gets a bounded page of albums visible to the authenticated Jellyfin user.
    /// </summary>
    /// <param name="request">Query options.</param>
    /// <param name="cancellationToken">Request cancellation token.</param>
    /// <returns>A bounded album page.</returns>
    [HttpGet]
    public ActionResult<AlbumQueryPage> GetAlbums(
        [FromQuery] AlbumQueryRequest request,
        CancellationToken cancellationToken)
    {
        if (!UserSettingsAccessEvaluator.TryGetUserId(User, out var userId))
        {
            return Forbid();
        }

        var page = _albumQueryService.Query(userId, request, cancellationToken);
        return page is null
            ? NotFound("The authenticated Jellyfin user no longer exists.")
            : Ok(page);
    }
}
