using System;
using System.IO;
using System.Text;

namespace Jellyfin.Plugin.MusicSuite.Integrations;

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
/// Injects the Spotify theme stylesheet and music redirect interceptor.
/// </summary>
public static class WebTransformer
{
    /// <summary>
    /// Injects the Spotify theme and redirect loader into index.html.
    /// </summary>
    /// <param name="payload">Transformation payload.</param>
    /// <returns>Transformed HTML contents.</returns>
    public static string? TransformIndex(TransformPayload? payload)
    {
        var contents = payload?.Contents;
        if (string.IsNullOrWhiteSpace(contents))
        {
            return contents;
        }

        const string ThemeTag = "<link rel=\"stylesheet\" href=\"/Plugins/MusicSuite/Assets/spotify-theme.css\">";
        const string RedirectTag = "<script src=\"/Plugins/MusicSuite/Assets/music-redirect.js\"></script>";

        if (contents.Contains("Plugins/MusicSuite/Assets/music-redirect.js", StringComparison.Ordinal))
        {
            return contents;
        }

        var injection = $"{ThemeTag}\n{RedirectTag}\n";

        if (contents.Contains("</head>", StringComparison.OrdinalIgnoreCase))
        {
            return contents.Replace("</head>", injection + "</head>", StringComparison.OrdinalIgnoreCase);
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
