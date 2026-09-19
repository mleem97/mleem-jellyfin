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
        serviceCollection.AddSingleton<MusicHoarderzHttpClient>();
        serviceCollection.AddSingleton<CoverImageValidator>();
        serviceCollection.AddSingleton<CoverMatchScorer>();
        serviceCollection.AddSingleton<CredentialStore>();
    }
}
