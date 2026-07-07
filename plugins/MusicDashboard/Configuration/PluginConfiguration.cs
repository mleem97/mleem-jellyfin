using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.MusicDashboard.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    public bool IncludeNonMusicLibraries { get; set; }
}
