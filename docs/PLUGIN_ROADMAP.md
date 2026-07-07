# Plugin Roadmap

## Repository Direction

This repository is Marvin's Jellyfin plugin library. It is expanded plugin-by-plugin and does not replace earlier plugins unless explicitly decided.

## Plugin 1: HDD Display

Purpose: Admin Dashboard storage overview and NVIDIA `jellyfin-ffmpeg` telemetry.

Implementation order:

1. Resolve Jellyfin library paths to mounts.
2. Aggregate real byte usage by media type.
3. Cache storage calculations.
4. Add NVIDIA usage data.
5. Render the Admin Dashboard widget.

## Plugin 2: Better MusicDisplay

Purpose: replace and enhance Jellyfin music views with endless scroll, live search, modern layouts and per-user layout settings.

Implementation order:

1. Add global configuration and per-user settings.
2. Add paged music query APIs.
3. Build Album view MVP with endless scroll and live search.
4. Add Artists, Album Artists, Playlists, Songs and Genres.
5. Add Suggestions start page.
6. Stabilize UI fallback and user layout storage.

## Plugin 3: MusicHoarderz Cover & Metadata Provider

Purpose: find music covers through MusicHoarderz/COV and enrich metadata through admin-configured Spotify and YouTube credentials.

Implementation order:

1. Add admin-only provider configuration.
2. Add provider abstraction and COV MVP.
3. Add matching score and review queue.
4. Add Jellyfin-only cover apply mode.
5. Add Spotify and YouTube fallback providers.
6. Add scheduled task automation.
7. Add optional folder image and audio tagging modes.

## Packaging

Each plugin releases independently with plugin-scoped tags:

```text
HddDisplay-v1.0.0.0
BetterMusicDisplay-v0.1.0.0
MusicHoarderzProvider-v0.1.0.0
```
