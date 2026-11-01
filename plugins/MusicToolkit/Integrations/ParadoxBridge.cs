using System;
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
public sealed class ParadoxBridge
{
    private readonly ILogger<ParadoxBridge> _logger;
    private readonly object _sync = new();
    private bool _pageRegistered;
    private bool _transformationRegistered;

    /// <summary>
    /// Initializes a new instance of the <see cref="ParadoxBridge"/> class.
    /// </summary>
    /// <param name="logger">Logger for optional integration diagnostics.</param>
    public ParadoxBridge(ILogger<ParadoxBridge> logger)
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
                ["id"] = "musictoolkit-spotify",
                ["url"] = "/MusicToolkit/asset/dashboard",
                ["displayText"] = "Musik",
                ["icon"] = "audiotrack",
                ["isEnabledAssembly"] = "Jellyfin.Plugin.MusicToolkit",
                ["isEnabledClass"] = "PageGate",
                ["isEnabledMethod"] = "IsEnabled",
            };
            register.Invoke(null, new object?[] { payload });
            _logger.LogInformation("Registered Spotify dashboard link via Paradox PluginPages");
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
            _logger.LogInformation("Paradox FileTransformation not installed; skipping CSS/JS injection");
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
            _logger.LogInformation("Registered index.html transformation (Spotify theme + music redirect loader) via Paradox FileTransformation");
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
