using System;
using System.IO;
using System.Text;

namespace Jellyfin.Plugin.HddDisplay.Integrations;

/// <summary>
/// Payload shape delivered by Paradox FileTransformation.
/// </summary>
public sealed class TransformPayload
{
    /// <summary>
    /// Gets or sets the raw file contents.
    /// </summary>
    public string? Contents { get; set; }
}

/// <summary>
/// Static transformation callback invoked by Paradox FileTransformation.
/// Injects the HDD Display dashboard widget into index.html.
/// </summary>
public static class WebTransformer
{
    /// <summary>
    /// Injects the HDD Display dashboard widget into index.html.
    /// </summary>
    /// <param name="payload">Transformation payload.</param>
    /// <returns>Transformed file contents, or the unchanged input.</returns>
    public static string? TransformIndex(TransformPayload? payload)
    {
        var contents = payload?.Contents;
        if (string.IsNullOrWhiteSpace(contents))
        {
            return contents;
        }

        const string WidgetTag = "Plugins/HddDisplay/Assets/DashboardWidget.js";
        if (contents.Contains(WidgetTag, StringComparison.Ordinal))
        {
            return contents;
        }

        var loader = "<script>!function(){var s=document.createElement(\"script\");s.src=window.ApiClient&&window.ApiClient.getUrl?window.ApiClient.getUrl(\"/Plugins/HddDisplay/Assets/DashboardWidget.js\"):\"/Plugins/HddDisplay/Assets/DashboardWidget.js\";document.head.appendChild(s)}();</script>";

        if (contents.Contains("</head>", StringComparison.Ordinal))
        {
            return contents.Replace("</head>", loader + "</head>", StringComparison.Ordinal);
        }

        return contents;
    }

    /// <summary>
    /// Reads an embedded resource from the plugin assembly.
    /// </summary>
    /// <param name="resourceName">Full embedded resource name.</param>
    /// <returns>Resource contents or null.</returns>
    public static string? ReadEmbeddedResource(string resourceName)
    {
        var assembly = typeof(WebTransformer).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return null;
        }

        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
