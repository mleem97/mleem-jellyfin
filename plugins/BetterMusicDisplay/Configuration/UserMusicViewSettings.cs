using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.BetterMusicDisplay.Configuration;

/// <summary>
/// Per-user music view settings.
/// </summary>
public class UserMusicViewSettings
{
    /// <summary>
    /// Gets or sets the persisted settings schema version.
    /// </summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>
    /// Gets or sets the Jellyfin user id.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Gets or sets the landing page.
    /// </summary>
    public string LandingPage { get; set; } = "Suggestions";

    /// <summary>
    /// Gets or sets the album layout.
    /// </summary>
    public string AlbumLayout { get; set; } = "Grid";

    /// <summary>
    /// Gets or sets the artist layout.
    /// </summary>
    public string ArtistLayout { get; set; } = "Grid";

    /// <summary>
    /// Gets or sets the song layout.
    /// </summary>
    public string SongLayout { get; set; } = "VirtualTable";

    /// <summary>
    /// Gets or sets the tile size.
    /// </summary>
    public string TileSize { get; set; } = "Medium";

    /// <summary>
    /// Gets or sets a value indicating whether missing covers are highlighted.
    /// </summary>
    public bool MarkMissingCovers { get; set; } = true;

    /// <summary>
    /// Gets or sets sort preferences by view key.
    /// </summary>
    public Dictionary<string, string> SortPreferences { get; set; } = new();

    /// <summary>
    /// Gets or sets enabled sections by section key.
    /// </summary>
    public Dictionary<string, bool> EnabledSections { get; set; } = new();
}
