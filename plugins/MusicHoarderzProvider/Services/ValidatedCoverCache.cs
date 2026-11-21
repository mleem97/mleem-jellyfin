using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.MusicHoarderzProvider.Services;

/// <summary>
/// Holds validated bytes behind short-lived, unguessable local URLs. Jellyfin's
/// native download action must never re-fetch the untrusted upstream image URL.
/// </summary>
public sealed class ValidatedCoverCache
{
    /// <summary>Maximum bytes held across all capability URLs and one search response.</summary>
    public const int MaxBytes = 32 * 1024 * 1024;

    private const int MaxImages = 16;
    private readonly Dictionary<Guid, CacheEntry> _images = new();
    private readonly TimeProvider _clock;

    /// <summary>Initializes a new instance of the <see cref="ValidatedCoverCache"/> class.</summary>
    /// <param name="clock">Optional clock used by offline expiry tests.</param>
    public ValidatedCoverCache(TimeProvider? clock = null)
    {
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>Stores a validated cover for ten minutes.</summary>
    /// <param name="image">Fully validated image bytes.</param>
    /// <returns>An opaque, random capability token.</returns>
    public Guid Add(ValidatedCoverImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (image.Content.Length > CoverImageValidator.MaxImageBytes)
        {
            throw new ArgumentException("Image exceeds the cache limit.", nameof(image));
        }

        lock (_images)
        {
            foreach (var id in _images.Where(pair => pair.Value.Expires <= _clock.GetUtcNow()).Select(static pair => pair.Key).ToArray())
            {
                _images.Remove(id);
            }

            while (_images.Count >= MaxImages || _images.Values.Sum(static entry => entry.Image.Content.Length) + image.Content.Length > MaxBytes)
            {
                _images.Remove(_images.OrderBy(static pair => pair.Value.Expires).First().Key);
            }

            var token = Guid.NewGuid();
            _images.Add(token, new CacheEntry(image, _clock.GetUtcNow().AddMinutes(10)));
            return token;
        }
    }

    /// <summary>Reserves a cover only when this response cannot evict its own earlier images.</summary>
    /// <param name="image">Validated image.</param>
    /// <param name="responseBytes">Bytes already published by this search response.</param>
    /// <returns>The new capability, or null when the response budget is exhausted.</returns>
    public Guid? AddForResponse(ValidatedCoverImage image, ref int responseBytes)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (image.Content.Length > MaxBytes - responseBytes)
        {
            return null;
        }

        var ticket = Add(image);
        responseBytes += image.Content.Length;
        return ticket;
    }

    /// <summary>Gets existing validated bytes; never downloads a cache miss.</summary>
    /// <param name="token">Opaque capability token.</param>
    /// <returns>The validated image or null after eviction/expiry.</returns>
    public ValidatedCoverImage? Get(Guid token)
    {
        lock (_images)
        {
            if (_images.TryGetValue(token, out var entry) && entry.Expires > _clock.GetUtcNow())
            {
                return entry.Image;
            }

            _images.Remove(token);
            return null;
        }
    }

    private sealed record CacheEntry(ValidatedCoverImage Image, DateTimeOffset Expires);
}
