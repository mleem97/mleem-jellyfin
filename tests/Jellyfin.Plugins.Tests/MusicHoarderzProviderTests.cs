using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MusicHoarderzProvider.Controllers;
using Jellyfin.Plugin.MusicHoarderzProvider.Models;
using Jellyfin.Plugin.MusicHoarderzProvider.Providers;
using Jellyfin.Plugin.MusicHoarderzProvider.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Model.Entities;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Jellyfin.Plugins.Tests;

public class MusicHoarderzProviderTests
{
    [Fact]
    public void ImageProvider_Supports_AlbumAndArtist()
    {
        var provider = CreateImageProvider();

        Assert.True(provider.Supports(new MusicAlbum()));
        Assert.True(provider.Supports(new MusicArtist()));
        Assert.False(provider.Supports(new Audio()));
    }

    [Fact]
    public void ImageProvider_SupportedImages_ArtistHasBackdrop()
    {
        var provider = CreateImageProvider();

        var albumImages = provider.GetSupportedImages(new MusicAlbum()).ToList();
        var artistImages = provider.GetSupportedImages(new MusicArtist()).ToList();

        Assert.Single(albumImages);
        Assert.Equal(ImageType.Primary, albumImages[0]);

        Assert.Contains(ImageType.Primary, artistImages);
        Assert.Contains(ImageType.Backdrop, artistImages);
    }

    [Fact]
    public async Task SearchTest_WithResults_ReturnsCoverSearchItemDtos()
    {
        var json = "{\"results\":[{\"url\":\"https://covers.example.com/a.jpg\",\"width\":1400,\"height\":1400,\"source\":\"spotify\",\"title\":\"Test Album\",\"artist\":\"Test Artist\"}]}";
        var handler = new MusicHoarderzQueueHandler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            };
            return response;
        });

        var factory = new MusicHoarderzTestHttpClientFactory(handler);
        var client = new MusicHoarderzHttpClient(factory, MusicHoarderzNoopLogger<MusicHoarderzHttpClient>.Instance, "https://covers.example.com", "DE");
        var scorer = new CoverMatchScorer();
        var store = new CredentialStore(MusicHoarderzNoopLogger<CredentialStore>.Instance, Path.GetTempPath());

        var controller = new ProviderController(
            store,
            factory,
            MusicHoarderzNoopLogger<ProviderController>.Instance,
            client,
            scorer);

        var actionResult = await controller.SearchTest("Test Artist", "Test Album", "DE", CancellationToken.None);
        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var items = Assert.IsAssignableFrom<IEnumerable<CoverSearchItemDto>>(okResult.Value).ToList();

        Assert.Single(items);
        Assert.Equal("https://covers.example.com/a.jpg", items[0].Url);
        Assert.Equal("spotify", items[0].Source);
        Assert.Equal(1400, items[0].Width);
        Assert.Equal(1400, items[0].Height);
    }

    [Fact]
    public async Task SearchTest_EmptyQuery_ReturnsEmptyList()
    {
        var factory = new MusicHoarderzTestHttpClientFactory(new MusicHoarderzQueueHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK)));
        var store = new CredentialStore(MusicHoarderzNoopLogger<CredentialStore>.Instance, Path.GetTempPath());
        var controller = new ProviderController(store, factory, MusicHoarderzNoopLogger<ProviderController>.Instance);

        var actionResult = await controller.SearchTest(null, null, null, CancellationToken.None);
        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var items = Assert.IsAssignableFrom<IEnumerable<CoverSearchItemDto>>(okResult.Value).ToList();

        Assert.Empty(items);
    }

    private static MusicHoarderzImageProvider CreateImageProvider()
    {
        var handler = new MusicHoarderzQueueHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK));
        var factory = new MusicHoarderzTestHttpClientFactory(handler);
        var client = new MusicHoarderzHttpClient(factory, MusicHoarderzNoopLogger<MusicHoarderzHttpClient>.Instance);
        var scorer = new CoverMatchScorer();
        var validator = new CoverImageValidator(factory, MusicHoarderzNoopLogger<CoverImageValidator>.Instance);
        return new MusicHoarderzImageProvider(client, scorer, validator, MusicHoarderzNoopLogger<MusicHoarderzImageProvider>.Instance);
    }
}
