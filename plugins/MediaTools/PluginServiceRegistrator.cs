using Jellyfin.Plugin.MediaTools.Integrations;
using Jellyfin.Plugin.MediaTools.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.MediaTools;

/// <summary>
/// Registers MediaTools services in Jellyfin DI.
/// </summary>
public sealed class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(
        IServiceCollection serviceCollection,
        IServerApplicationHost applicationHost)
    {
        _ = applicationHost;
        serviceCollection.AddSingleton<FFmpegResolverService>();
        serviceCollection.AddSingleton<SafeDatabaseSyncService>();
        serviceCollection.AddSingleton<MediaRenamerService>();
        serviceCollection.AddSingleton<SubtitleMuxerService>();
        serviceCollection.AddSingleton<SplitMovieMergerService>();
        serviceCollection.AddSingleton<ContainerConversionService>();
        serviceCollection.AddSingleton<StreamHashDeduplicatorService>();
        serviceCollection.AddSingleton<BackgroundJobQueueService>();
        serviceCollection.AddSingleton<PluginPagesBridge>();
    }
}
