using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.SpotifyProvider.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.SpotifyProvider;

/// <summary>
/// Main plugin entry point for the Spotify cover and metadata provider.
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
    public override string Name => "Spotify Cover & Metadata Provider";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("67de1daa-c9fe-497a-9ca2-dc303abb717b");

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
                Name = "SpotifyProvider",
                EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Web.config.html", GetType().Namespace)
            }
        };
    }
}
