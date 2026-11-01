using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.MusicHoarderzProvider.Services;

/// <summary>Reads untrusted bodies with a hard byte limit, including chunked responses.</summary>
internal static class BoundedHttpContent
{
    public static async Task<byte[]> ReadAsync(HttpContent content, int limit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var output = new MemoryStream();
        var chunk = new byte[8192];
        while (true)
        {
            var remaining = limit - (int)output.Length;
            var read = await stream.ReadAsync(chunk.AsMemory(0, Math.Min(chunk.Length, remaining + 1)), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return output.ToArray();
            }

            if (read > remaining)
            {
                throw new InvalidDataException("Response exceeded the size limit.");
            }

            await output.WriteAsync(chunk.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
    }
}
