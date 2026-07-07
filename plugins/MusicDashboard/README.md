# Music Dashboard

> Jellyfin plugin for a cleaner music library overview.

## Purpose

Music Dashboard is the dedicated music plugin in this Jellyfin plugin library. It is separate from HDD Display so storage and music features can evolve independently.

## Current Functionality

- Adds a Jellyfin plugin page named **Music Dashboard**.
- Exposes `GET /Plugins/MusicDashboard/Overview`.
- Lists detected Jellyfin music libraries.
- Lists configured paths per music library.

## Planned Functionality

- Metadata health checks for missing artists, album artists, genres and covers.
- Duplicate-track and duplicate-album detection.
- Codec and bitrate overview.
- Recently added music overview.
- Folder structure checks.

## Build

```bash
dotnet build plugins/MusicDashboard/MusicDashboard.csproj -c Release
```
