using System;
using System.Threading;
using Jellyfin.Plugin.BetterMusicDisplay.Models;

namespace Jellyfin.Plugin.BetterMusicDisplay.Services;

/// <summary>
/// Provides bounded, user-aware album queries.
/// </summary>
public interface IAlbumQueryService
{
    /// <summary>
    /// Queries albums visible to one Jellyfin user.
    /// </summary>
    /// <param name="userId">Authenticated Jellyfin user id.</param>
    /// <param name="request">Query request.</param>
    /// <param name="cancellationToken">Request cancellation token.</param>
    /// <returns>A page, or <see langword="null"/> when the user does not exist.</returns>
    AlbumQueryPage? Query(
        Guid userId,
        AlbumQueryRequest request,
        CancellationToken cancellationToken);
}
