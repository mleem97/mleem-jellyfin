# Better MusicDisplay

> Enhanced Jellyfin music UI with endless scroll, live search and per-user layouts.

## Purpose

Better MusicDisplay is the second plugin in this Jellyfin plugin library. It extends the music experience while HDD Display remains the first storage and dashboard plugin.

## Planned Scope

- Endless scroll for Albums, Suggestions, Artists, Album Artists, Playlists, Songs and Genres.
- Live search with debounce and request cancellation.
- Better filters and sort modes.
- Per-user layout settings.
- Virtualized song table for large libraries.

## Current Baseline

- Buildable Jellyfin plugin project.
- Plugin configuration page.
- `GET /Plugins/BetterMusicDisplay/Overview` endpoint.
- `GET /Plugins/BetterMusicDisplay/Users/{userId}/Settings` endpoint.
- `PUT /Plugins/BetterMusicDisplay/Users/{userId}/Settings` endpoint.
- `DELETE /Plugins/BetterMusicDisplay/Users/{userId}/Settings` endpoint.
- Per-user settings persistence in the plugin data folder.
- Header-based self-user guard for user settings access.

## Release Readiness

- Included in the shared plugin release workflow.
- Release metadata is prepared in `plugin.json`.
- Current implementation is a baseline release and not the final full music UI replacement.

## Build

```bash
dotnet build plugins/BetterMusicDisplay/BetterMusicDisplay.csproj -c Release
```
