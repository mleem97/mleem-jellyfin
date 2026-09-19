using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MetadataDashboard.Services;

/// <summary>
/// Service inspecting music libraries for missing metadata, artwork, and provider IDs.
/// </summary>
public sealed class MetadataAuditService
{
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<MetadataAuditService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="MetadataAuditService"/> class.
    /// </summary>
    public MetadataAuditService(ILibraryManager libraryManager, ILogger<MetadataAuditService> logger)
    {
        _libraryManager = libraryManager;
        _logger = logger;
    }

    /// <summary>
    /// Audits all audio items and albums in the library for missing metadata.
    /// </summary>
    public Task<MetadataAuditReport> RunAuditAsync(CancellationToken cancellationToken = default)
    {
        var musicFolders = _libraryManager.GetVirtualFolders()
            .Where(f => string.Equals(f.CollectionType?.ToString(), "music", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var query = new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.MusicAlbum, BaseItemKind.MusicArtist },
            Recursive = true,
            EnableTotalRecordCount = true
        };

        var items = _libraryManager.GetItemList(query);

        int totalAlbums = 0;
        int totalArtists = 0;
        int missingCovers = 0;
        int missingMbids = 0;
        int missingSpotify = 0;
        int missingGracenote = 0;
        int missingYear = 0;

        var issuesList = new List<MetadataAuditItemSummary>();

        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            bool hasIssue = false;
            var missingTags = new List<string>();

            if (item is MusicAlbum album)
            {
                totalAlbums++;

                if (!album.HasImage(ImageType.Primary))
                {
                    missingCovers++;
                    missingTags.Add("Cover");
                    hasIssue = true;
                }

                if (string.IsNullOrWhiteSpace(album.GetProviderId("MusicBrainzAlbum")) &&
                    string.IsNullOrWhiteSpace(album.GetProviderId("MusicBrainzReleaseGroup")))
                {
                    missingMbids++;
                    missingTags.Add("MusicBrainz");
                    hasIssue = true;
                }

                if (string.IsNullOrWhiteSpace(album.GetProviderId("Spotify")))
                {
                    missingSpotify++;
                    missingTags.Add("Spotify");
                    hasIssue = true;
                }

                if (string.IsNullOrWhiteSpace(album.GetProviderId("Gracenote")))
                {
                    missingGracenote++;
                    missingTags.Add("Gracenote");
                    hasIssue = true;
                }

                if (!album.ProductionYear.HasValue || album.ProductionYear == 0)
                {
                    missingYear++;
                    missingTags.Add("Jahr");
                    hasIssue = true;
                }

                if (hasIssue && issuesList.Count < 100)
                {
                    issuesList.Add(new MetadataAuditItemSummary
                    {
                        Id = album.Id,
                        Name = album.Name,
                        Artist = album.AlbumArtist ?? "Unbekannt",
                        Type = "Album",
                        MissingFields = missingTags,
                        HasPrimaryImage = album.HasImage(ImageType.Primary),
                        Year = album.ProductionYear,
                        MusicBrainzId = album.GetProviderId("MusicBrainzAlbum"),
                        SpotifyId = album.GetProviderId("Spotify"),
                        GracenoteId = album.GetProviderId("Gracenote")
                    });
                }
            }
            else if (item is MusicArtist artist)
            {
                totalArtists++;

                if (!artist.HasImage(ImageType.Primary))
                {
                    missingCovers++;
                    missingTags.Add("Cover");
                    hasIssue = true;
                }

                if (string.IsNullOrWhiteSpace(artist.GetProviderId("MusicBrainzArtist")))
                {
                    missingMbids++;
                    missingTags.Add("MusicBrainz");
                    hasIssue = true;
                }

                if (string.IsNullOrWhiteSpace(artist.GetProviderId("Spotify")))
                {
                    missingSpotify++;
                    missingTags.Add("Spotify");
                    hasIssue = true;
                }

                if (hasIssue && issuesList.Count < 100)
                {
                    issuesList.Add(new MetadataAuditItemSummary
                    {
                        Id = artist.Id,
                        Name = artist.Name,
                        Artist = artist.Name,
                        Type = "Künstler",
                        MissingFields = missingTags,
                        HasPrimaryImage = artist.HasImage(ImageType.Primary),
                        MusicBrainzId = artist.GetProviderId("MusicBrainzArtist"),
                        SpotifyId = artist.GetProviderId("Spotify"),
                        GracenoteId = artist.GetProviderId("Gracenote")
                    });
                }
            }
        }

        int totalCount = totalAlbums + totalArtists;
        int totalDeficiencies = missingCovers + missingMbids + missingSpotify + missingYear;
        int maxPossibleDeficiencies = totalCount * 4;

        int healthScore = 100;
        if (maxPossibleDeficiencies > 0)
        {
            healthScore = Math.Clamp(100 - (int)((double)totalDeficiencies / maxPossibleDeficiencies * 100.0), 0, 100);
        }

        return Task.FromResult(new MetadataAuditReport
        {
            TotalAlbums = totalAlbums,
            TotalArtists = totalArtists,
            HealthScore = healthScore,
            MissingCoversCount = missingCovers,
            MissingMusicBrainzCount = missingMbids,
            MissingSpotifyCount = missingSpotify,
            MissingGracenoteCount = missingGracenote,
            MissingYearCount = missingYear,
            ItemsWithIssues = issuesList
        });
    }
}

public sealed class MetadataAuditReport
{
    public int TotalAlbums { get; set; }
    public int TotalArtists { get; set; }
    public int HealthScore { get; set; }
    public int MissingCoversCount { get; set; }
    public int MissingMusicBrainzCount { get; set; }
    public int MissingSpotifyCount { get; set; }
    public int MissingGracenoteCount { get; set; }
    public int MissingYearCount { get; set; }
    public List<MetadataAuditItemSummary> ItemsWithIssues { get; set; } = new();
}

public sealed class MetadataAuditItemSummary
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public List<string> MissingFields { get; set; } = new();
    public bool HasPrimaryImage { get; set; }
    public int? Year { get; set; }
    public string? MusicBrainzId { get; set; }
    public string? SpotifyId { get; set; }
    public string? GracenoteId { get; set; }
}
