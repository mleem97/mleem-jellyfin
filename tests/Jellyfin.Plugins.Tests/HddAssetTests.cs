using Xunit;
using HddPlugin = Jellyfin.Plugin.HddDisplay.Plugin;

namespace Jellyfin.Plugins.Tests;

public sealed class HddAssetTests
{
    [Fact]
    public void DashboardWidgetIsEmbeddedInPluginAssembly()
    {
        var resourceNames = typeof(HddPlugin).Assembly.GetManifestResourceNames();

        Assert.Contains(
            "Jellyfin.Plugin.HddDisplay.Web.dashboard-widget.js",
            resourceNames);
        Assert.NotNull(typeof(HddPlugin).Assembly.GetName().Version);
    }
}
