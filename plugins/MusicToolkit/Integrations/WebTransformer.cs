using System;
using System.IO;
using System.Reflection;
using System.Text;

namespace Jellyfin.Plugin.MusicToolkit.Integrations;

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
/// Static transformation callback invoked by Paradox FileTransformation (see ParadoxBridge).
/// Signature contract: static string Transform(TransformPayload payload) where the first
/// parameter type of the method is deserialized from a JSON object containing a
/// <c>contents</c> property with the raw file text.
/// </summary>
public static class WebTransformer
{

    /// <summary>
    /// Injects the music redirect loader into the served index.html.
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

        // Loader script: pulls the real redirect logic from our plugin and keeps ItemId-safe redirects.
        var loader = "<script>!function(){var s=document.createElement(\"script\");s.src=window.ApiClient&&window.ApiClient.getUrl?window.ApiClient.getUrl(\"/MusicToolkit/asset/music-redirect.js\"):\"/MusicToolkit/asset/music-redirect.js\";document.head.appendChild(s)}();</script>";

        if (contents.Contains("MusicToolkit/asset/music-redirect.js", StringComparison.Ordinal))
        {
            return contents;
        }

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
