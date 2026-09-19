# MusicHoarderz Cover & Metadata Provider

> Jellyfin music cover and metadata provider using MusicHoarderz/COV with optional Spotify and YouTube fallback.

## Purpose

MusicHoarderz Provider is the third plugin in this Jellyfin plugin library. HDD Display remains the first plugin; MusicSuite is the second plugin.

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

## Release Readiness

- Included in the shared plugin release workflow.
- Release metadata is prepared in `plugin.json`.
- Current implementation is a baseline release before the full provider stack.

> Preview note: all `0.1.0.x` versions are a non-functional preview/baseline.
> `0.2.0.0` will be the first functional release (see issue #42).

## Build

```bash
dotnet build plugins/MusicHoarderzProvider/MusicHoarderzProvider.csproj -c Release
```
