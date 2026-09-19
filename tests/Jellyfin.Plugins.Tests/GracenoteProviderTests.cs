using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.GracenoteProvider.Configuration;
using Jellyfin.Plugin.GracenoteProvider.Providers;
using Jellyfin.Plugin.GracenoteProvider.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugins.Tests;

public sealed class GracenoteProviderTests
{
    private sealed class TestHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public TestHttpClientFactory(HttpMessageHandler handler)
        {
            _handler = handler;
        }

        public HttpClient CreateClient(string name)
        {
            return new HttpClient(_handler, disposeHandler: false);
        }
    }

    [Fact]
    public void GracenoteConfigurationUsesExpectedDefaults()
    {
        var config = new PluginConfiguration();

        Assert.Equal("v2", config.ApiVersion);
        Assert.Equal(string.Empty, config.GracenoteClientId);
        Assert.Equal(string.Empty, config.GracenoteUserId);
        Assert.Equal(string.Empty, config.ApiKey);
        Assert.True(config.EnableGracenote);
        Assert.True(config.EnableAlbumMetadata);
        Assert.True(config.EnableArtistMetadata);
        Assert.True(config.EnableImageLookup);
        Assert.Equal("LARGE", config.PreferredImageSize);
    }

    [Fact]
    public void GracenoteMetadataProviderHasCorrectNameAndOrder()
    {
        var factory = new TestHttpClientFactory(StubHttpMessageHandler.Json("<QUERIES/>"));
        var client = new GracenoteClient(factory, NullLogger<GracenoteClient>.Instance);
        var provider = new GracenoteMetadataProvider(client, factory, NullLogger<GracenoteMetadataProvider>.Instance);

        Assert.Equal("Gracenote", provider.Name);
        Assert.Equal(2, provider.Order);
    }

    [Fact]
    public void GracenoteArtistMetadataProviderHasCorrectNameAndOrder()
    {
        var factory = new TestHttpClientFactory(StubHttpMessageHandler.Json("<QUERIES/>"));
        var client = new GracenoteClient(factory, NullLogger<GracenoteClient>.Instance);
        var provider = new GracenoteArtistMetadataProvider(client, factory, NullLogger<GracenoteArtistMetadataProvider>.Instance);

        Assert.Equal("Gracenote", provider.Name);
        Assert.Equal(2, provider.Order);
    }

    [Fact]
    public void GracenoteImageProviderSupportsAlbumsAndArtists()
    {
        var factory = new TestHttpClientFactory(StubHttpMessageHandler.Json("<QUERIES/>"));
        var client = new GracenoteClient(factory, NullLogger<GracenoteClient>.Instance);
        var provider = new GracenoteImageProvider(client, factory, NullLogger<GracenoteImageProvider>.Instance);

        Assert.Equal("Gracenote", provider.Name);
        Assert.Equal(2, provider.Order);
        Assert.True(provider.Supports(new MusicAlbum()));
        Assert.True(provider.Supports(new MusicArtist()));
        Assert.False(provider.Supports(new Book()));
        Assert.False(provider.Supports(new Video()));
    }
}
