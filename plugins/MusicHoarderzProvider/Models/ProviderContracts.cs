using System;
using System.Collections.Generic;
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
/// <param name="ProviderIds">Known external identifiers, when available.</param>
public sealed record CoverSearchQuery(string Title, string Artist, int? Year, string Country, IReadOnlyDictionary<string, string>? ProviderIds = null);

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
/// <param name="ProviderIds">External identifiers actually supplied by the source.</param>
/// <param name="SizeBytes">Declared image size, never trusted for download limits.</param>
public sealed record NormalizedCoverResult(string Url, int? Width, int? Height, string Source, string? MimeType, int Score, string ScoreReason, string? Title = null, string? Artist = null, int? Year = null, IReadOnlyDictionary<string, string>? ProviderIds = null, long? SizeBytes = null);

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

/// <summary>Provider-independent album metadata returned for manual selection.</summary>
/// <param name="Id">Stable source identifier.</param>
/// <param name="Title">Title actually supplied by the source.</param>
/// <param name="Artist">Album artist actually supplied by the source.</param>
/// <param name="Year">Release year actually supplied by the source.</param>
/// <param name="ProviderIds">Known external identifiers.</param>
/// <param name="ImageUrl">Optional cover URL.</param>
public sealed record NormalizedAlbumResult(string Id, string? Title, string? Artist, int? Year, IReadOnlyDictionary<string, string> ProviderIds, string? ImageUrl);

/// <summary>Metadata contract separate from the cover transport and Jellyfin models.</summary>
public interface IAlbumMetadataProvider
{
    /// <summary>Searches normalized albums without modifying media.</summary>
    /// <param name="query">Search input.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Normalized album candidates.</returns>
    Task<IReadOnlyList<NormalizedAlbumResult>> SearchAlbumsAsync(CoverSearchQuery query, CancellationToken cancellationToken);
}

/// <summary>Structured health without credentials or raw upstream exception text.</summary>
/// <param name="Available">Whether the last completed lookup succeeded.</param>
/// <param name="Error">The last stable error, if any.</param>
public sealed record ProviderHealth(bool Available, ProviderError? Error);

/// <summary>In-memory album provider for offline integration tests.</summary>
public sealed class FakeAlbumMetadataProvider : IAlbumMetadataProvider
{
    /// <summary>Gets the test results.</summary>
    public Collection<NormalizedAlbumResult> Results { get; } = new();

    /// <inheritdoc />
    public Task<IReadOnlyList<NormalizedAlbumResult>> SearchAlbumsAsync(CoverSearchQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<NormalizedAlbumResult>>(new List<NormalizedAlbumResult>(Results));
    }
}
