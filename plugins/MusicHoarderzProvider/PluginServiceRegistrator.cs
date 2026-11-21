using System.Net.Http;
using Jellyfin.Plugin.MusicHoarderzProvider.Models;
using Jellyfin.Plugin.MusicHoarderzProvider.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.MusicHoarderzProvider;

/// <summary>
/// Registers MusicHoarderz provider services with Jellyfin.
/// </summary>
public sealed class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(
        IServiceCollection serviceCollection,
        IServerApplicationHost applicationHost)
    {
        _ = applicationHost;
        serviceCollection.AddHttpContextAccessor();
        serviceCollection.AddSingleton<MusicHoarderzHttpClient>();
        serviceCollection.AddSingleton<ICoverProvider>(static services => services.GetRequiredService<MusicHoarderzHttpClient>());
        serviceCollection.AddSingleton<IAlbumMetadataProvider>(static services => services.GetRequiredService<MusicHoarderzHttpClient>());
        serviceCollection.AddSingleton<ICoverImageDecoder, JellyfinCoverImageDecoder>();
        serviceCollection.AddSingleton<CoverImageValidator>();
        serviceCollection.AddSingleton<ValidatedCoverCache>();
        serviceCollection.AddSingleton<CoverMatchScorer>();
        serviceCollection.AddSingleton<CredentialStore>();
        serviceCollection.AddHttpClient("musichoarderz")
            .ConfigurePrimaryHttpMessageHandler(NetworkGuard.CreateHandler)
            .RemoveAllLoggers();
        serviceCollection.AddHttpClient("musichoarderz-images")
            .ConfigurePrimaryHttpMessageHandler(NetworkGuard.CreateHandler)
            .RemoveAllLoggers();
        serviceCollection.AddHttpClient("musichoarderz-credentials")
            .ConfigurePrimaryHttpMessageHandler(static () => new HttpClientHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false,
            })
            .RemoveAllLoggers();
    }
}
