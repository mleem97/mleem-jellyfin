using System;
using System.Linq;
using System.Reflection;
using Jellyfin.Plugin.MusicSuite.Controllers;
using Jellyfin.Plugin.MusicSuite.Integrations;
using Microsoft.AspNetCore.Authorization;
using Xunit;
using MusicPlugin = Jellyfin.Plugin.MusicSuite.Plugin;

namespace Jellyfin.Plugins.Tests;

public sealed class MusicSuiteTests
{
    [Fact]
    public void SpotifyWebAssetsAreEmbeddedInAssembly()
    {
        var resources = typeof(MusicPlugin).Assembly.GetManifestResourceNames();

        Assert.Contains("Jellyfin.Plugin.MusicSuite.Web.spotify-dashboard.html", resources);
        Assert.Contains("Jellyfin.Plugin.MusicSuite.Web.spotify-dashboard.js", resources);
        Assert.Contains("Jellyfin.Plugin.MusicSuite.Web.spotify-theme.css", resources);
        Assert.Contains("Jellyfin.Plugin.MusicSuite.Web.music-redirect.js", resources);
        Assert.Contains("Jellyfin.Plugin.MusicSuite.Web.albums-view.js", resources);
        Assert.Contains("Jellyfin.Plugin.MusicSuite.Web.config.html", resources);
        Assert.Contains("Jellyfin.Plugin.MusicSuite.Web.assets.musicsuite-icon.svg", resources);
    }

    [Fact]
    public void WebTransformerInjectsSpotifyStylesAndRedirectScript()
    {
        var initialHtml = "<html><head><title>Jellyfin</title></head><body><div class=\"skinHeader\"></div></body></html>";
        var payload = new TransformPayload { Contents = initialHtml };

        var transformed = WebTransformer.TransformIndex(payload);

        Assert.NotNull(transformed);
        Assert.Contains("Plugins/MusicSuite/Assets/spotify-theme.css", transformed);
        Assert.Contains("Plugins/MusicSuite/Assets/music-redirect.js", transformed);
        Assert.Contains("</head>", transformed);
        Assert.Contains("</body>", transformed);
    }

    [Theory]
    [InlineData(typeof(AssetController))]
    [InlineData(typeof(AlbumsContextController))]
    [InlineData(typeof(MusicDisplayController))]
    public void MusicSuiteControllersAreSecuredWithAuthorize(Type controllerType)
    {
        var attributes = controllerType
            .GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .ToArray();

        Assert.NotEmpty(attributes);
    }
}
