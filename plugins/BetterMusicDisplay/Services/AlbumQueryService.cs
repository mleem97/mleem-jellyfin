using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.BetterMusicDisplay.Models;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;

namespace Jellyfin.Plugin.BetterMusicDisplay.Services;

/// <summary>
/// Queries bounded pages of music albums through Jellyfin's library manager.
/// </summary>
public sealed class AlbumQueryService : IAlbumQueryService
{
    private const int MaximumMissingCoverScanRows = 1000;
    private const int MissingCoverChunkSize = 200;

    private readonly ILibraryManager _libraryManager;
    private readonly IUserManager _userManager;
    private readonly IUserDataManager _userDataManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="AlbumQueryService"/> class.
    /// </summary>
    public AlbumQueryService(
        ILibraryManager libraryManager,
        IUserManager userManager,
        IUserDataManager userDataManager)
    {
        _libraryManager = libraryManager;
        _userManager = userManager;
        _userDataManager = userDataManager;
    }

    /// <inheritdoc />
    public AlbumQueryPage? Query(
        Guid userId,
        AlbumQueryRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var user = _userManager.GetUserById(userId);
        if (user is null)
        {
            return null;
        }

        var normalized = request.Normalize();
        return normalized.MissingCover
            ? QueryMissingCovers(user, normalized, cancellationToken)
            : QueryDirect(user, normalized, cancellationToken);
    }

    private AlbumQueryPage QueryDirect(
        Jellyfin.Database.Implementations.Entities.User user,
        AlbumQueryRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = _libraryManager.GetItemsResult(CreateQuery(
            user,
            request,
            request.StartIndex,
            request.Limit));
        cancellationToken.ThrowIfCancellationRequested();

        var albums = result.Items.OfType<MusicAlbum>().ToArray();
        var items = MapAlbums(albums, user, request);
        var nextIndex = request.StartIndex + result.Items.Count;
        var hasMore = nextIndex < result.TotalRecordCount;
        return new AlbumQueryPage
        {
            StartIndex = request.StartIndex,
            NextStartIndex = hasMore ? nextIndex : null,
            TotalRecordCount = result.TotalRecordCount,
            FilteredTotalRecordCount = result.TotalRecordCount,
            ScannedCount = result.Items.Count,
            HasMore = hasMore,
            Items = items
        };
    }

    private AlbumQueryPage QueryMissingCovers(
        Jellyfin.Database.Implementations.Entities.User user,
        AlbumQueryRequest request,
        CancellationToken cancellationToken)
    {
        var collected = new List<MusicAlbum>(request.Limit);
        var scanIndex = request.StartIndex;
        var scannedCount = 0;
        var totalCount = 0;
        var reachedEnd = false;

        while (collected.Count < request.Limit
            && scannedCount < MaximumMissingCoverScanRows
            && !reachedEnd)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remainingBudget = MaximumMissingCoverScanRows - scannedCount;
            var chunkLimit = Math.Min(MissingCoverChunkSize, remainingBudget);
            var result = _libraryManager.GetItemsResult(CreateQuery(
                user,
                request,
                scanIndex,
                chunkLimit));
            totalCount = result.TotalRecordCount;
            if (result.Items.Count == 0)
            {
                reachedEnd = true;
                break;
            }

            foreach (var item in result.Items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                scanIndex++;
                scannedCount++;
                if (item is MusicAlbum album && !album.HasImage(ImageType.Primary))
                {
                    collected.Add(album);
                    if (collected.Count == request.Limit)
                    {
                        break;
                    }
                }
            }

            reachedEnd = scanIndex >= totalCount;
        }

        var hasMore = !reachedEnd && scanIndex < totalCount;
        return new AlbumQueryPage
        {
            StartIndex = request.StartIndex,
            NextStartIndex = hasMore ? scanIndex : null,
            TotalRecordCount = totalCount,
            FilteredTotalRecordCount = request.StartIndex == 0 && reachedEnd
                ? collected.Count
                : null,
            ScannedCount = scannedCount,
            HasMore = hasMore,
            Items = MapAlbums(collected, user, request)
        };
    }

    private static InternalItemsQuery CreateQuery(
        Jellyfin.Database.Implementations.Entities.User user,
        AlbumQueryRequest request,
        int startIndex,
        int limit)
    {
        return new InternalItemsQuery(user)
        {
            IncludeItemTypes = new[] { BaseItemKind.MusicAlbum },
            Recursive = true,
            StartIndex = startIndex,
            Limit = limit,
            SearchTerm = string.IsNullOrWhiteSpace(request.SearchTerm) ? null : request.SearchTerm,
            IsFavorite = request.IsFavorite,
            Genres = string.IsNullOrWhiteSpace(request.Genre)
                ? Array.Empty<string>()
                : new[] { request.Genre },
            Years = request.Year.HasValue ? new[] { request.Year.Value } : Array.Empty<int>(),
            ParentId = request.ParentId ?? Guid.Empty,
            IsVirtualItem = false,
            EnableTotalRecordCount = true,
            GroupByPresentationUniqueKey = true,
            OrderBy = new[] { (MapSortKey(request.SortBy), MapSortOrder(request.SortOrder)) }
        };
    }

    private IReadOnlyList<AlbumListItem> MapAlbums(
        IReadOnlyList<MusicAlbum> albums,
        Jellyfin.Database.Implementations.Entities.User user,
        AlbumQueryRequest request)
    {
        var baseItems = albums.Cast<BaseItem>().ToArray();
        var userData = _userDataManager.GetUserDataBatch(baseItems, user);
        return albums.Select(album => new AlbumListItem
        {
            Id = album.Id,
            Name = album.Name ?? string.Empty,
            AlbumArtist = album.AlbumArtist ?? string.Empty,
            ProductionYear = album.ProductionYear,
            DateCreated = request.IncludesField("DateCreated") ? album.DateCreated : null,
            HasPrimaryImage = album.HasImage(ImageType.Primary),
            IsFavorite = userData.TryGetValue(album.Id, out var data) && data.IsFavorite,
            Genres = request.IncludesField("Genres")
                ? album.Genres.ToArray()
                : Array.Empty<string>()
        }).ToArray();
    }

    private static ItemSortBy MapSortKey(string sortBy)
    {
        return sortBy.ToLowerInvariant() switch
        {
            "albumartist" => ItemSortBy.AlbumArtist,
            "productionyear" => ItemSortBy.ProductionYear,
            "datecreated" => ItemSortBy.DateCreated,
            _ => ItemSortBy.SortName
        };
    }

    private static SortOrder MapSortOrder(string sortOrder)
    {
        return string.Equals(sortOrder, "Descending", StringComparison.OrdinalIgnoreCase)
            ? SortOrder.Descending
            : SortOrder.Ascending;
    }
}
