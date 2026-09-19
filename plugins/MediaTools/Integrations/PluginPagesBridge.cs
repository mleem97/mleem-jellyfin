using System;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace Jellyfin.Plugin.MediaTools.Integrations;

/// <summary>
/// Loosely-coupled bridge into the IAmParadox27 plugin ecosystem via reflection (fail-open).
/// Registers the MediaTools dashboard page in PluginPages.
/// </summary>
public sealed class PluginPagesBridge
{
    private readonly ILogger<PluginPagesBridge> _logger;
    private static bool _initialized;

    /// <summary>
    /// Initializes a new instance of the <see cref="PluginPagesBridge"/> class.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    public PluginPagesBridge(ILogger<PluginPagesBridge> logger)
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
            _logger.LogInformation("Paradox PluginPages not installed; skipping sidebar page registration");
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
            var payload = new JObject
            {
                ["id"] = "mediatools-dashboard",
                ["url"] = "/Plugins/MediaTools/Assets/mediatools.html",
                ["displayText"] = "MediaTools",
                ["icon"] = "build",
                ["section"] = "server"
            };
            register.Invoke(null, new object?[] { payload });
            _logger.LogInformation("Registered MediaTools dashboard page via Paradox PluginPages");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Paradox RegisterPage invocation failed (fail-open)");
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
