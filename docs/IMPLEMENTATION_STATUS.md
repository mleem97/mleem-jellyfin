# Implementation status

## Repository inventory

The solution contains eight independent plugins: `HddDisplay`, `MusicSuite`, `MusicHoarderzProvider`, `SpotifyProvider`, `GracenoteProvider`, `MusicToolkit`, `MediaTools` and `MetadataDashboard`. Older planning documents call MusicSuite **Better MusicDisplay**; that is the same continuing plugin GUID.

Each plugin owns its metadata, source and version. Shared build/release tooling discovers `plugins/*/plugin.json`, validates project registration and package metadata, and builds every plugin. `dev` is the integration branch; publishing remains a separate manual operation from `main`.

## Implemented areas

| Area | Source and verification |
|---|---|
| HDD storage/mount/system/GPU services and admin authorization | `plugins/HddDisplay`, .NET tests under `tests/Jellyfin.Plugins.Tests`, UI smoke tests under `tests/dashboard-widget` |
| Album queries, endless scrolling, search/filter/sort, per-user settings and fallback | `plugins/MusicSuite`, matching .NET tests and `tests/music-suite` |
| Provider contracts, encrypted credentials, configured JSON-provider lookup, official COV picker, scoring, image validation and manual integration | `plugins/MusicHoarderzProvider`, `MusicHoarderz*Tests.cs`; see native metadata caveat below |
| Release packages, catalog isolation, checksum verification and plugin discovery | `.github/scripts`, `tests/release-tooling` |
| Separate plugin releases and reproducible dependency versions | per-plugin metadata, `Directory.Packages.props`, package lockfiles and the manual release workflow |

This inventory identifies code and tests. It does not assert that every UI flow or hardware configuration has been exercised on a deployed server.

## Remaining release qualification

The historical implementation tracker is issue [#52](https://github.com/mleem97/mleem-jellyfin/issues/52). Source fixes and successful automated checks can complete implementation issues; release issues require their deployment evidence as well.

| Issue | Historical target | Current published evidence | Remaining acceptance |
|---|---|---|---|
| [#31](https://github.com/mleem97/mleem-jellyfin/issues/31) | HDD Display 0.1.0.0 | Later 1.0.0.22 ZIP exists, checksum verified, actual assembly targets .NET 10 | Native/Docker/LXC and NVIDIA/non-NVIDIA qualification; real dashboard screenshot; install/update/removal confirmation |
| [#41](https://github.com/mleem97/mleem-jellyfin/issues/41) | Better MusicDisplay 0.1.0.0 | MusicSuite 0.1.0.0 and 1.0.0.0 ZIPs exist and were downloaded/verified | Real desktop/mobile screenshots and install/removal/native-view restoration evidence |
| [#51](https://github.com/mleem97/mleem-jellyfin/issues/51) | MusicHoarderz 0.2.0.0 | Later 1.0.0.1 ZIP exists, checksum verified | Manual image/metadata dialog on a real server and upgrade from the preview baseline |

Existing 1.x tags are retained. The repository does not republish lower historical target versions to simulate completion. [Release Readiness](RELEASE_READINESS.md) records the artifact evidence and the boundaries of local verification.

Two provider requirements also remain qualified: #45 cannot use the official COV
internal API under the repository's integration policy, and #47 cannot infer
non-destructive native identify behavior from sparse provider results. Jellyfin 12.2
explicitly requests full metadata replacement during that action. These limitations
remain tracked instead of being hidden by passing offline tests.

## CI infrastructure observation

On 2026-11-01, the public main check annotations were read and explicitly identify an
account billing lock as the reason jobs did not start. The absent logs were a consequence
of the pre-step failure. The owner must resolve GitHub's billing lock if it remains on
new runs; local build/test results are reported separately from hosted Actions status.
