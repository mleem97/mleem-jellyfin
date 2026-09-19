using System;
using System.Collections.Generic;
using Jellyfin.Plugin.MetadataDashboard.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.MetadataDashboard;

/// <summary>
/// The main plugin class for MetadataDashboard.
/// </summary>
public sealed class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <summary>
    /// Gets the current plugin instance.
    /// </summary>
    public static Plugin? Instance { get; private set; }

    /// <inheritdoc />
    public override string Name => "MetadataDashboard";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("c70012b6-0fc0-479e-a814-b5a09fcdd799");

    /// <inheritdoc />
    public override string Description => "Comprehensive metadata management studio for music: cross-reference Spotify, Gracenote, MusicBrainz, and CoverArtArchive.";

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return new[]
        {
            new PluginPageInfo
            {
                Name = "MetadataDashboard",
                EmbeddedResourcePath = $"{GetType().Namespace}.Web.config.html"
            },
            new PluginPageInfo
            {
                Name = "metadata-dashboard",
                EmbeddedResourcePath = $"{GetType().Namespace}.Web.metadata-dashboard.html",
                EnableInMainMenu = true,
                MenuIcon = "edit_note",
                MenuSection = "admin"
            },
            new PluginPageInfo
            {
                Name = "metadata-dashboard.js",
                EmbeddedResourcePath = $"{GetType().Namespace}.Web.metadata-dashboard.js"
            },
            new PluginPageInfo
            {
                Name = "metadata-dashboard.css",
                EmbeddedResourcePath = $"{GetType().Namespace}.Web.metadata-dashboard.css"
            }
        };
    }
}
