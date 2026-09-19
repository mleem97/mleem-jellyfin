using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Jellyfin.Plugin.MetadataDashboard.Controllers;
using Jellyfin.Plugin.MetadataDashboard.Providers;
using Jellyfin.Plugin.MetadataDashboard.Services;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Model.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using PluginInstance = Jellyfin.Plugin.MetadataDashboard.Plugin;

namespace Jellyfin.Plugins.Tests;

public sealed class MetadataDashboardTests
{
    [Fact]
    public void EmbeddedWebAssetsExistInPluginAssembly()
    {
        var resources = typeof(PluginInstance).Assembly.GetManifestResourceNames();

        Assert.Contains("Jellyfin.Plugin.MetadataDashboard.Web.metadata-dashboard.html", resources);
        Assert.Contains("Jellyfin.Plugin.MetadataDashboard.Web.metadata-dashboard.js", resources);
        Assert.Contains("Jellyfin.Plugin.MetadataDashboard.Web.metadata-dashboard.css", resources);
        Assert.Contains("Jellyfin.Plugin.MetadataDashboard.Web.config.html", resources);
        Assert.Contains("Jellyfin.Plugin.MetadataDashboard.Web.assets.metadata-dashboard-icon.svg", resources);
    }

    [Theory]
    [InlineData(typeof(MetadataDashboardController))]
    [InlineData(typeof(AssetController))]
    public void ControllersRequireAuthorization(Type controllerType)
    {
        var attributes = controllerType
            .GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .ToArray();

        Assert.NotEmpty(attributes);
    }

    [Fact]
    public void CoverArtArchiveImageProviderSupportsAlbumsAndExpectedImageTypes()
    {
        var httpClientFactory = new MusicHoarderzTestHttpClientFactory(StubHttpMessageHandler.Json("{}"));
        var service = new CoverArtArchiveService(httpClientFactory, NullLogger<CoverArtArchiveService>.Instance);
        var provider = new CoverArtArchiveImageProvider(service, httpClientFactory, NullLogger<CoverArtArchiveImageProvider>.Instance);

        Assert.Equal("CoverArtArchive", provider.Name);
        Assert.True(provider.Supports(new MusicAlbum()));
        Assert.False(provider.Supports(new MusicArtist()));

        var supported = provider.GetSupportedImages(new MusicAlbum()).ToList();
        Assert.Contains(ImageType.Primary, supported);
        Assert.Contains(ImageType.Backdrop, supported);
        Assert.Contains(ImageType.Disc, supported);
    }

    [Fact]
    public void CoverArtArchiveResultDeserializesCorrectly()
    {
        var sampleJson = """
        {
          "images": [
            {
              "types": ["Front"],
              "front": true,
              "back": false,
              "edit": 12345,
              "image": "http://coverartarchive.org/release/mbid/front.jpg",
              "thumbnails": {
                "250": "http://coverartarchive.org/release/mbid/front-250.jpg",
                "500": "http://coverartarchive.org/release/mbid/front-500.jpg",
                "1200": "http://coverartarchive.org/release/mbid/front-1200.jpg",
                "small": "http://coverartarchive.org/release/mbid/front-small.jpg",
                "large": "http://coverartarchive.org/release/mbid/front-large.jpg"
              },
              "approved": true
            }
          ],
          "release": "https://musicbrainz.org/release/sample-id"
        }
        """;

        var result = JsonSerializer.Deserialize<CoverArtArchiveResult>(sampleJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(result);
        Assert.NotNull(result.Images);
        Assert.Single(result.Images);
        var img = result.Images[0];
        Assert.True(img.Front);
        Assert.NotNull(img.Thumbnails);
        Assert.Equal("http://coverartarchive.org/release/mbid/front-1200.jpg", img.Thumbnails.Size1200);
    }
}
