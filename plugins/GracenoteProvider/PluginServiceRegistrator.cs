using Jellyfin.Plugin.GracenoteProvider.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.GracenoteProvider;

/// <summary>
/// Registers Gracenote services with Jellyfin.
/// </summary>
public sealed class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(
        IServiceCollection serviceCollection,
        IServerApplicationHost applicationHost)
    {
        _ = applicationHost;
        serviceCollection.AddSingleton<GracenoteClient>();
    }
}
