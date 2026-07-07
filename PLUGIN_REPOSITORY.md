# Jellyfin Plugin Library

This repository is Marvin's Jellyfin plugin library. It is organized for real plugin projects, not templates.

## Branch Policy

- `main` is the active branch.
- New work should target `main`.
- Old branch names such as `master` are not referenced by the repository documentation or workflows.

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

| Plugin | Folder | GUID |
|--------|--------|------|
| HDD Display | `plugins/HddDisplay` | `eb5d7894-8eef-4b36-aa6f-5d124e828ce1` |
| Music Dashboard | `plugins/MusicDashboard` | `4a88e030-4b75-4f2b-a43f-5b4c1b797d2b` |

## Jellyfin Manifest URL

```text
https://raw.githubusercontent.com/mleem97/mleem-jellyfin/main/manifest.json
```

## Release Tags

Each plugin has its own tag namespace:

```text
<PluginSlug>-v<version>
```

Examples:

```text
HddDisplay-v1.0.0.0
MusicDashboard-v0.1.0.0
```

## Add Another Plugin

1. Create `plugins/<PluginSlug>/`.
2. Add the project file, plugin entry point, configuration, controllers and web resources.
3. Add `plugin.json`.
4. Add an entry to `manifest.json`.
5. Add the project to CI/build metadata.
6. Add a plugin README.

Do not add placeholder template projects. A plugin folder should only exist when it contains actual plugin code.
