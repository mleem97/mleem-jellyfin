using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.MusicSuite.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.MusicSuite;

/// <summary>
/// Main plugin entry point for MusicSuite.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Application paths.</param>
    /// <param name="xmlSerializer">XML serializer.</param>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <inheritdoc />
    public override string Name => "MusicSuite";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("6b6c96ac-3f60-4ac3-8b8a-0d0cb8acb9dd");

    /// <summary>
    /// Gets the current plugin instance.
    /// </summary>
    public static Plugin? Instance { get; private set; }

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return new[]
        {
            new PluginPageInfo
            {
                Name = "MusicSuite",
                EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Web.config.html", GetType().Namespace),
                EnableInMainMenu = true,
                MenuSection = "server",
                MenuIcon = "audiotrack",
                DisplayName = "MusicSuite"
            },
            new PluginPageInfo
            {
                Name = "spotify-dashboard",
                EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Web.spotify-dashboard.html", GetType().Namespace),
                EnableInMainMenu = true,
                MenuSection = "library",
                MenuIcon = "audiotrack",
                DisplayName = "Musik"
            },
            new PluginPageInfo
            {
                Name = "spotify-dashboard.js",
                EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Web.spotify-dashboard.js", GetType().Namespace)
            },
            new PluginPageInfo
            {
                Name = "spotify-theme.css",
                EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Web.spotify-theme.css", GetType().Namespace)
            },
            new PluginPageInfo
            {
                Name = "music-redirect.js",
                EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Web.music-redirect.js", GetType().Namespace)
            },
            new PluginPageInfo
            {
                Name = "albums-view.js",
                EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Web.albums-view.js", GetType().Namespace)
            }
        };
    }
}
