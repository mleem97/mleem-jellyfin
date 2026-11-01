# Jellyfin Plugin Library

This repository is Marvin's Jellyfin plugin library. It is organized for real plugin projects, not templates.

## Branch Policy

- `dev` is the integration branch for new work and dependency updates.
- `main` is the release branch and public catalog source; promotion uses a separate PR.
- Old branch names such as `master` are not referenced by repository documentation or workflows.

## Repository Contract

Every plugin lives in its own folder:

```text
plugins/
└── <PluginSlug>/
    ├── plugin.json
    ├── <PluginSlug>.csproj
    ├── Plugin.cs
    ├── Configuration/
    ├── Controllers/
    ├── Web/
    └── README.md
```

Required metadata lives in `plugin.json`. The root `manifest.json` contains the Jellyfin store entries.

## Current Plugins

| Order | Plugin | Folder | GUID |
|------:|--------|--------|------|
| 1 | HDD Display | `plugins/HddDisplay` | `eb5d7894-8eef-4b36-aa6f-5d124e828ce1` |
| 2 | MusicSuite | `plugins/MusicSuite` | `6b6c96ac-3f60-4ac3-8b8a-0d0cb8acb9dd` |
| 3 | MusicHoarderz Cover & Metadata Provider | `plugins/MusicHoarderzProvider` | `f8d74b1c-3c97-4481-a3b3-6eb622d6ad58` |
| 4 | Spotify Cover & Metadata Provider | `plugins/SpotifyProvider` | `67de1daa-c9fe-497a-9ca2-dc303abb717b` |
| 5 | Gracenote Metadata Provider | `plugins/GracenoteProvider` | `a05dae4a-2e92-4303-8f16-a7ade62624b4` |
| 6 | Music Toolkit & Spotify UX | `plugins/MusicToolkit` | `9f3e582a-281b-4b21-8c43-b295cbfa51de` |
| 7 | MediaTools | `plugins/MediaTools` | `8e4d2f17-5a33-4c91-b921-dfa724180c55` |
| 8 | MetadataDashboard | `plugins/MetadataDashboard` | `c70012b6-0fc0-479e-a814-b5a09fcdd799` |

The authoritative inventory is `plugins/*/plugin.json`; `build.yaml` is generated from
it. Run `python .github/scripts/validate_repository.py --matrix` to inspect all plugins.
Shared NuGet versions live in `Directory.Packages.props` and every project commits a
dependency lockfile. Upstream plugins are pinned submodules under `external/`, with
runtime relationships in `dependencies/plugins.json`.

See [Monorepo maintenance](docs/MONOREPO_MAINTENANCE.md) and
[upstream dependency management](external/README.md) for the maintained workflow.

## Jellyfin Manifest URL

```text
https://raw.githubusercontent.com/mleem97/mleem-jellyfin/main/manifest.json
```

## Automatic Releases

Releases are handled by `.github/workflows/auto-publish-plugins.yml` and `.github/scripts/publish_plugins.py`.

The workflow is manually dispatched on `main` with a plugin slug. A push alone does
not publish a release. The workflow validates metadata, runs tests, builds the selected
plugin, validates its declared ZIP contents, updates only its catalog entry and verifies
the downloaded release asset. Jellyfin uses MD5; additional integrity verification and
the published checksum sidecar use SHA-256.

Manual release runs can be started from GitHub Actions by choosing a plugin slug and bump type.

## Versioning

Each plugin has its own version and tag namespace:

```text
<PluginSlug>-v<version>
```

Examples:

```text
HddDisplay-v1.0.0.0
MusicSuite-v0.1.0.0
MusicHoarderzProvider-v0.1.0.0
SpotifyProvider-v1.0.0.0
GracenoteProvider-v1.0.0.0
MusicToolkit-v1.0.0.0
```

Automatic bump rules:

| Commit message | Bump |
|----------------|------|
| `BREAKING CHANGE` or `type!:` | major |
| `feat:` | minor |
| `fix:`, `perf:`, `refactor:` | patch |
| anything else | revision |

## Add Another Plugin

Use `python tools/new_plugin.py MyPlugin --name "My Plugin" --dry-run` to preview,
then repeat without `--dry-run`. The generator creates a unique identity, registers
the plugin and its separate test project in the solution and regenerates `build.yaml`.
Implement behavior/tests and commit generated lockfiles before publishing.

1. Create `plugins/<PluginSlug>/`.
2. Add the project file, plugin entry point, configuration, controllers and web resources.
3. Add `plugin.json`.
4. Add an entry to `manifest.json` only through a verified release with an existing asset.
5. Run the metadata validator; the build matrix discovers the new plugin automatically.
6. Add a plugin README.

Do not add placeholder template projects. A plugin folder should only exist when it contains actual plugin code.
