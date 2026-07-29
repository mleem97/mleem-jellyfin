using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.BetterMusicDisplay.Models;

/// <summary>
/// Defines a bounded album query.
/// </summary>
public sealed class AlbumQueryRequest
{
    /// <summary>
    /// Gets or sets the underlying Jellyfin start index.
    /// </summary>
    public int StartIndex { get; set; }

    /// <summary>
    /// Gets or sets the requested page size.
    /// </summary>
    public int Limit { get; set; } = 100;

    /// <summary>
    /// Gets or sets an optional music-library parent id.
    /// </summary>
    public Guid? ParentId { get; set; }

    /// <summary>
    /// Gets or sets the search term.
    /// </summary>
    public string SearchTerm { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the sort key.
    /// </summary>
    public string SortBy { get; set; } = "SortName";

    /// <summary>
    /// Gets or sets the sort order.
    /// </summary>
    public string SortOrder { get; set; } = "Ascending";

    /// <summary>
    /// Gets or sets an optional favorite filter.
    /// </summary>
    public bool? IsFavorite { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether only albums without a primary image are returned.
    /// </summary>
    public bool MissingCover { get; set; }

    /// <summary>
    /// Gets or sets an optional genre filter.
    /// </summary>
    public string Genre { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets an optional production-year filter.
    /// </summary>
    public int? Year { get; set; }

    /// <summary>
    /// Gets or sets optional response fields.
    /// </summary>
    public string Fields { get; set; } = "Genres,DateCreated";

    /// <summary>
    /// Creates a validated request with bounded values.
    /// </summary>
    /// <returns>A normalized request.</returns>
    public AlbumQueryRequest Normalize()
    {
        var normalizedSortBy = SortBy?.Trim() ?? string.Empty;
        var allowedSortKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SortName",
            "AlbumArtist",
            "ProductionYear",
            "DateCreated"
        };
        if (!allowedSortKeys.Contains(normalizedSortBy))
        {
            normalizedSortBy = "SortName";
        }

        var normalizedOrder = string.Equals(
            SortOrder,
            "Descending",
            StringComparison.OrdinalIgnoreCase)
            ? "Descending"
            : "Ascending";

        var normalizedFields = string.Join(
            ',',
            (Fields ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(field => string.Equals(field, "Genres", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(field, "DateCreated", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase));

        return new AlbumQueryRequest
        {
            StartIndex = Math.Max(0, StartIndex),
            Limit = Math.Clamp(Limit, 1, 200),
            ParentId = ParentId is { } parentId && parentId != Guid.Empty ? parentId : null,
            SearchTerm = (SearchTerm ?? string.Empty).Trim(),
            SortBy = normalizedSortBy,
            SortOrder = normalizedOrder,
            IsFavorite = IsFavorite,
            MissingCover = MissingCover,
            Genre = (Genre ?? string.Empty).Trim(),
            Year = Year is >= 1000 and <= 9999 ? Year : null,
            Fields = normalizedFields
        };
    }

    /// <summary>
    /// Determines whether a response field is enabled.
    /// </summary>
    /// <param name="field">Field name.</param>
    /// <returns>Whether the field should be included.</returns>
    public bool IncludesField(string field)
    {
        return Fields
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains(field, StringComparer.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Contains one bounded page of music albums.
/// </summary>
public sealed class AlbumQueryPage
{
    /// <summary>
    /// Gets or sets the requested start index.
    /// </summary>
    public int StartIndex { get; set; }

    /// <summary>
    /// Gets or sets the next underlying start index, when more items may exist.
    /// </summary>
    public int? NextStartIndex { get; set; }

    /// <summary>
    /// Gets or sets the total count before the post-query missing-cover filter.
    /// </summary>
    public int TotalRecordCount { get; set; }

    /// <summary>
    /// Gets or sets the exact filtered total when it can be determined without an unbounded scan.
    /// </summary>
    public int? FilteredTotalRecordCount { get; set; }

    /// <summary>
    /// Gets or sets the number of underlying rows inspected.
    /// </summary>
    public int ScannedCount { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether another bounded request may return more albums.
    /// </summary>
    public bool HasMore { get; set; }

    /// <summary>
    /// Gets or sets the albums.
    /// </summary>
    public IReadOnlyList<AlbumListItem> Items { get; set; } = Array.Empty<AlbumListItem>();
}

/// <summary>
/// Minimal album list item used by the Better MusicDisplay grid.
/// </summary>
public sealed class AlbumListItem
{
    /// <summary>
    /// Gets or sets the Jellyfin item id.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the album title.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the album artist.
    /// </summary>
    public string AlbumArtist { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the production year.
    /// </summary>
    public int? ProductionYear { get; set; }

    /// <summary>
    /// Gets or sets the creation date when requested.
    /// </summary>
    public DateTime? DateCreated { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether a primary image exists.
    /// </summary>
    public bool HasPrimaryImage { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the album is a favorite for the current user.
    /// </summary>
    public bool IsFavorite { get; set; }

    /// <summary>
    /// Gets or sets genres when requested.
    /// </summary>
    public IReadOnlyList<string> Genres { get; set; } = Array.Empty<string>();
}
