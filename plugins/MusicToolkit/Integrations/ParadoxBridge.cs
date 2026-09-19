using System;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MusicToolkit.Integrations;

/// <summary>
/// Loosely-coupled bridge into the IAmParadox27 plugin ecosystem via reflection (fail-open).
/// </summary>
public static class ParadoxBridge
{
    private static ILogger? _logger;
    private static bool _initialized;

    /// <summary>
    /// Initializes the bridge (registers Spotify page + file transformations when Paradox is present).
    /// </summary>
    /// <param name="logger">Logger.</param>
    public static void Initialize(ILogger logger)
    {
        _logger = logger;
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        try
        {
            TryRegisterPage();
            TryRegisterTransformations();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ParadoxBridge initialization failed (fail-open, native music UI kept)");
        }
    }

    private static void TryRegisterPage()
    {
        var pageInterface = FindType("Jellyfin.Plugin.PluginPages.PluginInterface");
        if (pageInterface is null)
        {
            _logger?.LogInformation("Paradox PluginPages not installed; skipping /pages/spotify-music registration");
            return;
        }

        var register = pageInterface.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(m => string.Equals(m.Name, "RegisterPage", StringComparison.Ordinal));
        if (register is null)
        {
            _logger?.LogWarning("Paradox PluginPages.RegisterPage not found");
            return;
        }

        // Best-effort signature: RegisterPage(route, name, section, icon, resourcePath)
        try
        {
            var parameters = register.GetParameters();
            if (parameters.Length >= 4)
            {
                var args = new object?[parameters.Length];
                args[0] = "/pages/spotify-music";
                args[1] = "Musik";
                args[2] = "library";
                args[3] = "audiotrack";
                if (parameters.Length > 4)
                {
                    args[4] = "Jellyfin.Plugin.MusicToolkit.Web.spotify-dashboard.html";
                }

                for (var i = 5; i < args.Length; i++)
                {
                    args[i] = parameters[i].HasDefaultValue ? parameters[i].DefaultValue : null;
                }

                register.Invoke(null, args);
                _logger?.LogInformation("Registered Spotify dashboard at /pages/spotify-music via Paradox");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Paradox RegisterPage invocation failed (fail-open)");
        }
    }

    private static void TryRegisterTransformations()
    {
        var transformInterface = FindType("Jellyfin.Plugin.FileTransformation.PluginInterface");
        if (transformInterface is null)
        {
            _logger?.LogInformation("Paradox FileTransformation not installed; skipping CSS/JS injection");
            return;
        }

        var register = transformInterface.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(m => string.Equals(m.Name, "RegisterTransformation", StringComparison.Ordinal));
        if (register is null)
        {
            _logger?.LogWarning("Paradox FileTransformation.RegisterTransformation not found");
            return;
        }

        try
        {
            // Inject theme CSS into <head> of index.html and redirect JS before </body>.
            register.Invoke(null, new object?[] { "index.html", "<head>", "spotify-theme.css", "head-prepend" });
            register.Invoke(null, new object?[] { "index.html", "</body>", "music-redirect.js", "body-append" });
            _logger?.LogInformation("Registered Spotify theme + redirect transformations via Paradox");
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Paradox RegisterTransformation invocation failed (fail-open)");
        }
    }

    private static Type? FindType(string fullName)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type? type;
            try
            {
                type = assembly.GetType(fullName, throwOnError: false, ignoreCase: false);
            }
            catch
            {
                continue;
            }

            if (type is not null)
            {
                return type;
            }
        }

        return null;
    }
}
