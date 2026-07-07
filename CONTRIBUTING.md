# Contributing

This repository hosts multiple Jellyfin plugins. Keep changes isolated to the plugin folder they belong to unless the change affects shared release tooling or documentation.

## Repository Rules

- One plugin per folder under `plugins/<PluginSlug>/`.
- Every plugin folder must contain a `plugin.json` metadata file.
- Every plugin project must be listed in `Jellyfin.Plugins.sln`.
- Shared release automation must update only the manifest entry matching the plugin GUID.
- Do not add plugin-specific assumptions to root-level tooling unless they are described in `plugin.json`.

## Adding a Plugin

1. Create `plugins/<PluginSlug>/`.
2. Add `<PluginSlug>.csproj`.
3. Add `plugin.json` with a unique GUID.
4. Add the project to `Jellyfin.Plugins.sln`.
5. Add or update the plugin entry in `manifest.json`.
6. Add a plugin README.
7. Build locally with:

```bash
dotnet build plugins/<PluginSlug>/<PluginSlug>.csproj -c Release
```

## Release Tags

Use plugin-scoped tags:

```bash
<PluginSlug>-v<version>
```

Example:

```bash
HddDisplay-v1.0.0.21
```

This avoids version collisions when the repository contains multiple plugins.

## Coding Style

- Keep plugin APIs explicit and stable.
- Prefer safe fallback behavior over hard failures in Jellyfin UI code.
- Avoid expensive filesystem scans on dashboard page load.
- Keep hardware telemetry optional and diagnostic when unavailable.

## Pull Requests

A PR should state:

- affected plugin(s);
- release impact;
- manual test steps;
- screenshots or dashboard notes for UI changes.
