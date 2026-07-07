# Jellyfin Plugin Repository

This repository is configured as a multi-plugin Jellyfin plugin repository. The shared `manifest.json` can contain any number of plugins, while each plugin keeps its own source code, project file, metadata, and documentation inside `plugins/<PluginSlug>/`.

## Repository Contract

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

## How Releases Work

- `manifest.json` is a JSON array of plugin entries.
- Every plugin has a stable `guid`.
- The release workflow reads `plugins/<PluginSlug>/plugin.json`.
- The workflow updates only the manifest entry matching that plugin's GUID.
- Other plugin entries and historical versions are preserved.
- New releases are prepended to the matching plugin's `versions` list.

## Release Tags

Use plugin-scoped tags:

```bash
<PluginSlug>-v<version>
```

Example:

```bash
HddDisplay-v1.0.0.21
```

Plugin-scoped tags avoid collisions when several plugins live in the same repository.

## Add Another Plugin

1. Create `plugins/<PluginSlug>/`.
2. Add the plugin project file.
3. Add `plugin.json` with at least:
   - `slug`
   - `name`
   - `guid`
   - `version`
   - `targetAbi`
   - `framework`
   - `project`
   - `packagePrefix`
   - `assembly`
   - `owner`
   - `category`
   - `overview`
   - `description`
4. Add the project to `Jellyfin.Plugins.sln`.
5. Add an entry to `manifest.json`.
6. Push a plugin-scoped release tag.

## Jellyfin Repository URL

Use one of these URLs in Jellyfin's plugin repository settings:

- `https://raw.githubusercontent.com/mleem97/mleem-jellyfin/master/manifest.json`
- `https://github.com/mleem97/mleem-jellyfin/releases/latest/download/manifest.json`

## Current Plugins

| Plugin | Folder | GUID |
|--------|--------|------|
| HDD Display | `plugins/HddDisplay` | `eb5d7894-8eef-4b36-aa6f-5d124e828ce1` |
