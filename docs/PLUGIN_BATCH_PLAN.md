# Three-Plugin Batch Plan

## Scope

The repository contains three Jellyfin plugins that are developed independently but share build, manifest and release automation:

1. HDD Display
2. Better MusicDisplay
3. MusicHoarderz Cover & Metadata Provider

The repository is expanded as a plugin library. HDD Display remains the first plugin and is not replaced by the music plugins.

---

# Plugin 1: HDD Display

## Batch H1: Mount discovery backend

Goal: replace simple `/mnt` and `/media` path splitting with reliable mount resolution.

Tasks:

- Add `MountResolver` service.
- Parse `/proc/self/mountinfo` on Linux.
- Normalize Jellyfin library paths.
- Choose deepest matching mount.
- Keep `DriveInfo` fallback for Windows or restricted containers.
- Return diagnostics for unmatched paths.

Acceptance:

- Docker bind mounts resolve to the correct mount.
- LXC passthrough paths resolve to the correct mount.
- Unmatched paths are visible in diagnostics.
- Existing `/Plugins/HddDisplay/Storage` remains backward compatible until the new endpoint is used.

## Batch H2: Storage usage aggregation

Goal: show real byte usage per media type instead of visual placeholders.

Tasks:

- Add `MediaUsageAggregator`.
- Read Jellyfin virtual folders and indexed media paths.
- Aggregate bytes by mount and media type.
- Support movies, tvshows, music, video, other and free.
- Add cache to avoid full recalculation on every dashboard load.

Acceptance:

- Bars use real byte counts.
- Free space is calculated per mount.
- Missing or inaccessible paths do not break the response.
- Cache TTL is configurable.

## Batch H3: NVIDIA telemetry

Goal: expose live GTX 1060 / `jellyfin-ffmpeg` status.

Tasks:

- Add `GpuUsageProvider` interface.
- Add `NvidiaSmiGpuUsageProvider`.
- Query GPU, encoder, decoder and VRAM usage.
- Detect active `jellyfin-ffmpeg` processes.
- Return unavailable state if `nvidia-smi` is missing.

Acceptance:

- Telemetry works when `nvidia-smi` is available.
- Missing telemetry does not break storage display.
- Response states provider, GPU name, process count and VRAM.

## Batch H4: Admin Dashboard widget

Goal: render HDD Display directly on the Jellyfin Admin Dashboard.

Tasks:

- Add `/Plugins/HddDisplay/AdminDashboard/Overview`.
- Render compact storage cards.
- Render media type stacked bars.
- Render GPU status card.
- Replace or augment the `Pfade` / `Paths` card.
- Fallback insert if direct replacement fails.

Acceptance:

- Widget appears on Admin Dashboard.
- Three-disk layout is readable.
- GPU unavailable state is clean.
- No plugin-settings-page dependency for the primary feature.

## Batch H5: HDD Display v1.0 stabilization

Goal: make HDD Display release-ready.

Tasks:

- Add diagnostics page.
- Add logging and error states.
- Validate Docker/LXC/native Linux.
- Validate NVIDIA and no-NVIDIA systems.
- Update README and screenshots.
- Trigger auto-publish release.

Acceptance:

- Auto-release creates `HddDisplay-v...`.
- ZIP contains plugin artifacts only.
- Manifest entry updates only HDD Display.

---

# Plugin 2: Better MusicDisplay

## Batch B1: Foundation and settings

Goal: create a stable plugin foundation with global and per-user settings.

Tasks:

- Add admin configuration page.
- Add `PluginConfiguration`.
- Add `UserMusicViewSettings`.
- Add `UserSettingsService`.
- Add default layout handling.
- Add admin/user permission checks.

Acceptance:

- Admin can enable/disable plugin globally.
- Admin can set defaults for new users.
- Users can save their own layout if allowed.
- User settings are isolated by Jellyfin user id.

## Batch B2: Query API layer

Goal: provide reusable APIs for dynamic music views.

Tasks:

- Add `MusicQueryService`.
- Add paged endpoints for Albums, Suggestions, Artists, Album Artists, Playlists, Songs and Genres.
- Support `startIndex`, `limit`, `searchTerm`, `sortBy`, `sortOrder`, `filters` and `fields`.
- Add cache boundaries and cancellation support.

Acceptance:

- Every view can load data in batches.
- Search, filters and sorting can be combined.
- Large libraries are never loaded in one request.

## Batch B3: Album view MVP

Goal: replace the album page with the first real endless-scroll UI.

Tasks:

- Add album grid component.
- Add live search with debounce.
- Add filter chips for favorites, missing cover, genre and year.
- Add sort controls.
- Add cover lazy loading.
- Add scroll state restore.

Acceptance:

- No visible pagination in album view.
- 100 item default batch size.
- Additional albums load near scroll end.
- Running requests are cancelled on new search.

## Batch B4: Artists and album artists

Goal: replace artist and album-artist views.

Tasks:

- Add artist grid/list component.
- Add alphabet navigation.
- Add sticky search.
- Add cover collage fallback.
- Add album-artist deduplication strategy.

Acceptance:

- Artists and album artists are separate views.
- Alphabet navigation does not use page changes.
- Similar album-artist variants are grouped predictably.

## Batch B5: Playlists and genres

Goal: improve library navigation beyond albums and artists.

Tasks:

- Add playlist cards.
- Add playlist cover mosaic.
- Add track count and runtime summary.
- Add genre cards.
- Add genre detail view with top albums, artists and random songs.

Acceptance:

- Playlists and genres use dynamic loading.
- Genre details render independent sections.
- Missing covers use deterministic fallbacks.

## Batch B6: Virtual songs table

Goal: make large song libraries usable.

Tasks:

- Add virtualized song table.
- Add configurable columns.
- Add sortable columns.
- Add compact mobile row layout.
- Add multi-select groundwork.

Acceptance:

- 10,000+ songs are scrollable without rendering all DOM nodes.
- Column choices persist per user.
- Search and filter changes reset the virtual window cleanly.

## Batch B7: Suggestions start page

Goal: build a modern music landing page.

Tasks:

- Add sections for recently added, last played, often played, random albums, missing covers and genre-based recommendations.
- Allow users to enable, disable and reorder sections.
- Load sections independently.
- Add section-level error states.

Acceptance:

- One broken section does not break the page.
- Section choices persist per user.
- Suggestions can be default landing page.

## Batch B8: Better MusicDisplay v1.0 stabilization

Goal: make the plugin release-ready.

Tasks:

- Add fallback to default Jellyfin views.
- Test permissions.
- Test mobile and desktop layouts.
- Test dark theme compatibility.
- Add docs and screenshots.
- Trigger auto-publish release.

Acceptance:

- Main music views are dynamic.
- User customization works.
- Auto-release creates `BetterMusicDisplay-v...`.

---

# Plugin 3: MusicHoarderz Cover & Metadata Provider

## Batch M1: Admin-only configuration

Goal: safely store provider settings and secrets.

Tasks:

- Add admin settings page.
- Add MusicHoarderz/COV settings.
- Add Spotify Client ID and encrypted Client Secret settings.
- Add YouTube encrypted API key settings.
- Add masked secret display.
- Add server-side admin checks.

Acceptance:

- Normal users cannot read, save, test or delete provider credentials.
- Secrets are never returned in clear text.
- Status endpoint only returns configured flags.

## Batch M2: Provider abstraction

Goal: keep COV, Spotify and YouTube isolated.

Tasks:

- Add `ICoverSearchProvider`.
- Add `IMetadataProvider`.
- Add `CoverSearchRequest` and `CoverSearchResult`.
- Add provider health result.
- Add fake provider for tests.

Acceptance:

- Providers can be enabled and disabled independently.
- Controller code does not know provider internals.
- Missing provider failure does not break the whole search.

## Batch M3: MusicHoarderz/COV MVP

Goal: search covers through MusicHoarderz/COV.

Tasks:

- Implement COV provider behind interface.
- Support base URL, country, source priority and max results.
- Normalize returned cover data.
- Cache provider responses.
- Add diagnostic output for API instability.

Acceptance:

- Manual album cover search returns normalized candidates.
- Provider failures are visible but non-fatal.
- No fragile scraping is hardwired into core services.

## Batch M4: Matching score and review queue

Goal: rate cover results before automatic apply.

Tasks:

- Add score factors for album title, album artist, year, resolution, square ratio and source priority.
- Default auto-apply minimum score: 85.
- Route scores 60-84 to review queue.
- Ignore low-confidence results below 60.

Acceptance:

- Every result has a score and explanation.
- Admin can approve, reject or retry queued items.
- Rejected items are not immediately suggested again.

## Batch M5: Jellyfin-only cover apply

Goal: safely apply covers without touching media files.

Tasks:

- Download and validate selected image.
- Set Jellyfin primary image.
- Update cache.
- Add apply result reporting.
- Add permission handling for non-admin apply if enabled.

Acceptance:

- Default write mode is Jellyfin-only.
- Media files are not changed.
- Failed downloads do not change item state.

## Batch M6: Spotify metadata fallback

Goal: add Spotify album/track metadata and cover fallback.

Tasks:

- Implement Spotify token service using Client Credentials.
- Cache access token server-side.
- Add album and track search.
- Add 429 handling and backoff.
- Add connection test button.

Acceptance:

- Client Secret is never returned to browser.
- Spotify can be used as fallback or metadata source.
- Rate limits pause provider cleanly.

## Batch M7: YouTube metadata fallback

Goal: add YouTube fallback metadata for tracks/videos.

Tasks:

- Implement YouTube client.
- Use admin-configured API key.
- Support region and language settings.
- Add quota-safe limits.
- Add connection test button.

Acceptance:

- YouTube key is masked.
- Provider can be disabled without side effects.
- Quota errors are reported and cached.

## Batch M8: Scheduled tasks

Goal: automate missing-cover discovery.

Tasks:

- Add scheduled task for missing covers.
- Add dry-run mode.
- Add review-only mode.
- Add scan report.
- Respect cache and rate limits.

Acceptance:

- Task appears in Jellyfin Scheduled Tasks.
- Dry run writes nothing.
- Report includes scanned, matched, applied, review and error counts.

## Batch M9: Folder image write mode

Goal: optionally write `cover.jpg` or `folder.jpg`.

Tasks:

- Add `FolderImage` mode.
- Validate library path boundaries.
- Check write permissions.
- Configure file name.
- Avoid overwrite unless allowed.

Acceptance:

- Plugin never writes outside the music library path.
- Admin opt-in is required.
- Existing files are preserved by default.

## Batch M10: Audio tag writing

Goal: optionally embed cover art in audio files.

Tasks:

- Add explicit admin warning.
- Add dry run and report.
- Integrate tagging library after evaluation.
- Support MP3 and FLAC first.
- Add per-file error handling.

Acceptance:

- Tagging is disabled by default.
- Admin must explicitly enable it.
- Failed files do not stop the entire job.
- Report lists every changed file.

## Batch M11: MusicHoarderz Provider v1.0 stabilization

Goal: release the provider safely.

Tasks:

- Security review.
- Secret redaction tests.
- Provider failure tests.
- Cache tests.
- Review queue tests.
- Scheduled task tests.
- Documentation for Spotify, YouTube and write modes.
- Trigger auto-publish release.

Acceptance:

- Auto-release creates `MusicHoarderzProvider-v...`.
- Secrets remain protected.
- Jellyfin-only apply and review queue are stable.
