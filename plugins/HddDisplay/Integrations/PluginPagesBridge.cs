using System;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace Jellyfin.Plugin.HddDisplay.Integrations;

/// <summary>
/// Loosely-coupled bridge into the IAmParadox27 plugin ecosystem via reflection (fail-open).
/// Registers the HDD Display detail page in PluginPages and injects the dashboard widget via FileTransformation.
/// </summary>
public static class PluginPagesBridge
{
    private static ILogger? _logger;
    private static bool _initialized;

    /// <summary>
    /// Initializes the bridge.
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
                ["id"] = "hdd-display-storage",
                ["url"] = "/Plugins/HddDisplay/Assets/hdd-display.html",
                ["displayText"] = "Speicher & Festplatten",
                ["icon"] = "storage",
                ["section"] = "server"
            };
            register.Invoke(null, new object?[] { payload });
            _logger?.LogInformation("Registered HDD Display detail page via Paradox PluginPages");
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
            _logger?.LogInformation("Paradox FileTransformation not installed; skipping widget script injection");
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
            var assemblyName = typeof(WebTransformer).Assembly.FullName;
            var payload = new JObject
            {
                ["id"] = Guid.Parse("7c9e11a2-3b44-4f81-a952-efb4193da4c2"),
                ["fileNamePattern"] = "index.html",
                ["callbackAssembly"] = assemblyName,
                ["callbackClass"] = "Jellyfin.Plugin.HddDisplay.Integrations.WebTransformer",
                ["callbackMethod"] = "TransformIndex",
            };
            register.Invoke(null, new object?[] { payload });
            _logger?.LogInformation("Registered index.html transformation (HDD Display dashboard widget loader) via Paradox FileTransformation");
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
