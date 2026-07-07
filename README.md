# mleem Jellyfin Plugins

> Personal Jellyfin plugin library for Marvin's Jellyfin projects.

[![License](https://img.shields.io/badge/License-GPLv3-green?style=for-the-badge)](./LICENSE)
[![Jellyfin](https://img.shields.io/badge/Jellyfin-10.10%2B-blue?style=for-the-badge)](https://jellyfin.org)
[![.NET](https://img.shields.io/badge/.NET-9.0-purple?style=for-the-badge&logo=dotnet)](https://dotnet.microsoft.com)

## Repository URL for Jellyfin

Use this manifest URL in Jellyfin:

```text
https://raw.githubusercontent.com/mleem97/mleem-jellyfin/main/manifest.json
```

## Purpose

This repository is the plugin library for my Jellyfin projects. It is not a template repository. Every plugin under `plugins/` is treated as a real plugin project with its own source code, metadata, README and release metadata.

## Plugins

| Plugin | Folder | Purpose |
|--------|--------|---------|
| HDD Display | [`plugins/HddDisplay`](./plugins/HddDisplay) | Admin Dashboard storage overview and NVIDIA transcoding telemetry |
| Music Dashboard | [`plugins/MusicDashboard`](./plugins/MusicDashboard) | Music library overview and metadata checks |

## Layout

```text
mleem-jellyfin/
├── plugins/
│   ├── HddDisplay/
│   │   ├── plugin.json
│   │   ├── HddDisplay.csproj
│   │   ├── Plugin.cs
│   │   ├── Configuration/
│   │   ├── Controllers/
│   │   └── Web/
│   └── MusicDashboard/
│       ├── plugin.json
│       ├── MusicDashboard.csproj
│       ├── Plugin.cs
│       ├── Configuration/
│       ├── Controllers/
│       └── Web/
├── docs/
├── .github/
│   ├── scripts/
│   └── workflows/
├── manifest.json
├── build.yaml
├── PLUGIN_REPOSITORY.md
├── CONTRIBUTING.md
└── README.md
```

## Build

Build the current plugins individually:

```bash
dotnet build plugins/HddDisplay/HddDisplay.csproj -c Release
dotnet build plugins/MusicDashboard/MusicDashboard.csproj -c Release
```

## Automatic Releases

Plugin releases are automated from `main`.

When a commit changes files under `plugins/<PluginSlug>/`, the `Auto Publish Plugins` workflow:

1. detects the changed plugin folder;
2. creates a changelog from commits since the last `<PluginSlug>-v*` tag;
3. bumps the version in `plugin.json` and the plugin `.csproj`;
4. builds the plugin;
5. creates a ZIP package;
6. updates `manifest.json`;
7. commits the release metadata back to `main`;
8. creates a GitHub release with the package and manifest.

Manual releases can also be started from GitHub Actions with a selected plugin and bump type.

## Versioning

The release script uses four-part Jellyfin-compatible versions:

```text
major.minor.patch.revision
```

Automatic bump rules:

| Commit style | Bump |
|--------------|------|
| `BREAKING CHANGE` or `type!:` | major |
| `feat:` | minor |
| `fix:`, `perf:`, `refactor:` | patch |
| everything else | revision |

Each plugin has its own tag namespace:

```text
HddDisplay-v1.0.0.0
MusicDashboard-v0.1.0.0
```

## Planning

- [`docs/ADMIN_DASHBOARD_STORAGE_GPU_PLAN.md`](./docs/ADMIN_DASHBOARD_STORAGE_GPU_PLAN.md)
- [`docs/PLUGIN_ROADMAP.md`](./docs/PLUGIN_ROADMAP.md)

## License

This project is licensed under the **GNU General Public License v3.0**. See [`LICENSE`](./LICENSE).
