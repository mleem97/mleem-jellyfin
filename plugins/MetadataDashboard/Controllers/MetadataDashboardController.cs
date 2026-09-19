using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MetadataDashboard.Services;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MetadataDashboard.Controllers;

/// <summary>
/// API controller exposing metadata audit, search, and update endpoints.
/// </summary>
[ApiController]
[Authorize]
[Route("Plugins/MetadataDashboard")]
public sealed class MetadataDashboardController : ControllerBase
{
    private readonly MetadataAuditService _auditService;
    private readonly MusicBrainzLookupService _musicBrainzService;
    private readonly CoverArtArchiveService _coverArtService;
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<MetadataDashboardController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="MetadataDashboardController"/> class.
    /// </summary>
    public MetadataDashboardController(
        MetadataAuditService auditService,
        MusicBrainzLookupService musicBrainzService,
        CoverArtArchiveService coverArtService,
        ILibraryManager libraryManager,
        ILogger<MetadataDashboardController> logger)
    {
        _auditService = auditService;
        _musicBrainzService = musicBrainzService;
        _coverArtService = coverArtService;
        _libraryManager = libraryManager;
        _logger = logger;
    }

    /// <summary>
    /// Returns current health and diagnostic status of all metadata providers.
    /// </summary>
    [HttpGet("Status")]
    public ActionResult<object> GetStatus()
    {
        return Ok(new
        {
            Status = "OK",
            Version = "1.0.0.0",
            Providers = new[]
            {
                new { Name = "CoverArtArchive", Active = Plugin.Instance?.Configuration.EnableCoverArtArchiveProvider ?? true, Description = Plugin.Instance?.Configuration.CoverArtArchiveMirror ?? "https://coverartarchive.org" },
                new { Name = "MusicBrainz", Active = true, Description = Plugin.Instance?.Configuration.MusicBrainzMirror ?? "https://musicbrainz.org" },
                new { Name = "Spotify", Active = true, Description = "Spotify Web API Metadata & Artwork" },
                new { Name = "Gracenote", Active = true, Description = "Gracenote MusicID GMD v2/v3" }
            }
        });
    }

    /// <summary>
    /// Runs a comprehensive metadata health audit across music libraries.
    /// </summary>
    [HttpGet("Audit")]
    public async Task<ActionResult<MetadataAuditReport>> RunAudit(CancellationToken cancellationToken)
    {
        var report = await _auditService.RunAuditAsync(cancellationToken).ConfigureAwait(false);
        return Ok(report);
    }

    /// <summary>
    /// Searches MusicBrainz for releases.
    /// </summary>
    [HttpGet("MusicBrainz/Search")]
    public async Task<ActionResult<List<MusicBrainzReleaseMatch>>> SearchMusicBrainz(
        [FromQuery] string query,
        [FromQuery] string? artist,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return BadRequest(new { Message = "Query parameter is required." });
        }

        var results = await _musicBrainzService.SearchReleasesAsync(query, artist, cancellationToken).ConfigureAwait(false);
        return Ok(results);
    }

    /// <summary>
    /// Retrieves CoverArtArchive artwork listings for a release MBID.
    /// </summary>
    [HttpGet("CoverArtArchive/{releaseId}")]
    public async Task<ActionResult<CoverArtArchiveResult>> GetCoverArt(
        string releaseId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(releaseId))
        {
            return BadRequest(new { Message = "ReleaseId is required." });
        }

        var result = await _coverArtService.GetReleaseCoverArtAsync(releaseId, cancellationToken).ConfigureAwait(false);
        if (result == null)
        {
            return NotFound(new { Message = $"No cover art found for release {releaseId}." });
        }

        return Ok(result);
    }

    /// <summary>
    /// Applies metadata and provider IDs to a library item.
    /// </summary>
    [HttpPost("Apply")]
    public async Task<ActionResult<object>> ApplyMetadata([FromBody] ApplyMetadataRequest request)
    {
        if (request == null || request.ItemId == Guid.Empty)
        {
            return BadRequest(new { Message = "Valid ItemId is required." });
        }

        var item = _libraryManager.GetItemById(request.ItemId);
        if (item == null)
        {
            return NotFound(new { Message = $"Item {request.ItemId} not found." });
        }

        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            item.Name = request.Name.Trim();
        }

        if (request.Year.HasValue && request.Year > 0)
        {
            item.ProductionYear = request.Year;
        }

        if (item is MusicAlbum album)
        {
            if (!string.IsNullOrWhiteSpace(request.Artist))
            {
                album.AlbumArtists = new[] { request.Artist.Trim() };
            }

            if (!string.IsNullOrWhiteSpace(request.MusicBrainzId))
            {
                album.SetProviderId("MusicBrainzAlbum", request.MusicBrainzId.Trim());
            }

            if (!string.IsNullOrWhiteSpace(request.SpotifyId))
            {
                album.SetProviderId("Spotify", request.SpotifyId.Trim());
            }

            if (!string.IsNullOrWhiteSpace(request.GracenoteId))
            {
                album.SetProviderId("Gracenote", request.GracenoteId.Trim());
            }
        }
        else if (item is MusicArtist artist)
        {
            if (!string.IsNullOrWhiteSpace(request.MusicBrainzId))
            {
                artist.SetProviderId("MusicBrainzArtist", request.MusicBrainzId.Trim());
            }

            if (!string.IsNullOrWhiteSpace(request.SpotifyId))
            {
                artist.SetProviderId("Spotify", request.SpotifyId.Trim());
            }

            if (!string.IsNullOrWhiteSpace(request.GracenoteId))
            {
                artist.SetProviderId("Gracenote", request.GracenoteId.Trim());
            }
        }

        await _libraryManager.UpdateItemAsync(item, item.GetParent(), ItemUpdateType.MetadataEdit, CancellationToken.None).ConfigureAwait(false);

        return Ok(new
        {
            Success = true,
            ItemId = item.Id,
            Message = "Metadata successfully applied."
        });
    }
}

/// <summary>
/// Request model for applying metadata to an item.
/// </summary>
public sealed class ApplyMetadataRequest
{
    public Guid ItemId { get; set; }
    public string? Name { get; set; }
    public string? Artist { get; set; }
    public int? Year { get; set; }
    public string? MusicBrainzId { get; set; }
    public string? SpotifyId { get; set; }
    public string? GracenoteId { get; set; }
}
