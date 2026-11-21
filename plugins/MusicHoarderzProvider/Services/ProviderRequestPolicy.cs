using System;
using Jellyfin.Plugin.MusicHoarderzProvider.Configuration;
using Microsoft.AspNetCore.Http;

namespace Jellyfin.Plugin.MusicHoarderzProvider.Services;

/// <summary>Restricts this provider's MVP to authenticated, explicit Jellyfin picker requests.</summary>
public static class ProviderRequestPolicy
{
    /// <summary>Checks that an image enumeration comes from the native image picker.</summary>
    /// <param name="context">Current request.</param>
    /// <param name="configuration">Provider settings.</param>
    /// <returns>Whether images are being explicitly searched, never refreshed implicitly.</returns>
    public static bool IsImageSearchRequest(HttpContext? context, PluginConfiguration? configuration)
    {
        if (!IsManualRequest(context, configuration))
        {
            return false;
        }

        var segments = context!.Request.Path.Value!.Trim('/').Split('/');
        return segments.Length == 3 && Guid.TryParse(segments[1], out _)
            && string.Equals(segments[2], "RemoteImages", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Checks manual request context independently from background refresh settings.</summary>
    /// <param name="context">The current server request, absent during background scans.</param>
    /// <param name="configuration">Provider configuration.</param>
    /// <returns>Whether a user explicitly opened or applied a supported picker.</returns>
    public static bool IsManualRequest(HttpContext? context, PluginConfiguration? configuration)
    {
        if (configuration is null || !configuration.Enabled || !configuration.MusicHoarderz.Enabled
            || !configuration.AllowManualSearch || context?.User.Identity?.IsAuthenticated != true
            || (!context.User.IsInRole("Administrator") && !configuration.AllowNonAdminManualSearch))
        {
            return false;
        }

        var segments = (context.Request.Path.Value ?? string.Empty).Trim('/').Split('/');
        if (segments.Length < 3 || !string.Equals(segments[0], "Items", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.Equals(segments[1], "RemoteSearch", StringComparison.OrdinalIgnoreCase))
        {
            return (segments.Length == 3 && (string.Equals(segments[2], "MusicAlbum", StringComparison.OrdinalIgnoreCase)
                || string.Equals(segments[2], "Image", StringComparison.OrdinalIgnoreCase)))
                || (segments.Length == 4 && string.Equals(segments[2], "Apply", StringComparison.OrdinalIgnoreCase)
                    && Guid.TryParse(segments[3], out _));
        }

        return Guid.TryParse(segments[1], out _)
            && string.Equals(segments[2], "RemoteImages", StringComparison.OrdinalIgnoreCase)
            && (segments.Length == 3 || (segments.Length == 4
                && string.Equals(segments[3], "Download", StringComparison.OrdinalIgnoreCase)));
    }
}
