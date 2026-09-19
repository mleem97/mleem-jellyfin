using System;

namespace Jellyfin.Plugin.MusicToolkit.Integrations;

/// <summary>
/// Enablement gate referenced by the PluginPages registration payload.
/// </summary>
public static class PageGate
{
    /// <summary>
    /// Gate method called by PluginPages for the registered page id.
    /// </summary>
    /// <param name="pageId">The page id being rendered.</param>
    /// <returns>True when the page should be shown.</returns>
    public static bool IsEnabled(string? pageId)
    {
        _ = pageId;
        return true;
    }
}
