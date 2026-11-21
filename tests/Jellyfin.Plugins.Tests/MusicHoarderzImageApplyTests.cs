using System.Net;
using System.Security.Claims;
using Jellyfin.Plugin.MusicHoarderzProvider.Configuration;
using Jellyfin.Plugin.MusicHoarderzProvider.Controllers;
using Jellyfin.Plugin.MusicHoarderzProvider.Models;
using Jellyfin.Plugin.MusicHoarderzProvider.Providers;
using Jellyfin.Plugin.MusicHoarderzProvider.Services;
using MediaBrowser.Controller.Entities.Audio;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Jellyfin.Plugins.Tests;

public class MusicHoarderzImageApplyTests
{
    [Fact]
    public async Task NativePicker_ReceivesLocalCapabilityUrlAndNeverRefetchesUpstreamOnApply()
    {
        var png = MusicHoarderzImageFixtures.SmallPng();
        var handler = new MusicHoarderzQueueHandler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(png) };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
            return response;
        });
        var validator = new CoverImageValidator(new MusicHoarderzTestHttpClientFactory(handler),
            MusicHoarderzNoopLogger<CoverImageValidator>.Instance, new MusicHoarderzTestImageDecoder(), (_, _) => Task.FromResult(new[] { IPAddress.Parse("93.184.216.34") }));
        var source = new FakeCoverProvider();
        source.Results.Add(new NormalizedCoverResult("https://covers.example.com/a.png", 1, 1, "cov", "image/png", 0, "", "Album", "Artist"));
        var cache = new ValidatedCoverCache();
        var context = AdminContext();
        var config = new PluginConfiguration { MinimumWidth = 1, MinimumHeight = 1 };
        var provider = new MusicHoarderzImageProvider(source, new CoverMatchScorer(), validator,
            MusicHoarderzNoopLogger<MusicHoarderzImageProvider>.Instance, new HttpContextAccessor { HttpContext = context }, () => config, cache);

        var images = (await provider.GetImages(new MusicAlbum { Name = "Album", AlbumArtists = new[] { "Artist" } }, CancellationToken.None)).ToArray();

        var localUrl = Assert.Single(images).Url;
        Assert.StartsWith("https://jellyfin.example.com/jellyfin/Plugins/MusicHoarderzProvider/Images/", localUrl);
        Assert.DoesNotContain("covers.example.com", localUrl);
        var ticket = Guid.Parse(new Uri(localUrl).Segments.Last());
        var controller = new ValidatedImageController(cache) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        var responseBytes = Assert.IsType<FileContentResult>(controller.GetImage(ticket));
        Assert.Equal(png, responseBytes.FileContents);
        Assert.Equal("image/png", responseBytes.ContentType);
        Assert.Single(handler.Requests);
        Assert.IsType<NotFoundResult>(controller.GetImage(Guid.NewGuid()));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task BackgroundAndMetadataRefresh_DoNotSearchOrAutomaticallyApplyCovers()
    {
        var source = new FakeCoverProvider();
        var factory = new MusicHoarderzTestHttpClientFactory(new MusicHoarderzQueueHandler((_, _) => throw new InvalidOperationException("Unexpected HTTP")));
        var context = AdminContext();
        context.Request.Path = "/Items/RemoteSearch/Apply/" + Guid.NewGuid();
        var provider = new MusicHoarderzImageProvider(source, new CoverMatchScorer(),
            new CoverImageValidator(factory, MusicHoarderzNoopLogger<CoverImageValidator>.Instance, new MusicHoarderzTestImageDecoder()),
            MusicHoarderzNoopLogger<MusicHoarderzImageProvider>.Instance, new HttpContextAccessor { HttpContext = context }, () => new PluginConfiguration { AutoSearchEnabled = true });

        Assert.Empty(await provider.GetImages(new MusicAlbum { Name = "Album" }, CancellationToken.None));
        Assert.Null(source.LastQuery);
    }

    [Fact]
    public void CapabilityCache_ExpiresAndIsBounded()
    {
        var clock = new TestClock();
        var cache = new ValidatedCoverCache(clock);
        var image = new ValidatedCoverImage("https://covers.example.com/a.png", "image/png", 1, 1, new byte[] { 1, 2, 3 });
        var oldest = cache.Add(image);
        for (var i = 0; i < 16; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            cache.Add(image);
        }

        Assert.Null(cache.Get(oldest));
        var current = cache.Add(image);
        Assert.NotNull(cache.Get(current));
        clock.Advance(TimeSpan.FromMinutes(11));
        Assert.Null(cache.Get(current));
    }

    private static DefaultHttpContext AdminContext()
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("jellyfin.example.com");
        context.Request.PathBase = "/jellyfin";
        context.Request.Path = "/Items/" + Guid.NewGuid() + "/RemoteImages";
        context.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "Administrator") }, "Jellyfin"));
        return context;
    }

    [Fact]
    public void LargeSearchBatch_CannotEvictItsOwnReturnedCapabilities()
    {
        var cache = new ValidatedCoverCache();
        var image = new ValidatedCoverImage("https://covers.example.com/large.png", "image/png", 1200, 1200,
            new byte[CoverImageValidator.MaxImageBytes]);
        var tickets = new List<Guid>();
        var responseBytes = 0;
        for (var candidate = 0; candidate < 5; candidate++)
        {
            var ticket = cache.AddForResponse(image, ref responseBytes);
            if (ticket.HasValue)
            {
                tickets.Add(ticket.Value);
            }
        }

        Assert.Equal(2, tickets.Count);
        Assert.All(tickets, ticket => Assert.NotNull(cache.Get(ticket)));
        Assert.InRange(responseBytes, 1, ValidatedCoverCache.MaxBytes);
    }

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan elapsed) => _now += elapsed;
    }
}
