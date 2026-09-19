using System;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace Jellyfin.Plugin.MusicToolkit.Integrations;

/// <summary>
/// Loosely-coupled bridge into the IAmParadox27 plugin ecosystem via reflection (fail-open).
/// Uses the real PluginPages/FileTransformation contracts:
///   - PluginPages.PluginInterface.RegisterPage(JObject { id, url, displayText, icon })
///   - FileTransformation.PluginInterface.RegisterTransformation(JObject { id, fileNamePattern, callbackAssembly, callbackClass, callbackMethod })
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
        TryRegisterPage();
        TryRegisterTransformations();
    }

    private static void TryRegisterPage()
    {
        var pageInterface = FindType("Jellyfin.Plugin.PluginPages.PluginInterface");
        if (pageInterface is null)
        {
            _logger?.LogInformation("Paradox PluginPages not installed; skipping sidebar page registration");
            return;
        }

        var register = pageInterface.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(m => string.Equals(m.Name, "RegisterPage", StringComparison.Ordinal));
        if (register is null)
        {
            _logger?.LogWarning("Paradox PluginPages.RegisterPage not found");
            return;
        }

        try
        {
            var payload = new JObject
            {
                ["id"] = "musictoolkit-spotify",
                ["url"] = "/MusicToolkit/asset/dashboard",
                ["displayText"] = "Musik",
                ["icon"] = "audiotrack",
                ["isEnabledAssembly"] = "Jellyfin.Plugin.MusicToolkit",
                ["isEnabledClass"] = "PageGate",
                ["isEnabledMethod"] = "IsEnabled",
            };
            register.Invoke(null, new object?[] { payload });
            _logger?.LogInformation("Registered Spotify dashboard link via Paradox PluginPages");
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
            // Exact assembly full name required: FileTransformation matches Assembly.FullName exactly.
            var assemblyName = typeof(WebTransformer).Assembly.FullName;
            var payload = new JObject
            {
                ["id"] = Guid.Parse("3f6d8d5e-2a44-4a1e-9c31-5b7f0d8e9a21"),
                ["fileNamePattern"] = "index.html",
                ["callbackAssembly"] = assemblyName,
                ["callbackClass"] = "Jellyfin.Plugin.MusicToolkit.Integrations.WebTransformer",
                ["callbackMethod"] = "TransformIndex",
            };
            register.Invoke(null, new object?[] { payload });
            _logger?.LogInformation("Registered index.html transformation (Spotify theme + music redirect loader) via Paradox FileTransformation");
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
