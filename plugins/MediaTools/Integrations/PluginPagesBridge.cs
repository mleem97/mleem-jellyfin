using System;
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
    private readonly object _sync = new();
    private bool _pageRegistered;

    /// <summary>
    /// Initializes a new instance of the <see cref="PluginPagesBridge"/> class.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    public PluginPagesBridge(ILogger<PluginPagesBridge> logger)
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
                ["id"] = "mediatools-dashboard",
                ["url"] = "/Plugins/MediaTools/Assets/mediatools.html",
                ["displayText"] = "MediaTools",
                ["icon"] = "build",
                ["section"] = "server"
            };
            register.Invoke(null, new object?[] { payload });
            _logger.LogInformation("Registered MediaTools dashboard page via Paradox PluginPages");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Paradox RegisterPage invocation failed (fail-open)");
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
