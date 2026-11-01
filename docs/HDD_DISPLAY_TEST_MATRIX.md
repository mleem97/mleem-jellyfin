# HDD Display test matrix

## Automated coverage

| Area | Coverage |
|---|---|
| Linux mountinfo | deepest-match resolution, root mount, escaped paths, missing mount fallback |
| Media scan | real byte aggregation, cancellation, timeout result state, reparse-point exclusion |
| System scan | exclusive nested paths, cancellation, cache behavior |
| GPU | independent cache and detached snapshots; unavailable state remains non-fatal |
| Authorization | all HTTP controllers inherit Jellyfin `RequiresElevation` |
| Dashboard | JSDOM smoke test for Paths-card placement, duplicate prevention and route cleanup |
| Packaging | Release DLL/PDB ZIP smoke validation |

## Manual release qualification

The following matrix must be completed before closing the HDD Display release issue. No row is considered passed by unit tests or by the existence of a downloadable release. Record the server version, environment, plugin commit/version, result and screenshot alongside each completed row.

| Environment | Storage | System paths | GPU | Dashboard | Result |
|---|---|---|---|---|---|
| Docker with bind mounts | deepest mount and real bytes | cache/metadata/transcodes resolve inside container | NVIDIA passthrough and no-passthrough | widget survives SPA navigation | pending |
| LXC passthrough | passthrough mount selected | inaccessible paths produce diagnostics | NVIDIA passthrough if configured | fallback placement works | pending |
| Native Linux | root and nested mounts | all Jellyfin paths classified exclusively | `nvidia-smi` present/absent | Paths card augmented | pending |
| Read-only Jellyfin Web | unchanged | unchanged | unchanged | native UI remains usable without loader | pending |

## Compatibility targets

- Development source: Jellyfin Server/Web 12.2 and .NET 10.
- Existing published 1.0.0.22 package: Jellyfin 12.1 and .NET 10.
- No 10.x server compatibility is claimed for these .NET 10 packages.
- NVIDIA and non-NVIDIA hosts

## Failure cases

- Empty library list.
- Missing or unreadable library directory.
- Symbolic-link loop.
- Nested mounts.
- Cancelled HTTP request.
- Scan deadline exceeded.
- Missing, failing or hanging `nvidia-smi`.
- Widget asset loaded twice.
- Route leaves and re-enters the Admin Dashboard.
- User is authenticated but is not an administrator.
