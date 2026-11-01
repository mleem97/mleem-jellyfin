using System;
using System.Linq;
using System.Reflection;
using Jellyfin.Plugin.MusicSuite.Controllers;
using Jellyfin.Plugin.MusicSuite.Integrations;
using Jellyfin.Plugin.MusicSuite.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using MusicPlugin = Jellyfin.Plugin.MusicSuite.Plugin;

namespace Jellyfin.Plugins.Tests;

public sealed class MusicSuiteTests
{
    [Fact]
    public void WebBridgeIsRegisteredAsAStartupService()
    {
        var services = new ServiceCollection();
        new Jellyfin.Plugin.MusicSuite.PluginServiceRegistrator().RegisterServices(services, null!);
        Assert.Contains(services, service => service.ServiceType == typeof(IHostedService)
            && service.ImplementationType == typeof(MusicSuiteStartupService));
    }

    [Fact]
    public async Task WebBridgeStartupHonorsCancellationAndToleratesAbsentOptionalPlugins()
    {
        var service = new MusicSuiteStartupService(new ParadoxBridge(NullLogger<ParadoxBridge>.Instance));
        await Assert.ThrowsAsync<OperationCanceledException>(() => service.StartAsync(new CancellationToken(canceled: true)));
        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);
    }

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
    public void WebTransformerInjectsAlbumAndOptionalNavigationWithoutGlobalTheme()
    {
        var initialHtml = "<html><head><title>Jellyfin</title></head><body><div class=\"skinHeader\"></div></body></html>";
        var payload = new TransformPayload { Contents = initialHtml };

        var transformed = WebTransformer.TransformIndex(payload);

        Assert.NotNull(transformed);
        Assert.Contains("albums-view.js", transformed);
        Assert.Contains("music-redirect.js", transformed);
        Assert.DoesNotContain("spotify-theme.css", transformed);
        Assert.Equal(transformed, WebTransformer.TransformIndex(new TransformPayload { Contents = transformed }));
        Assert.Contains("</head>", transformed);
        Assert.Contains("</body>", transformed);
    }

    [Fact]
    public void AlbumAndNavigationInjectionSupportsCaseInsensitiveHtmlHead()
    {
        var transformed = WebTransformer.TransformIndex(new TransformPayload { Contents = "<HTML><HEAD></HEAD><BODY></BODY></HTML>" });
        Assert.Contains("data-musicsuite-loader", transformed!);
        Assert.Contains("p.slice(0,i)", transformed!);
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
