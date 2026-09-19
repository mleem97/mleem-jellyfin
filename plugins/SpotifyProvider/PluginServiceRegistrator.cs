using Jellyfin.Plugin.SpotifyProvider.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.SpotifyProvider;

/// <summary>
/// Registers Spotify services with Jellyfin.
/// </summary>
public sealed class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(
        IServiceCollection serviceCollection,
        IServerApplicationHost applicationHost)
    {
        _ = applicationHost;
        serviceCollection.AddSingleton<SpotifyAuthService>();
        serviceCollection.AddSingleton<SpotifyApiClient>();
    }
}
