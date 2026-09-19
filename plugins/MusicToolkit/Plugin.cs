using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.MusicToolkit.Configuration;
using Jellyfin.Plugin.MusicToolkit.Integrations;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MusicToolkit;

/// <summary>
/// Main plugin entry point for Music Toolkit and Spotify UX.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Application paths.</param>
    /// <param name="xmlSerializer">XML serializer.</param>
    /// <param name="logger">Logger.</param>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer, ILogger<Plugin> logger)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
        ParadoxBridge.Initialize(logger);
    }

    /// <inheritdoc />
    public override string Name => "Music Toolkit & Spotify UX";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("9f3e582a-281b-4b21-8c43-b295cbfa51de");

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
                Name = "MusicToolkit",
                EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Configuration.configPage.html", GetType().Namespace),
                EnableInMainMenu = true,
                MenuSection = "server",
                MenuIcon = "audiotrack",
                DisplayName = "Music Toolkit",
            },
        };
    }
}
