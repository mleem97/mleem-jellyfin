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
        Assert.Contains(
            "Jellyfin.Plugin.HddDisplay.Web.hdd-display.html",
            resourceNames);
        Assert.Contains(
            "Jellyfin.Plugin.HddDisplay.Web.hdd-display.js",
            resourceNames);
        Assert.Contains(
            "Jellyfin.Plugin.HddDisplay.Web.hdd-display.css",
            resourceNames);
        Assert.Contains(
            "Jellyfin.Plugin.HddDisplay.Web.assets.hdd-icon.svg",
            resourceNames);
        Assert.NotNull(typeof(HddPlugin).Assembly.GetName().Version);
    }

    [Fact]
    public void WebTransformerInjectsDashboardWidgetIntoHead()
    {
        var initialHtml = "<html><head><title>Jellyfin</title></head><body></body></html>";
        var transformed = Jellyfin.Plugin.HddDisplay.Integrations.WebTransformer.TransformIndex(
            new Jellyfin.Plugin.HddDisplay.Integrations.TransformPayload { Contents = initialHtml });

        Assert.NotNull(transformed);
        Assert.Contains("Plugins/HddDisplay/Assets/DashboardWidget.js", transformed);
        Assert.Contains("</head>", transformed);
    }
}
