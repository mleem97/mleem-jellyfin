using System.Reflection;
using Jellyfin.Plugin.BetterMusicDisplay.Controllers;
using Microsoft.AspNetCore.Authorization;
using Xunit;
using MusicPlugin = Jellyfin.Plugin.BetterMusicDisplay.Plugin;

namespace Jellyfin.Plugins.Tests;

public sealed class MusicAlbumsInjectionTests
{
    [Fact]
    public void AlbumsLoaderIsEmbeddedInPluginAssembly()
    {
        var resources = typeof(MusicPlugin).Assembly.GetManifestResourceNames();

        Assert.Contains(
            "Jellyfin.Plugin.BetterMusicDisplay.Web.albums-view.js",
            resources);
    }

    [Theory]
    [InlineData(typeof(AssetController))]
    [InlineData(typeof(AlbumsContextController))]
    public void AlbumsLoaderEndpointsRequireAuthentication(Type controllerType)
    {
        var attributes = controllerType
            .GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .ToArray();

        Assert.NotEmpty(attributes);
    }
}
