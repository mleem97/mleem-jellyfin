using Jellyfin.Plugin.MusicSuite.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.MusicSuite;

/// <summary>
/// Registers MusicSuite services with Jellyfin.
/// </summary>
public sealed class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(
        IServiceCollection serviceCollection,
        IServerApplicationHost applicationHost)
    {
        _ = applicationHost;
        serviceCollection.AddSingleton<IUserMusicSettingsStore, UserMusicSettingsStore>();
        serviceCollection.AddScoped<IAlbumQueryService, AlbumQueryService>();
    }
}
