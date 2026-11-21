using System;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace Jellyfin.Plugin.HddDisplay.Integrations;

/// <summary>
/// Loosely-coupled bridge into the IAmParadox27 plugin ecosystem via reflection (fail-open).
/// Registers the HDD Display detail page in PluginPages and injects the dashboard widget via FileTransformation.
/// </summary>
public sealed class PluginPagesBridge
{
    private readonly ILogger<PluginPagesBridge> _logger;
    private readonly object _sync = new();
    private bool _pageRegistered;
    private bool _transformationRegistered;

    /// <summary>
    /// Initializes a new instance of the <see cref="PluginPagesBridge"/> class.
    /// </summary>
    /// <param name="logger">Logger for optional integration diagnostics.</param>
    public PluginPagesBridge(ILogger<PluginPagesBridge> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Registers optional integrations after plugin loading; unsuccessful registrations can be retried.
    /// </summary>
    public void Initialize()
    {
        lock (_sync)
        {
            if (!_pageRegistered)
            {
                _pageRegistered = TryRegisterPage();
            }

            if (!_transformationRegistered)
            {
                _transformationRegistered = TryRegisterTransformations();
            }
        }
    }

    private bool TryRegisterPage()
    {
        var pageInterface = FindType("Jellyfin.Plugin.PluginPages.PluginInterface");
        if (pageInterface is null)
        {
            _logger.LogInformation("Paradox PluginPages not installed; skipping sidebar page registration");
            return false;
        }

        var register = pageInterface.GetMethod(
            "RegisterPage",
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: new[] { typeof(JObject) },
            modifiers: null);
        if (register is null)
        {
            _logger.LogWarning("Paradox PluginPages.RegisterPage not found");
            return false;
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
            _logger.LogInformation("Registered HDD Display detail page via Paradox PluginPages");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Paradox RegisterPage invocation failed (fail-open)");
            return false;
        }
    }

    private bool TryRegisterTransformations()
    {
        var transformInterface = FindType("Jellyfin.Plugin.FileTransformation.PluginInterface");
        if (transformInterface is null)
        {
            _logger.LogInformation("Paradox FileTransformation not installed; skipping widget script injection");
            return false;
        }

        var register = transformInterface.GetMethod(
            "RegisterTransformation",
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: new[] { typeof(JObject) },
            modifiers: null);
        if (register is null)
        {
            _logger.LogWarning("Paradox FileTransformation.RegisterTransformation not found");
            return false;
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
            _logger.LogInformation("Registered index.html transformation (HDD Display dashboard widget loader) via Paradox FileTransformation");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Paradox RegisterTransformation invocation failed (fail-open)");
            return false;
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
