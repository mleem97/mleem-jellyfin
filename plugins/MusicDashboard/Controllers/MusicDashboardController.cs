using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.MusicDashboard.Controllers;

/// <summary>
/// Music Dashboard API endpoints.
/// </summary>
[ApiController]
[Route("Plugins/MusicDashboard")]
public class MusicDashboardController : ControllerBase
{
    /// <summary>
    /// Gets a music library overview for the plugin page.
    /// </summary>
    /// <returns>Music library overview.</returns>
    [HttpGet("Overview")]
    public ActionResult<MusicDashboardOverview> GetOverview()
    {
        var libraryManager = HttpContext.RequestServices.GetService<ILibraryManager>();
        if (libraryManager is null)
        {
            return StatusCode(500, "ILibraryManager service is not available.");
        }

        var libraries = libraryManager.GetVirtualFolders()
            .Select(v => new MusicLibraryOverview
            {
                Name = string.IsNullOrWhiteSpace(v.Name) ? "Music" : v.Name,
                Type = NormalizeCollectionType(v.CollectionType?.ToString()),
                Paths = (v.Locations ?? Array.Empty<string>())
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToArray()
            })
            .Where(library => library.Type == "music")
            .OrderBy(library => library.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Ok(new MusicDashboardOverview
        {
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            MusicLibraryCount = libraries.Length,
            MusicPathCount = libraries.Sum(library => library.Paths.Count),
            Libraries = libraries
        });
    }

    private static string NormalizeCollectionType(string? collectionType)
    {
        if (string.IsNullOrWhiteSpace(collectionType))
        {
            return "unknown";
        }

        return collectionType.Trim().ToLowerInvariant() switch
        {
            "music" => "music",
            _ => "other"
        };
    }
}

/// <summary>
/// Music dashboard overview response.
/// </summary>
public class MusicDashboardOverview
{
    /// <summary>
    /// Gets or sets the generation timestamp.
    /// </summary>
    public DateTimeOffset GeneratedAtUtc { get; set; }

    /// <summary>
    /// Gets or sets the number of music libraries.
    /// </summary>
    public int MusicLibraryCount { get; set; }

    /// <summary>
    /// Gets or sets the number of music library paths.
    /// </summary>
    public int MusicPathCount { get; set; }

    /// <summary>
    /// Gets or sets the detected music libraries.
    /// </summary>
    public IReadOnlyList<MusicLibraryOverview> Libraries { get; set; } = Array.Empty<MusicLibraryOverview>();
}

/// <summary>
/// Music library overview entry.
/// </summary>
public class MusicLibraryOverview
{
    /// <summary>
    /// Gets or sets the library name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the normalized collection type.
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the library paths.
    /// </summary>
    public IReadOnlyList<string> Paths { get; set; } = Array.Empty<string>();
}
