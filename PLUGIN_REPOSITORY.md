# Jellyfin Plugin Library

This repository is Marvin's Jellyfin plugin library. It is organized for real plugin projects, not templates.

## Branch Policy

- `main` is the active branch.
- New work should target `main`.
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
| 2 | Better MusicDisplay | `plugins/BetterMusicDisplay` | `6b6c96ac-3f60-4ac3-8b8a-0d0cb8acb9dd` |
| 3 | MusicHoarderz Cover & Metadata Provider | `plugins/MusicHoarderzProvider` | `f8d74b1c-3c97-4481-a3b3-6eb622d6ad58` |

## Jellyfin Manifest URL

```text
https://raw.githubusercontent.com/mleem97/mleem-jellyfin/main/manifest.json
```

## Automatic Releases

Automatic releases are handled by `.github/workflows/auto-publish-plugins.yml` and `.github/scripts/auto_publish_plugins.py`.

On a push to `main`, changed plugin folders are detected. For each changed plugin the workflow reads `plugin.json`, creates a changelog from commits, bumps the version, builds a ZIP package, updates `manifest.json`, writes release metadata back to `main`, and creates a GitHub release.

Manual release runs can be started from GitHub Actions by choosing a plugin slug and bump type.

## Versioning

Each plugin has its own version and tag namespace:

```text
<PluginSlug>-v<version>
```

Examples:

```text
HddDisplay-v1.0.0.0
BetterMusicDisplay-v0.1.0.0
MusicHoarderzProvider-v0.1.0.0
```

Automatic bump rules:

| Commit message | Bump |
|----------------|------|
| `BREAKING CHANGE` or `type!:` | major |
| `feat:` | minor |
| `fix:`, `perf:`, `refactor:` | patch |
| anything else | revision |

## Add Another Plugin

1. Create `plugins/<PluginSlug>/`.
2. Add the project file, plugin entry point, configuration, controllers and web resources.
3. Add `plugin.json`.
4. Add an entry to `manifest.json`.
5. Add build coverage if the generic build workflow does not already cover it.
6. Add a plugin README.

Do not add placeholder template projects. A plugin folder should only exist when it contains actual plugin code.
