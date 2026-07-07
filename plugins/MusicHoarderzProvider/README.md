# MusicHoarderz Cover & Metadata Provider

> Jellyfin music cover and metadata provider using MusicHoarderz/COV with optional Spotify and YouTube fallback.

## Purpose

MusicHoarderz Provider is the third plugin in this Jellyfin plugin library. HDD Display remains the first plugin; Better MusicDisplay is the second plugin.

## Planned Scope

- Search album and single covers through MusicHoarderz/COV.
- Admin-only Spotify Client ID and Client Secret configuration.
- Admin-only YouTube API key configuration.
- Matching score for automatic cover decisions.
- Review queue for uncertain results.
- Safe Jellyfin-only apply mode by default.
- Optional folder image and audio tag writing after explicit admin opt-in.

## Current Baseline

- Buildable Jellyfin plugin project.
- Admin configuration page.
- `GET /Plugins/MusicHoarderzProvider/Status` endpoint.
- Status endpoint redacts secrets and only returns configured flags.

## Build

```bash
dotnet build plugins/MusicHoarderzProvider/MusicHoarderzProvider.csproj -c Release
```
