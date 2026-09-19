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

## Current backend

- Buildable Jellyfin plugin project.
- Plugin configuration page.
- Authenticated, claim-based per-user settings endpoints.
- Injected, atomic per-user settings persistence.
- `GET /Plugins/BetterMusicDisplay/Overview`.
- `GET /Plugins/BetterMusicDisplay/Albums` with bounded paging.
- Album query options: `startIndex`, `limit`, `parentId`, `searchTerm`, `sortBy`, `sortOrder`, `isFavorite`, `missingCover`, `genre`, `year` and `fields`.
- Normal album pages are limited to 200 results.
- Missing-cover queries inspect at most 1,000 underlying albums per request in chunks of 200 and return `nextStartIndex` for continuation.
- Queries run with Jellyfin's authenticated user context, so library visibility and parental restrictions remain active.

## Album response

The Albums API returns only the fields needed by the MVP grid: item id, title, album artist, year, optional date/genres, primary-image state and current-user favorite state. `filteredTotalRecordCount` is exact for direct filters; for missing-cover queries it remains nullable unless the bounded scan reaches the end.

## Release definition

`0.1.0.0` is complete only when the Albums view can be enabled for a music library, loads in bounded batches, preserves native Jellyfin navigation/playback and restores the native view after an initialization error, plugin disablement or uninstall.

## Build

```bash
dotnet build plugins/BetterMusicDisplay/BetterMusicDisplay.csproj -c Release
```
