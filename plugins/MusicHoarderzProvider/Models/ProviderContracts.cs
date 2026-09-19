using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.MusicHoarderzProvider.Models;

/// <summary>
/// Cover search query for a single album lookup.
/// </summary>
/// <param name="Title">Album title.</param>
/// <param name="Artist">Album artist.</param>
/// <param name="Year">Release year, when known.</param>
/// <param name="Country">Preferred country code (ISO-3166-1 alpha-2).</param>
public sealed record CoverSearchQuery(string Title, string Artist, int? Year, string Country);

/// <summary>
/// Normalized cover result returned by a cover provider.
/// </summary>
/// <param name="Url">Absolute https cover image URL.</param>
/// <param name="Width">Image width in pixels, when known.</param>
/// <param name="Height">Image height in pixels, when known.</param>
/// <param name="Source">Provider source name (for example "musichoarderz").</param>
/// <param name="MimeType">Image MIME type, when known.</param>
/// <param name="Score">Deterministic match score from 0 to 100.</param>
/// <param name="ScoreReason">Human readable score breakdown.</param>
/// <param name="Title">Album title delivered with the result, when known.</param>
/// <param name="Artist">Album artist delivered with the result, when known.</param>
/// <param name="Year">Release year delivered with the result, when known.</param>
public sealed record NormalizedCoverResult(string Url, int? Width, int? Height, string Source, string? MimeType, int Score, string ScoreReason, string? Title = null, string? Artist = null, int? Year = null);

/// <summary>
/// Structured provider error.
/// </summary>
/// <param name="Code">Stable machine readable error code.</param>
/// <param name="Message">Human readable error message without secrets.</param>
public sealed record ProviderError(string Code, string Message);

/// <summary>
/// Cover provider contract used by the image and metadata providers.
/// </summary>
public interface ICoverProvider
{
    /// <summary>
    /// Searches covers for the given query.
    /// </summary>
    /// <param name="query">Cover search query.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Normalized cover results (never null, fail-open).</returns>
    Task<IReadOnlyList<NormalizedCoverResult>> SearchAsync(CoverSearchQuery query, CancellationToken cancellationToken);
}

/// <summary>
/// In-memory fake cover provider for tests and offline development.
/// </summary>
public sealed class FakeCoverProvider : ICoverProvider
{
    /// <summary>
    /// Gets the canned results returned by search calls.
    /// </summary>
    public Collection<NormalizedCoverResult> Results { get; } = new();

    /// <summary>
    /// Gets the last query received, if any.
    /// </summary>
    public CoverSearchQuery? LastQuery { get; private set; }

    /// <inheritdoc />
    public Task<IReadOnlyList<NormalizedCoverResult>> SearchAsync(CoverSearchQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        LastQuery = query;
        IReadOnlyList<NormalizedCoverResult> snapshot = new System.Collections.Generic.List<NormalizedCoverResult>(Results);
        return Task.FromResult(snapshot);
    }
}
