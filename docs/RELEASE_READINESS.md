# Release readiness and artifact audit

Audit date: 2026-11-01. This record distinguishes downloaded release artifacts from validation of the current development source and from tests on an installed Jellyfin server.

## Verified published artifacts

The following ZIP files were downloaded from this repository's GitHub Releases. Each package contains a non-empty plugin DLL and PDB at the ZIP root. The MediaTools package also contains `TagLibSharp.dll`. Each MD5 digest matched its existing catalog entry; SHA-256 values below were calculated from the downloaded bytes. Inspection of the plugin DLLs found the target framework `.NETCoreApp,Version=v10.0`.

| Plugin/version | SHA-256 of downloaded ZIP |
|---|---|
| [HDD Display 1.0.0.22](https://github.com/mleem97/mleem-jellyfin/releases/tag/HddDisplay-v1.0.0.22) | `2833be9e41c89266d6da0a0df434e593e26b2ca0fc8d50509154b26e6ffe077c` |
| [MusicSuite 1.0.0.0](https://github.com/mleem97/mleem-jellyfin/releases/tag/MusicSuite-v1.0.0.0) | `dfff526d845fbd281b192cacb779782d09512d04127160bcf8065a86f588d9d6` |
| [MusicHoarderzProvider 1.0.0.1](https://github.com/mleem97/mleem-jellyfin/releases/tag/MusicHoarderzProvider-v1.0.0.1) | `78b8685aeac7d30d817a48dd8600061b6a5706d2bb9f282f70c330a860ccf516` |
| [SpotifyProvider 1.0.0.1](https://github.com/mleem97/mleem-jellyfin/releases/tag/SpotifyProvider-v1.0.0.1) | `5a5378e2f245dc5f5d6e726540c607bf91a69ad631f13513a78c769ff7484db3` |
| [GracenoteProvider 1.0.0.1](https://github.com/mleem97/mleem-jellyfin/releases/tag/GracenoteProvider-v1.0.0.1) | `994d0f47d206a6c5b36c469b572568057a9e3ce573a8e51ab596595742d910ab` |
| [MediaTools 1.0.0.0](https://github.com/mleem97/mleem-jellyfin/releases/tag/MediaTools-v1.0.0.0) | `b3e345a6dce0f14f9a0132afce2752fdf913f8b4925c8a1fc560af9091e423d1` |
| [MetadataDashboard 1.0.0.0](https://github.com/mleem97/mleem-jellyfin/releases/tag/MetadataDashboard-v1.0.0.0) | `21a1b145ad2e524413de703a634ab9cc6dfe090fb517108387966f338fbc3083` |

The manifest retains each of these releases' declared `12.1.0.0` ABI. It removes the 21 aliases advertising the same .NET 10 binaries for Jellyfin 10.9, 10.10 and 10.11. No release asset, historical tag or checksum was replaced. Newly compiled development packages target 12.2 and must receive a new release version before being advertised as installable updates.

The historical [MusicSuite 0.1.0.0](https://github.com/mleem97/mleem-jellyfin/releases/tag/MusicSuite-v0.1.0.0) ZIP also exists and was downloaded. Its MD5 is `6FE7D214CAF4399A741089F02F4CD90A`; SHA-256 is `86af05d3e53796560a8f9203b0ac78a7754b333ac0bf8b922298cb7e30fbafad`. This proves the requested version in issue #41 was published, but does not prove its live UI acceptance criteria.

The historical [MusicToolkit 1.0.0.1](https://github.com/mleem97/mleem-jellyfin/releases/tag/MusicToolkit-v1.0.0.1) ZIP contains only the plugin DLL/PDB and omits `TagLibSharp.dll`. Its SHA-256 is `f7e9c66d5ff61dd601f42f269475f3a8554c91049b596f4cec5443f372fe8a39`. It remains outside the catalog until a corrected package is released under a newer version.

## Automated release gates

The shared tooling now verifies:

- Plugin GUID uniqueness, metadata/project versions, effective framework and declared server ABI against pinned Jellyfin API dependencies.
- Registration in the solution and metadata-driven discovery of every plugin.
- Exact required DLLs in ZIP-root layout, optional PDB, non-empty files, duplicate names, corrupt ZIP members and unsafe paths/symlinks.
- Catalog GUIDs, HTTPS asset URLs, MD5 format, unique version/ABI pairs, and the pending release's package checksum.
- Downloaded published ZIP bytes against local SHA-256, followed by package validation again.
- A newer version for each release and preservation of other plugins and separately built ABI variants.

`tests/release-tooling` contains regression tests for these failure paths. Dashboard and music-view regression suites are run by CI and the manual publish workflow. `build.yaml` is generated from plugin metadata; regenerate it with `python .github/scripts/validate_repository.py --sync-build` when adding a plugin or changing its build metadata.

## Remaining live qualification

| Issue | Evidence still needed |
|---|---|
| [#31: HDD release](https://github.com/mleem97/mleem-jellyfin/issues/31) | Complete the native Linux, Docker bind mount, LXC and NVIDIA/non-NVIDIA rows in the HDD test matrix. Capture a real admin-dashboard screenshot. Verify install, restart, upgrade and removal. |
| [#41: music display release](https://github.com/mleem97/mleem-jellyfin/issues/41) | Capture real desktop and mobile views. Confirm install/removal and complete restoration of the native music view. Exercise authenticated album browsing and user layout persistence on the target server. |
| [#51: provider release](https://github.com/mleem97/mleem-jellyfin/issues/51) | Confirm manual album image and metadata searches in Jellyfin's normal dialogs, selection/application behavior and an upgrade from preview 0.1.0.2 without lost encrypted settings. |

No real Jellyfin deployment, GPU passthrough host or browser session connected to the user's media server was available for this audit. These rows remain pending; generated images or mocked DOM snapshots are not substituted for real server screenshots. The original HDD 0.1.0.0 and provider 0.2.0.0 targets have been overtaken by published 1.x versions, so their acceptance should be exercised against the next monotonically increasing release.

## GitHub Actions limitation

The inspected `main` job from [run 35499929615](https://github.com/mleem97/mleem-jellyfin/actions/runs/35499929615) and the recent Dependabot job from [run 37142258204](https://github.com/mleem97/mleem-jellyfin/actions/runs/37142258204) failed with no executed steps and unavailable logs (`BlobNotFound`). Current action references `actions/setup-dotnet@v6` and `actions/setup-node@v7` both resolve to valid upstream tags. Runner/account availability needs to be checked if the same pre-step failure persists; no source-level cause can be inferred from an absent log.
