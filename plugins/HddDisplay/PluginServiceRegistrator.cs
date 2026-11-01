using Jellyfin.Plugin.HddDisplay.Integrations;
using Jellyfin.Plugin.HddDisplay.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.HddDisplay;

/// <summary>
/// Registers HDD Display startup services with Jellyfin.
/// </summary>
public sealed class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        _ = applicationHost;
        serviceCollection.AddSingleton<PluginPagesBridge>();
        serviceCollection.AddHostedService<HddDisplayStartupService>();
    }
}
