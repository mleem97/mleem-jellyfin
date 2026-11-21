using System;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace Jellyfin.Plugin.MetadataDashboard.Integrations;

/// <summary>
/// Reflection bridge to integrate with Paradox PluginPages without a compile-time dependency.
/// </summary>
public sealed class ParadoxBridge
{
    private readonly ILogger<ParadoxBridge> _logger;
    private readonly object _sync = new();
    private bool _pageRegistered;

    /// <summary>
    /// Initializes a new instance of the <see cref="ParadoxBridge"/> class.
    /// </summary>
    /// <param name="logger">Logger for optional integration diagnostics.</param>
    public ParadoxBridge(ILogger<ParadoxBridge> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Initializes the bridge.
    /// </summary>
    public void Initialize()
    {
        lock (_sync)
        {
            if (!_pageRegistered)
            {
                _pageRegistered = TryRegisterPage();
            }
        }
    }

    private bool TryRegisterPage()
    {
        var pageInterface = FindType("Jellyfin.Plugin.PluginPages.PluginInterface");
        if (pageInterface is null)
        {
            _logger.LogInformation("Paradox PluginPages not installed; skipping page registration");
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
            var manifest = new JObject
            {
                ["id"] = "metadata-dashboard",
                ["displayText"] = "Metadaten-Manager",
                ["url"] = "/Plugins/MetadataDashboard/Assets/metadata-dashboard.html",
                ["icon"] = "edit_note"
            };

            register.Invoke(null, new object?[] { manifest });
            _logger.LogInformation("Successfully registered Metadaten-Manager with Paradox PluginPages");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to register Metadaten-Manager with Paradox PluginPages");
            return false;
        }
    }

    private static Type? FindType(string fullName)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                var type = assembly.GetType(fullName, throwOnError: false);
                if (type is not null)
                {
                    return type;
                }
            }
            catch
            {
                // Ignore load issues in external assemblies
            }
        }

        return null;
    }
}
