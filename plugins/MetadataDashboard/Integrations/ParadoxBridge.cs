using System;
using System.Linq;
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
    private static bool _initialized;

    /// <summary>
    /// Initializes a new instance of the <see cref="ParadoxBridge"/> class.
    /// </summary>
    public ParadoxBridge(ILogger<ParadoxBridge> logger)
    {
        _logger = logger;
        Initialize();
    }

    /// <summary>
    /// Initializes the bridge.
    /// </summary>
    public void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        TryRegisterPage();
    }

    private void TryRegisterPage()
    {
        var pageInterface = FindType("Jellyfin.Plugin.PluginPages.PluginInterface");
        if (pageInterface is null)
        {
            _logger.LogInformation("Paradox PluginPages not installed; skipping page registration");
            return;
        }

        var register = pageInterface.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(m => string.Equals(m.Name, "RegisterPage", StringComparison.Ordinal));
        if (register is null)
        {
            _logger.LogWarning("Paradox PluginPages.RegisterPage not found");
            return;
        }

        try
        {
            var manifest = new JObject
            {
                ["id"] = "metadata-dashboard",
                ["displayText"] = "Metadaten-Manager",
                ["path"] = "/Plugins/MetadataDashboard/Assets/metadata-dashboard.html",
                ["route"] = "/pages/metadata-dashboard",
                ["icon"] = "edit_note",
                ["section"] = "tools",
                ["requiresAdmin"] = true
            };

            register.Invoke(null, new object[] { manifest.ToString() });
            _logger.LogInformation("Successfully registered Metadaten-Manager with Paradox PluginPages");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to register Metadaten-Manager with Paradox PluginPages");
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
