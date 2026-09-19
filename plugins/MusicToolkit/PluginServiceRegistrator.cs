using Jellyfin.Plugin.MusicToolkit.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.MusicToolkit;

/// <summary>
/// Registers MusicToolkit services with Jellyfin.
/// </summary>
public sealed class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(
        IServiceCollection serviceCollection,
        IServerApplicationHost applicationHost)
    {
        _ = applicationHost;
        serviceCollection.AddSingleton<AudioHashService>();
        serviceCollection.AddSingleton<SafeRenameService>();
        serviceCollection.AddHttpClient();
        serviceCollection.AddSingleton<GracenoteClient>();
    }
}
