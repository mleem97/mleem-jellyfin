# Better MusicDisplay

> Enhanced Jellyfin music UI with endless-scroll albums, live search and per-user layouts.

## Purpose

Better MusicDisplay is the second plugin in this Jellyfin plugin library. It extends the music experience while HDD Display remains the storage and dashboard plugin.

## Version 0.1.0 scope

The first functional release is deliberately limited to the **Albums** view.

Included:

- paged album query API;
- endless-scroll album grid;
- debounced live search with request cancellation;
- sorting by title, album artist, year and date added;
- filters for favorites, missing covers, genre and year;
- small, medium and large per-user tile sizes;
- scroll-state restoration;
- fail-open fallback to Jellyfin's native music view.

Explicitly excluded from 0.1.0:

- Suggestions landing page;
- Artists and Album Artists replacements;
- Playlists and Genres replacements;
- virtualized Songs table;
- recommendation logic;
- multi-select and bulk operations.

Those features remain assigned to later milestones and must not delay or expand the Albums MVP.

## Current baseline

- Buildable Jellyfin plugin project.
- Plugin configuration page.
- `GET /Plugins/BetterMusicDisplay/Overview` endpoint.
- `GET /Plugins/BetterMusicDisplay/Users/{userId}/Settings` endpoint.
- `PUT /Plugins/BetterMusicDisplay/Users/{userId}/Settings` endpoint.
- `DELETE /Plugins/BetterMusicDisplay/Users/{userId}/Settings` endpoint.
- Per-user settings persistence in the plugin data folder.
- Existing header-based settings guard is temporary and is replaced before the functional release.

## Release definition

`0.1.0.0` is complete only when the Albums view can be enabled for a music library, loads in bounded batches, preserves native Jellyfin navigation/playback and restores the native view after an initialization error, plugin disablement or uninstall.

## Build

```bash
dotnet build plugins/BetterMusicDisplay/BetterMusicDisplay.csproj -c Release
```
