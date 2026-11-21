# MusicHoarderz Cover & Metadata Provider

MusicHoarderz adds a manual cover picker and an optional, administrator-configured JSON cover/album source to Jellyfin. It does not modify audio tags or files in the media library.

## Release history

All historical `0.1.0.x` versions are **non-functional previews/baselines**, not a complete metadata provider. Their tags and releases are retained for historical reference. Issue #42 originally selected `0.2.0.0` as the first functional release target; the repository has since advanced to `1.0.0.1`. Do not downgrade the current plugin to satisfy an obsolete version number.

The `dev` fixes described here are development changes, not a claim that an existing release asset contains them. Installation, upgrade and uninstall on a real Jellyfin server remain release acceptance checks.

## Official COV picker

The official service is **https://covers.musichoarders.xyz/**. Its internal API is not a supported integration surface. This plugin neither calls that API nor scrapes the site.

1. Open the plugin configuration page as a Jellyfin administrator.
2. Enter the artist, album and country.
3. Choose **Offiziellen COV-Picker öffnen**.
4. Select the artwork on the official website, download it and import it using Jellyfin's normal image upload dialog.

The launcher passes only the documented `theme`, `country`, `artist` and `album` query parameters. Selecting, downloading and applying a COV cover remain explicit user actions. Browser remote-protocol callbacks and automatic COV artwork retrieval are not implemented.

See [COV integration policy](../../docs/COV_INTEGRATION_POLICY.md).

## Optional JSON provider

A separate service that explicitly implements the following contract can be configured in **Optionaler JSON-Provider**. This contract describes this plugin's adapter; it is **not an assertion that the official COV website offers these endpoints**.

The base URL must be public HTTPS without user information, query string or fragment. A search sends:

```http
GET /api/covers/search?title=Album&artist=Artist&country=DE&year=2024
Accept: application/json
```

`year` is omitted when unknown; all query values are URL encoded. A provider may return an array, or an object containing an array under `results`, `covers`, `items` or `data`:

```json
{
  "results": [
    {
      "url": "https://your-public-image-host.example/album.png",
      "title": "Album",
      "artist": "Artist",
      "year": 2024,
      "width": 1200,
      "height": 1200,
      "mimeType": "image/png",
      "source": "your-provider",
      "sizeBytes": 240000,
      "providerIds": { "MusicBrainzAlbum": "release-id" }
    }
  ]
}
```

Missing metadata remains missing. Manual apply resolves the exact previously selected snapshot; it does not run a new search with Jellyfin's changed canonical title. The identifier includes the source endpoint and full candidate metadata, so shared artwork URLs cannot silently replace a prior selection. Expired or evicted selections require a fresh search. The album adapter uses stable opaque identifiers and never substitutes search text for source-delivered metadata when applying a selection. Implementations and tests use `ICoverProvider` and `IAlbumMetadataProvider`; the Jellyfin providers do not interpret service-specific JSON.

Searches have a 20-second total deadline, a 2 MiB streaming response limit, at most three redirects, up to 100 normalized results and a ten-minute cache capped at 200 searches and an estimated 8 MiB. Selected album snapshots have a separate ten-minute, 500-entry/8 MiB bound. HTTP 429/5xx, invalid JSON/MIME, invalid endpoints and network failures return empty results with stable diagnostic codes. Caller cancellation is preserved.

## Jellyfin integration and manual operation

- Enable MusicHoarderz in the library's image/metadata provider list as appropriate. Jellyfin discovers the remote provider implementations; provider order is `1`.
- In an album's **Images → Search** dialog, choose **MusicHoarderz**. The later artist/artist-backdrop support is preserved.
- Album metadata is available through Jellyfin's manual identification search when a compatible JSON provider is configured.
- Background scans and ordinary refreshes do not search or apply this provider's metadata or covers. An old `AutoSearchEnabled=true` setting does not bypass this restriction. Automatic apply is unavailable in this MVP.
- Metadata selection applies only fields actually supplied by the selected source candidate. Jellyfin's own identify/replace dialog may replace other metadata according to the options the administrator chooses; this plugin does not perform file/tag writes.
- Administrator-only manual operation is the default. `AllowManualSearch=false` disables searches. The optional `AllowNonAdminManualSearch` setting permits searches from authenticated non-admin requests; Jellyfin still enforces its native administrative permission for applying images and identifying media.

## Safe image handling

Only public HTTPS image URLs are downloaded. HTTP clients disable automatic redirects, cookies and proxies; every connection resolves and validates DNS and then connects directly to one of those checked IP addresses while retaining normal TLS hostname/certificate validation. Private, loopback, local, multicast, reserved and IPv4-mapped private addresses are blocked, including after redirects.

Downloads are capped at 15 MiB and 20 seconds, including streaming bodies. MIME must match the file signature; image containers and dimensions are checked before bytes are exposed. Only static images are accepted: APNG, animated GIF and animated WebP are rejected, and GIF frame rectangles must fit their bounded logical screen. Configured minimum dimensions, a 16,384-pixel edge limit and a 64-megapixel limit apply. The validator then calls Jellyfin's registered image encoder to decode actual pixels through a small blur-hash calculation. An empty/failed decode is rejected. This reuses the host codec without packaging a second native dependency; temporary files are created with private permissions and removed in a `finally` block. Offline tests mock this host decoder boundary; real codec behavior belongs to the Jellyfin runtime checks.

**Native apply uses the validated bytes.** Jellyfin's normal image-download route can bypass a provider's `GetImageResponse`, so search results contain a local capability URL rather than the upstream URL. `/Plugins/MusicHoarderzProvider/Images/{ticket}` serves only an already validated byte buffer; it accepts no source URL and never fetches a cache miss. Tokens are random, expire after ten minutes, and are bounded by 16 images and 32 MiB. One search response shares that byte budget and omits further oversized candidates before they could evict earlier URLs in the same response. Expired/evicted links return 404 and require a new search. This endpoint intentionally needs no user-token forwarding so Jellyfin can fetch it internally. It does not expose credentials or arbitrary files.

The local URL uses the request's scheme, host and Jellyfin base path. Reverse proxies must forward the correct public origin, and the Jellyfin server must be able to reach its own public URL for native downloads.

## Credentials

All configuration, status, preview, save, test and delete endpoints require Jellyfin's `RequiresElevation` policy. Status returns masked values/configured flags. Spotify secrets and YouTube keys are encrypted with AES-GCM; the key is atomically stored in the plugin data directory with owner-only Unix permissions. Existing damaged keys are not silently replaced. Back up both the encrypted plugin configuration and `credentials.key`; losing the key makes saved secrets unrecoverable.

Blank credential inputs keep saved values. Deletion is explicit. Connection-test credentials are never persisted automatically, embedded in query strings, or included in request/exception logs. Credential HTTP clients do not follow redirects.

Spotify and YouTube search fallbacks are **future features**. The current plugin can store and test those credentials but does not claim to search those APIs as fallbacks. The separate SpotifyProvider plugin is independent.

## Validation

Provider tests use fake HTTP handlers, DNS results, provider interfaces and generated PNG/JPEG/GIF/WebP fixtures. They cover successful/empty/error searches, cancellation, limits, redirects and private DNS, MIME/container mismatches, deterministic scoring and external IDs, manual permissions, non-destructive metadata, credential restart/concurrency, and serving cached bytes to native apply without another upstream download.

```bash
dotnet build plugins/MusicHoarderzProvider/MusicHoarderzProvider.csproj -c Release
dotnet test tests/Jellyfin.Plugins.Tests/Jellyfin.Plugins.Tests.csproj -c Release --filter 'FullyQualifiedName~MusicHoarderz'
```

Real COV/provider connectivity, Jellyfin catalog installation and full server upgrade/uninstall checks are separate from these offline regression tests.
