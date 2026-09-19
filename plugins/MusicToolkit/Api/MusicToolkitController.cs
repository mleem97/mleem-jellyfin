using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MusicToolkit.Api.Models;
using Jellyfin.Plugin.MusicToolkit.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.MusicToolkit.Api;

/// <summary>
/// Admin REST API for filename hygiene and deduplication.
/// </summary>
[ApiController]
[Route("MusicToolkit")]
[Authorize(Policy = "RequiresElevation")]
public sealed class MusicToolkitController : ControllerBase
{
    private readonly ILibraryManager _libraryManager;
    private readonly AudioHashService _hashService;
    private readonly SafeRenameService _renameService;
    private readonly GracenoteClient _gracenoteClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="MusicToolkitController"/> class.
    /// </summary>
    public MusicToolkitController(
        ILibraryManager libraryManager,
        AudioHashService hashService,
        SafeRenameService renameService,
        GracenoteClient gracenoteClient)
    {
        _libraryManager = libraryManager;
        _hashService = hashService;
        _renameService = renameService;
        _gracenoteClient = gracenoteClient;
    }

    /// <summary>
    /// Dry-run: analyzes the music library for hash prefixes and audio duplicates.
    /// </summary>
    /// <param name="musicRoot">Optional music root override.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Rename preview rows.</returns>
    [HttpGet("DryRun")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [SuppressMessage("Security", "CA3003:Review code for file path injection vulnerabilities", Justification = "Admin-only endpoint (RequiresElevation); musicRoot defaults to the server-side library path.")]
    public async Task<ActionResult<IReadOnlyList<RenamePreviewDto>>> DryRun(
        [FromQuery] string? musicRoot = null,
        CancellationToken cancellationToken = default)
    {
        var root = string.IsNullOrWhiteSpace(musicRoot)
            ? FindMusicRoot()
            : musicRoot;

        if (string.IsNullOrWhiteSpace(root) || !System.IO.Directory.Exists(root))
        {
            return BadRequest("Music root not found. Pass ?musicRoot=/path/to/music.");
        }

        var files = System.IO.Directory.EnumerateFiles(root, "*.*", System.IO.SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".flac", StringComparison.OrdinalIgnoreCase)
                || f.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase)
                || f.EndsWith(".m4a", StringComparison.OrdinalIgnoreCase)
                || f.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase)
                || f.EndsWith(".opus", StringComparison.OrdinalIgnoreCase)
                || f.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var previews = new List<RenamePreviewDto>();
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var parsed = FilenameCleaner.Parse(System.IO.Path.GetFileName(file));
            var dir = System.IO.Path.GetDirectoryName(file) ?? root;
            var newName = parsed.CleanFileName + System.IO.Path.GetExtension(file).ToLowerInvariant();
            var newPath = System.IO.Path.Combine(dir, newName);
            var confidence = ComputeConfidence(System.IO.Path.GetFileName(file), parsed);
            previews.Add(new RenamePreviewDto
            {
                OldPath = file,
                NewPath = newPath,
                Status = string.Equals(file, newPath, StringComparison.OrdinalIgnoreCase) ? "Skip" : "Rename",
                Confidence = confidence,
                Artist = parsed.Artist,
                Title = parsed.Title,
                TrackNumber = parsed.Track,
            });
        }

        await Task.CompletedTask.ConfigureAwait(false);
        return Ok(previews);
    }

    /// <summary>
    /// Applies a rename preview.
    /// </summary>
    /// <param name="request">Items to rename.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Execution summary.</returns>
    [HttpPost("ExecuteRename")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<RenameExecutionDto>> ExecuteRename(
        [FromBody] RenameExecutionDto request,
        CancellationToken cancellationToken = default)
    {
        var result = new RenameExecutionDto();
        foreach (var item in request.Items.Where(i => string.Equals(i.Status, "Rename", StringComparison.OrdinalIgnoreCase)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var ok = await _renameService.RenameAsync(item.OldPath, item.NewPath, cancellationToken).ConfigureAwait(false);
                if (ok)
                {
                    result.Renamed++;
                }
                else
                {
                    result.Skipped++;
                }

                result.Items.Add(item);
            }
            catch (Exception ex)
            {
                result.Skipped++;
                result.Errors.Add(item.OldPath + ": " + ex.Message);
            }
        }

        return Ok(result);
    }

    /// <summary>
    /// Moves identified duplicates into the quarantine folder.
    /// </summary>
    /// <param name="musicRoot">Music root.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Duplicate groups with quarantine destinations.</returns>
    [HttpPost("Deduplicate")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [SuppressMessage("Security", "CA3003:Review code for file path injection vulnerabilities", Justification = "Admin-only endpoint (RequiresElevation); musicRoot defaults to the server-side library path.")]
    public async Task<ActionResult<IReadOnlyList<DuplicateGroupDto>>> Deduplicate(
        [FromQuery] string? musicRoot = null,
        CancellationToken cancellationToken = default)
    {
        var config = Plugin.Instance?.Configuration;
        var root = string.IsNullOrWhiteSpace(musicRoot) ? FindMusicRoot() : musicRoot;
        if (string.IsNullOrWhiteSpace(root) || !System.IO.Directory.Exists(root))
        {
            return BadRequest("Music root not found.");
        }

        var quarantine = System.IO.Path.Combine(root, config?.QuarantineFolder ?? "_duplicates");
        System.IO.Directory.CreateDirectory(quarantine);

        var files = System.IO.Directory.EnumerateFiles(root, "*.*", System.IO.SearchOption.AllDirectories)
            .Where(f => !f.StartsWith(quarantine, StringComparison.OrdinalIgnoreCase))
            .Where(f => f.EndsWith(".flac", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".m4a", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var groups = await _hashService.FindDuplicatesAsync(files, config?.PreferredCodec ?? "flac", cancellationToken).ConfigureAwait(false);
        var dto = new List<DuplicateGroupDto>();
        foreach (var g in groups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var group = new DuplicateGroupDto
            {
                AudioHash = g.AudioHash,
                MasterPath = g.MasterPath,
                MasterScore = g.MasterScore,
                Codec = System.IO.Path.GetExtension(g.MasterPath).TrimStart('.'),
            };

            foreach (var dup in g.DuplicatePaths)
            {
                var dest = System.IO.Path.Combine(quarantine, System.IO.Path.GetFileName(dup));
                dest = DedupPath(dest);
                await _renameService.RenameAsync(dup, dest, cancellationToken).ConfigureAwait(false);
                group.DuplicatePaths.Add(dest);
            }

            dto.Add(group);
        }

        return Ok(dto);
    }

    /// <summary>
    /// Validates the configured Gracenote client id by test-registering a user.
    /// </summary>
    /// <param name="clientId">Optional client id override; falls back to configuration.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Ok when registration succeeded.</returns>
    [HttpPost("TestGracenote")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult> TestGracenote(
        [FromQuery] string? clientId = null,
        CancellationToken cancellationToken = default)
    {
        var config = Plugin.Instance?.Configuration;
        var id = string.IsNullOrWhiteSpace(clientId) ? config?.GracenoteClientId : clientId;
        if (string.IsNullOrWhiteSpace(id))
        {
            return BadRequest("No Gracenote client id configured. Pass ?clientId=...");
        }

        try
        {
            var user = await _gracenoteClient.RegisterAsync(id, cancellationToken).ConfigureAwait(false);
            if (config is not null)
            {
                config.GracenoteClientId = id;
                config.GracenoteUserId = user;
                Plugin.Instance!.UpdateConfiguration(config);
            }

            return Ok(new { registered = true, userIdLength = user.Length });
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { registered = false, error = ex.Message });
        }
    }

    private static int ComputeConfidence(string original, ParsedName parsed)
    {
        var score = 50;
        if (System.Text.RegularExpressions.Regex.IsMatch(original, "^[a-f0-9]{8,32}__", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        {
            score += 30;
        }

        if (parsed.Track.HasValue)
        {
            score += 10;
        }

        if (!string.IsNullOrWhiteSpace(parsed.Mix))
        {
            score += 5;
        }

        return Math.Min(100, score);
    }

    [SuppressMessage("Security", "CA3003:Review code for file path injection vulnerabilities", Justification = "Admin-only endpoint (RequiresElevation); quarantine path is derived server-side.")]
    private static string DedupPath(string dest)
    {
        if (!System.IO.File.Exists(dest))
        {
            return dest;
        }

        var dir = System.IO.Path.GetDirectoryName(dest) ?? string.Empty;
        var stem = System.IO.Path.GetFileNameWithoutExtension(dest);
        var ext = System.IO.Path.GetExtension(dest);
        for (var i = 1; i < 1000; i++)
        {
            var candidate = System.IO.Path.Combine(dir, stem + " (" + i + ")" + ext);
            if (!System.IO.File.Exists(candidate))
            {
                return candidate;
            }
        }

        return dest;
    }

    private string? FindMusicRoot()
    {
        try
        {
            var query = new InternalItemsQuery
            {
                IncludeItemTypes = new[] { BaseItemKind.Audio },
                Recursive = true,
            };

            foreach (var item in _libraryManager.GetItemList(query))
            {
                var dir = System.IO.Path.GetDirectoryName(item.Path);
                if (!string.IsNullOrWhiteSpace(dir) && System.IO.Directory.Exists(dir))
                {
                    return dir;
                }
            }
        }
        catch
        {
            // Best effort: fall back to null and let the caller report the error.
        }

        return null;
    }
}
