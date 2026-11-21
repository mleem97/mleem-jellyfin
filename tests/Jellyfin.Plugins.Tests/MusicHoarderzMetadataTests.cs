using System.Security.Claims;
using Jellyfin.Plugin.MusicHoarderzProvider.Configuration;
using Jellyfin.Plugin.MusicHoarderzProvider.Models;
using Jellyfin.Plugin.MusicHoarderzProvider.Providers;
using Jellyfin.Plugin.MusicHoarderzProvider.Services;
using MediaBrowser.Controller.Providers;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Jellyfin.Plugins.Tests;

public class MusicHoarderzMetadataTests
{
    [Fact]
    public async Task BackgroundRefresh_DoesNotApplyPreviouslySelectedMetadata()
    {
        var source = Source();
        var provider = CreateProvider(source, null, new PluginConfiguration { AutoSearchEnabled = true });
        var result = await provider.GetMetadata(Info(), CancellationToken.None);

        Assert.False(result.HasMetadata);
        Assert.Null(result.Item);
    }

    [Fact]
    public async Task ManualApply_UsesSourceValuesAndPreservesExternalIds()
    {
        var source = Source();
        source.Results.Add(new NormalizedAlbumResult("selected", "Source Album", "Source Artist", 1999,
            new Dictionary<string, string> { ["MusicBrainzAlbum"] = "mbid" }, null));
        var provider = CreateProvider(source, AdminContext("/Items/RemoteSearch/Apply/" + Guid.NewGuid()), new PluginConfiguration());

        var result = await provider.GetMetadata(Info(), CancellationToken.None);

        Assert.True(result.HasMetadata);
        Assert.Equal("Source Album", result.Item.Name);
        Assert.Equal(new[] { "Source Artist" }, result.Item.AlbumArtists);
        Assert.Equal(1999, result.Item.ProductionYear);
        Assert.Equal("mbid", result.Item.ProviderIds["MusicBrainzAlbum"]);
    }

    [Fact]
    public async Task MissingSourceData_IsNotInventedFromQuery()
    {
        var source = Source();
        source.Results.Add(new NormalizedAlbumResult("selected", null, null, null, new Dictionary<string, string>(), null));
        var provider = CreateProvider(source, AdminContext("/Items/RemoteSearch/Apply/" + Guid.NewGuid()), new PluginConfiguration());

        var result = await provider.GetMetadata(Info(), CancellationToken.None);

        Assert.False(result.HasMetadata);
        Assert.Null(result.Item);
    }

    [Fact]
    public async Task UnknownManualSelection_DoesNotTrustClientProvidedFields()
    {
        var provider = CreateProvider(Source(), AdminContext("/Items/RemoteSearch/Apply/" + Guid.NewGuid()), new PluginConfiguration());

        Assert.False((await provider.GetMetadata(Info(), CancellationToken.None)).HasMetadata);
    }

    [Fact]
    public async Task SearchMissingArtist_DoesNotInventArtistOrYear()
    {
        var source = Source();
        source.Results.Add(new NormalizedAlbumResult("selected", "Source Album", null, null, new Dictionary<string, string>(), null));
        var provider = CreateProvider(source, AdminContext("/Items/RemoteSearch/MusicAlbum"), new PluginConfiguration());

        var results = (await provider.GetSearchResults(Info(), CancellationToken.None)).ToArray();

        Assert.Single(results);
        Assert.Null(results[0].AlbumArtist);
        Assert.Null(results[0].ProductionYear);
    }

    [Theory]
    [InlineData("/Items/RemoteSearch/MusicAlbum")]
    [InlineData("/Items/a1c16aa2-5a34-4880-8f89-69243b5f3c5c/RemoteImages")]
    public void ManualPolicy_RequiresAuthenticatedAdministratorByDefault(string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        var config = new PluginConfiguration();
        Assert.False(ProviderRequestPolicy.IsManualRequest(context, config));
        context.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "normal-user") }, "Jellyfin"));
        Assert.False(ProviderRequestPolicy.IsManualRequest(context, config));
        config.AllowNonAdminManualSearch = true;
        Assert.True(ProviderRequestPolicy.IsManualRequest(context, config));
        config.AllowManualSearch = false;
        Assert.False(ProviderRequestPolicy.IsManualRequest(AdminContext(path), config));
    }

    [Fact]
    public void RefreshApi_IsNotAManualPicker()
    {
        Assert.False(ProviderRequestPolicy.IsManualRequest(AdminContext("/Items/" + Guid.NewGuid() + "/Refresh"), new PluginConfiguration()));
    }

    private static FakeAlbumMetadataProvider Source() => new();

    private static AlbumInfo Info() => new()
    {
        Name = "Existing Query Title",
        AlbumArtists = new[] { "Existing Query Artist" },
        Year = 2020,
        ProviderIds = new Dictionary<string, string> { ["MusicHoarderz"] = "selected" },
    };

    private static DefaultHttpContext AdminContext(string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "Administrator") }, "Jellyfin"));
        return context;
    }

    private static MusicHoarderzMetadataProvider CreateProvider(IAlbumMetadataProvider source, HttpContext? context, PluginConfiguration configuration)
    {
        return new MusicHoarderzMetadataProvider(source, new CoverMatchScorer(), MusicHoarderzNoopLogger<MusicHoarderzMetadataProvider>.Instance,
            new HttpContextAccessor { HttpContext = context }, configurationAccessor: () => configuration);
    }
}
