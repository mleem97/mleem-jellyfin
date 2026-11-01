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
/// Injects the album view and optional Spotify navigation without hiding native pages.
/// </summary>
public static class WebTransformer
{
    /// <summary>
    /// Injects the album and navigation loaders into index.html.
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

        if (contents.Contains("data-musicsuite-loader", StringComparison.Ordinal))
        {
            return contents;
        }

        // Derive the server prefix before ApiClient is ready (for example /jellyfin/web/).
        // CSS is loaded only when the optional dashboard opens; the album view styles itself.
        const string injection = "<script data-musicsuite-loader>!function(){"
            + "var p=window.location.pathname,i=p.toLowerCase().indexOf('/web'),b=i>=0?p.slice(0,i):'';"
            + "['albums-view.js','music-redirect.js'].forEach(function(f){var s=document.createElement('script');"
            + "s.async=false;s.src=window.ApiClient&&window.ApiClient.getUrl?window.ApiClient.getUrl('Plugins/MusicSuite/Assets/'+f):b+'/Plugins/MusicSuite/Assets/'+f;"
            + "document.head.appendChild(s);});}();</script>\n";

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
