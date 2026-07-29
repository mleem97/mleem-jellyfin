using Jellyfin.Plugin.BetterMusicDisplay.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.BetterMusicDisplay;

/// <summary>
/// Registers Better MusicDisplay services with Jellyfin.
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
