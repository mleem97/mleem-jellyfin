using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.MediaTools.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.MediaTools;

/// <summary>
/// MediaTools plugin entry point.
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
    public override string Name => "MediaTools";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("8e4d2f17-5a33-4c91-b921-dfa724180c55");

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
                Name = "mediatools",
                EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Web.mediatools.html", GetType().Namespace),
                EnableInMainMenu = true,
                MenuSection = "server",
                MenuIcon = "build",
                DisplayName = "MediaTools"
            },
            new PluginPageInfo
            {
                Name = "mediatools.js",
                EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Web.mediatools.js", GetType().Namespace)
            },
            new PluginPageInfo
            {
                Name = "mediatools.css",
                EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Web.mediatools.css", GetType().Namespace)
            }
        };
    }
}
