using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Drawing;

namespace Jellyfin.Plugin.MusicHoarderzProvider.Services;

/// <summary>Performs pixel decoding after network, size, container and dimension checks.</summary>
public interface ICoverImageDecoder
{
    /// <summary>Checks that the bounded image payload can be decoded.</summary>
    /// <param name="content">Downloaded image.</param>
    /// <param name="mimeType">Already verified image MIME.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Whether a real image decoder accepted the pixels.</returns>
    Task<bool> CanDecodeAsync(ReadOnlyMemory<byte> content, string mimeType, CancellationToken cancellationToken);
}

/// <summary>
/// Uses Jellyfin's registered image encoder to decode pixels without adding a
/// second native codec to the plugin package. Temporary files are always removed.
/// </summary>
public sealed class JellyfinCoverImageDecoder : ICoverImageDecoder
{
    private readonly IImageEncoder _encoder;
    private readonly string _directory;

    /// <summary>Initializes a new instance of the <see cref="JellyfinCoverImageDecoder"/> class.</summary>
    /// <param name="encoder">Jellyfin's real host image encoder.</param>
    /// <param name="paths">Jellyfin application paths.</param>
    public JellyfinCoverImageDecoder(IImageEncoder encoder, IApplicationPaths paths)
    {
        _encoder = encoder;
        _directory = Path.Combine(paths.TempDirectory, "musichoarderz-image-validation");
    }

    /// <inheritdoc />
    public async Task<bool> CanDecodeAsync(ReadOnlyMemory<byte> content, string mimeType, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var extension = mimeType switch
        {
            "image/png" => ".png",
            "image/jpeg" => ".jpg",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            _ => null,
        };
        if (extension is null)
        {
            return false;
        }

        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, Guid.NewGuid().ToString("N") + extension);
        try
        {
            var options = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.Asynchronous,
            };
            if (!OperatingSystem.IsWindows())
            {
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }

            using (var file = new FileStream(path, options))
            {
                await file.WriteAsync(content, cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            // Unlike a size/header probe, this asks Jellyfin's encoder to decode
            // actual pixels. Its Skia implementation downsamples to at most 128².
            var hash = _encoder.GetImageBlurHash(1, 1, path);
            cancellationToken.ThrowIfCancellationRequested();
            return !string.IsNullOrEmpty(hash);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // Upstream decoder diagnostics/URLs never enter API responses.
            return false;
        }
        finally
        {
            File.Delete(path);
        }
    }
}
