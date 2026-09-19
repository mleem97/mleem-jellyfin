using System;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace Jellyfin.Plugin.MusicSuite.Integrations;

/// <summary>
/// Loosely-coupled bridge into the IAmParadox27 plugin ecosystem via reflection (fail-open).
/// Registers the Spotify music dashboard in PluginPages and injects the theme/redirect via FileTransformation.
/// </summary>
public sealed class ParadoxBridge
{
    private readonly ILogger<ParadoxBridge> _logger;
    private static bool _initialized;

    /// <summary>
    /// Initializes a new instance of the <see cref="ParadoxBridge"/> class.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
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
        TryRegisterTransformations();
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
                ["id"] = "musicsuite-spotify",
                ["url"] = "/Plugins/MusicSuite/Assets/spotify-dashboard.html",
                ["displayText"] = "Musik",
                ["icon"] = "audiotrack",
                ["section"] = "library"
            };
            register.Invoke(null, new object?[] { payload });
            _logger.LogInformation("Registered Spotify music dashboard page via Paradox PluginPages");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Paradox RegisterPage invocation failed (fail-open)");
        }
    }

    private void TryRegisterTransformations()
    {
        var transformInterface = FindType("Jellyfin.Plugin.FileTransformation.PluginInterface");
        if (transformInterface is null)
        {
            _logger.LogInformation("Paradox FileTransformation not installed; skipping theme and redirect injection");
            return;
        }

        var register = transformInterface.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(m => string.Equals(m.Name, "RegisterTransformation", StringComparison.Ordinal));
        if (register is null)
        {
            _logger.LogWarning("Paradox FileTransformation.RegisterTransformation not found");
            return;
        }

        try
        {
            var assemblyName = typeof(WebTransformer).Assembly.FullName;
            var payload = new JObject
            {
                ["id"] = Guid.Parse("6b6c96ac-3f60-4ac3-8b8a-0d0cb8acb9dd"),
                ["fileNamePattern"] = "index.html",
                ["callbackAssembly"] = assemblyName,
                ["callbackClass"] = "Jellyfin.Plugin.MusicSuite.Integrations.WebTransformer",
                ["callbackMethod"] = "TransformIndex",
            };
            register.Invoke(null, new object?[] { payload });
            _logger.LogInformation("Registered index.html transformation for MusicSuite via Paradox FileTransformation");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Paradox RegisterTransformation invocation failed (fail-open)");
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
