# Implementation Status

## Current repository shape

The repository is a three-plugin Jellyfin plugin library:

1. `HddDisplay`
2. `BetterMusicDisplay`
3. `MusicHoarderzProvider`

## HDD Display

### HDD Display done

- H1 mount discovery backend.
- Linux mount resolution through `/proc/self/mountinfo`.
- Windows and restricted-environment fallback through `DriveInfo`.
- Per-library-path mount diagnostics.
- H2 media usage aggregation backend.
- Real file-size aggregation by resolved mount and media type.
- Configurable storage scan cache.
- Forced storage scan refresh through `refresh=true`.
- Explicit cache clear endpoint.
- H3 NVIDIA telemetry provider.
- `nvidia-smi` GPU usage snapshot.
- GPU memory usage and process list.
- Jellyfin ffmpeg process detection.
- H4 dashboard data endpoint.
- Dashboard widget rendering for storage, media segments and GPU status.
- Manual dashboard refresh button.

### HDD Display open

- Validate build results from GitHub Actions.
- Test on Docker, LXC and native Linux.
- Test on NVIDIA and non-NVIDIA hosts.
- Improve large-library scan performance if needed.
- Add final Admin Dashboard placement tuning against a real Jellyfin dashboard.
- Add screenshots and release notes.

## Better MusicDisplay

### Better MusicDisplay done

- Plugin metadata.
- Buildable baseline project.
- Plugin page.
- Basic overview endpoint.
- Initial global and user settings models.
- B1 user settings persistence service.
- Per-user settings endpoints for get, save and reset.
- Header-based self-user guard for settings access.
- User customization blocking when global customization is disabled.

### Better MusicDisplay open

- Strong Jellyfin admin-role integration for cross-user management.
- Music query API layer.
- Album endless-scroll MVP.
- Artists and album artists.
- Playlists and genres.
- Virtual songs table.
- Suggestions start page.
- Full UI fallback and release stabilization.

## MusicHoarderz Cover & Metadata Provider

### MusicHoarderz Provider done

- Plugin metadata.
- Buildable baseline project.
- Plugin page.
- Status endpoint that avoids returning secrets.
- Initial configuration models for MusicHoarderz/COV, Spotify and YouTube.

### MusicHoarderz Provider open

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
