# Implementation Status

## Current repository shape

The repository is a three-plugin Jellyfin plugin library:

1. `HddDisplay`
2. `BetterMusicDisplay`
3. `MusicHoarderzProvider`

## HDD Display

### Done

- H1 mount discovery backend.
- Linux mount resolution through `/proc/self/mountinfo`.
- Windows and restricted-environment fallback through `DriveInfo`.
- Per-library-path mount diagnostics.
- H2 media usage aggregation backend.
- Real file-size aggregation by resolved mount and media type.
- Configurable storage scan cache.
- H3 NVIDIA telemetry provider.
- `nvidia-smi` GPU usage snapshot.
- GPU memory usage and process list.
- Jellyfin ffmpeg process detection.
- H4 dashboard data endpoint.
- Dashboard widget rendering for storage, media segments and GPU status.

### Open

- Validate build results from GitHub Actions.
- Test on Docker, LXC and native Linux.
- Test on NVIDIA and non-NVIDIA hosts.
- Add forced refresh endpoint for storage scan cache.
- Improve large-library scan performance if needed.
- Add final Admin Dashboard placement tuning against a real Jellyfin dashboard.
- Add screenshots and release notes.

## Better MusicDisplay

### Done

- Plugin metadata.
- Buildable baseline project.
- Plugin page.
- Basic overview endpoint.
- Initial global and user settings models.

### Open

- User settings persistence service.
- Admin and user permission checks.
- Music query API layer.
- Album endless-scroll MVP.
- Artists and album artists.
- Playlists and genres.
- Virtual songs table.
- Suggestions start page.
- Full UI fallback and release stabilization.

## MusicHoarderz Cover & Metadata Provider

### Done

- Plugin metadata.
- Buildable baseline project.
- Plugin page.
- Status endpoint that avoids returning secrets.
- Initial configuration models for MusicHoarderz/COV, Spotify and YouTube.

### Open

- Admin-only save/test/delete endpoints for credentials.
- Secret masking and redaction across UI, logs and diagnostics.
- Provider abstraction.
- MusicHoarderz/COV implementation.
- Matching score.
- Review queue.
- Jellyfin-only cover apply.
- Spotify fallback.
- YouTube fallback.
- Scheduled tasks.
- Optional folder image writing.
- Optional audio tagging.
