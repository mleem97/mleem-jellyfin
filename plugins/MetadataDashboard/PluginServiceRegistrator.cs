using Jellyfin.Plugin.MetadataDashboard.Integrations;
using Jellyfin.Plugin.MetadataDashboard.Providers;
using Jellyfin.Plugin.MetadataDashboard.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Providers;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.MetadataDashboard;

/// <summary>
/// Registers services for the MetadataDashboard plugin.
/// </summary>
public sealed class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<CoverArtArchiveService>();
        serviceCollection.AddSingleton<MusicBrainzLookupService>();
        serviceCollection.AddSingleton<MetadataAuditService>();
        serviceCollection.AddSingleton<IRemoteImageProvider, CoverArtArchiveImageProvider>();
        serviceCollection.AddSingleton<ParadoxBridge>();
    }
}
