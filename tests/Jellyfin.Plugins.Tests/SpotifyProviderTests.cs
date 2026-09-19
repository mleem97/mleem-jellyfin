using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.SpotifyProvider;
using Jellyfin.Plugin.SpotifyProvider.Configuration;
using Jellyfin.Plugin.SpotifyProvider.Providers;
using Jellyfin.Plugin.SpotifyProvider.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Providers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugins.Tests;

public sealed class SpotifyProviderTests
{
    [Fact]
    public void SpotifyConfigurationUsesExpectedDefaults()
    {
        var config = new PluginConfiguration();

        Assert.Equal(string.Empty, config.ClientId);
        Assert.Equal(string.Empty, config.ClientSecret);
        Assert.Equal("DE", config.Market);
        Assert.True(config.EnableImageLookup);
        Assert.True(config.EnableMusicMetadata);
        Assert.True(config.EnableAudiobookMetadata);
        Assert.True(config.EnablePodcastMetadata);
    }

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
    public void SpotifyImageProviderSupportsAllAudioAndBookMediaTypes()
    {
        var factory = new TestHttpClientFactory(StubHttpMessageHandler.Json("{}"));
        var auth = new SpotifyAuthService(factory, NullLogger<SpotifyAuthService>.Instance);
        var api = new SpotifyApiClient(factory, auth, NullLogger<SpotifyApiClient>.Instance);
        var provider = new SpotifyImageProvider(factory, api, NullLogger<SpotifyImageProvider>.Instance);

        Assert.Equal("Spotify", provider.Name);
        Assert.Equal(3, provider.Order);

        Assert.True(provider.Supports(new MusicAlbum()));
        Assert.True(provider.Supports(new MusicArtist()));
        Assert.True(provider.Supports(new Book()));
        Assert.True(provider.Supports(new Audio()));
        Assert.False(provider.Supports(new Video()));
    }

    [Fact]
    public void SpotifyAlbumMetadataProviderHasCorrectNameAndOrder()
    {
        var factory = new TestHttpClientFactory(StubHttpMessageHandler.Json("{}"));
        var auth = new SpotifyAuthService(factory, NullLogger<SpotifyAuthService>.Instance);
        var api = new SpotifyApiClient(factory, auth, NullLogger<SpotifyApiClient>.Instance);
        var provider = new SpotifyAlbumMetadataProvider(api, factory, NullLogger<SpotifyAlbumMetadataProvider>.Instance);

        Assert.Equal("Spotify", provider.Name);
        Assert.Equal(3, provider.Order);
    }

    [Fact]
    public void SpotifyAudiobookMetadataProviderHasCorrectNameAndOrder()
    {
        var factory = new TestHttpClientFactory(StubHttpMessageHandler.Json("{}"));
        var auth = new SpotifyAuthService(factory, NullLogger<SpotifyAuthService>.Instance);
        var api = new SpotifyApiClient(factory, auth, NullLogger<SpotifyApiClient>.Instance);
        var provider = new SpotifyAudiobookMetadataProvider(api, factory, NullLogger<SpotifyAudiobookMetadataProvider>.Instance);

        Assert.Equal("Spotify", provider.Name);
        Assert.Equal(3, provider.Order);
    }
}
