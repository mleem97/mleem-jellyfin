# mleem Jellyfin Plugins

A monorepo for Marvin's Jellyfin plugins. Every folder in `plugins/` is an independent plugin with its own GUID, version, assembly, metadata and release package.

## Install published plugins

Add this repository URL under **Jellyfin Dashboard → Plugins → Repositories**:

```text
https://raw.githubusercontent.com/mleem97/mleem-jellyfin/main/manifest.json
```

The catalog describes published release assets. Development changes on `dev` do not update those binaries. The existing local plugin releases target Jellyfin 12.1 and .NET 10; the development source targets Jellyfin 12.2 and .NET 10. A .NET 10 plugin package cannot be made compatible with older Jellyfin runtimes by duplicating its catalog entry with another `targetAbi`.

Installation, optional companion plugins and update/removal steps are in [the installation guide](docs/INSTALLATION.md). Release evidence and outstanding live-server qualification are tracked in [release readiness](docs/RELEASE_READINESS.md).

## Plugins

| Plugin | Source | Purpose |
|---|---|---|
| HDD Display | [HddDisplay](plugins/HddDisplay) | Administrator dashboard storage, mount usage and optional NVIDIA telemetry |
| MusicSuite | [MusicSuite](plugins/MusicSuite) | Music dashboard, album browsing, live search and per-user layouts |
| MusicHoarderz Provider | [MusicHoarderzProvider](plugins/MusicHoarderzProvider) | Manual cover and album metadata lookup through MusicHoarderz/COV |
| Spotify Provider | [SpotifyProvider](plugins/SpotifyProvider) | Spotify music metadata and artwork |
| Gracenote Provider | [GracenoteProvider](plugins/GracenoteProvider) | Gracenote album/artist metadata and artwork |
| MediaTools | [MediaTools](plugins/MediaTools) | Media renaming, remuxing, merging and duplicate analysis |
| MetadataDashboard | [MetadataDashboard](plugins/MetadataDashboard) | Music metadata audit and lookup dashboard |
| MusicToolkit | [MusicToolkit](plugins/MusicToolkit) | Legacy music maintenance tools and Spotify-style UI; retained as an independent project |

The published MusicToolkit 1.0.0.1 archive lacks its TagLibSharp dependency and remains outside the installation catalog. Development packages include only the runtime dependencies explicitly declared in each `plugin.json`.

## Repository layout

- `plugins/<PluginSlug>/`: plugin source, `plugin.json`, embedded web assets and project file.
- `tests/Jellyfin.Plugins.Tests/`: shared plugin behavior and authorization tests.
- `tests/MusicToolkit.Tests/`: MusicToolkit-specific tests.
- `tests/dashboard-widget/`, `tests/music-suite/`: JavaScript UI regression suites.
- `tests/release-tooling/`: package, checksum and metadata validation tests.
- `.github/scripts/`: common plugin discovery, validation, packaging and publishing.
- `external/`: separately versioned upstream companion-plugin sources; see [monorepo maintenance](docs/MONOREPO_MAINTENANCE.md).
- `configs/`, `docker/`, `docs/`: optional integration configuration, deployment examples and documentation.

`Jellyfin.Plugins.sln` is the entry point for all eight plugins and both .NET test projects. `plugin.json` drives CI discovery and packaging, so a newly registered plugin is built without adding another build job. `build.yaml` is a generated compatibility index; run `python .github/scripts/validate_repository.py --sync-build` after adding or changing plugin build metadata.

## Build and verify

Use the .NET SDK selected by `global.json`, Node.js 22 and Python 3. Package versions are centralized in `Directory.Packages.props`; committed lockfiles make restores reproducible.

```bash
dotnet restore Jellyfin.Plugins.sln --locked-mode
dotnet build Jellyfin.Plugins.sln -c Release --no-restore
dotnet test Jellyfin.Plugins.sln -c Release --no-restore --no-build
python -m unittest discover -s tests/release-tooling -v
python .github/scripts/validate_repository.py
npm ci --prefix tests/dashboard-widget
npm test --prefix tests/dashboard-widget
npm ci --prefix tests/music-suite
npm test --prefix tests/music-suite
python .github/scripts/package_smoke_test.py
```

Build or package one plugin independently:

```bash
dotnet build plugins/HddDisplay/HddDisplay.csproj -c Release
python .github/scripts/package_smoke_test.py --plugin HddDisplay --output artifacts
```

The generated ZIP has the plugin DLL and declared dependencies at its root. CI runs on pull requests and pushes to `dev`/`main`, checks all plugin metadata against the projects, builds every plugin independently and uploads validated development packages.

## Integration and releases

`dev` collects implementation work. Production releases use the manual **Auto Publish Plugins** workflow on `main`. Select one plugin and either a version bump or a newer exact four-part version (`major.minor.patch.revision`).

The release job runs tests, builds the selected plugin, validates the ZIP and catalog, publishes the release and downloads its asset to verify SHA-256. The catalog's `checksum` remains MD5 because Jellyfin requires that format; a separate `.zip.sha256` file records SHA-256 for the downloadable package. A source merge or a passing package smoke test does not by itself prove installation, upgrade or UI behavior on a real server.

Each plugin has its own tag namespace, for example `HddDisplay-v1.0.0.23` or `MusicSuite-v1.0.1.0`. Existing tags are immutable. Historical MVP version requests do not justify downgrading already released 1.x plugins.

## Further documentation

- [Implementation status](docs/IMPLEMENTATION_STATUS.md)
- [Monorepo maintenance and upstream dependencies](docs/MONOREPO_MAINTENANCE.md)
- [Release readiness and verified artifacts](docs/RELEASE_READINESS.md)
- [HDD Display qualification matrix](docs/HDD_DISPLAY_TEST_MATRIX.md)
- [MusicHoarderz integration policy](docs/COV_INTEGRATION_POLICY.md)
- [Architecture](docs/ARCHITECTURE.md)
- [Contributing](CONTRIBUTING.md)

## License

This project's source is licensed under [Apache License 2.0](LICENSE). Upstream companion projects retain their own licenses.
