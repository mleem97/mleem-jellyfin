# Jellyfin Plugin Repository (Multi-Plugin)

This repository is configured as a Jellyfin plugin repository and can host multiple plugins in one `manifest.json`.

## How it works

- `manifest.json` is an array of plugin entries.
- Release workflow updates only the plugin matching `PLUGIN_GUID`.
- Other plugin entries are preserved.
- New releases are prepended to the plugin's `versions` list.

## Add another plugin

- Add another object to `manifest.json` with a unique `guid`.
- Create a dedicated release workflow (or matrix) for that plugin.
- Set these workflow env values per plugin:
  - `PLUGIN_GUID`
  - `PLUGIN_NAME`
  - `PLUGIN_PACKAGE_PREFIX`

## Repository URL in Jellyfin

Use one of these URLs:

- `https://raw.githubusercontent.com/mleem97/marvin-jelly-2/master/manifest.json`
- `https://github.com/mleem97/marvin-jelly-2/releases/latest/download/manifest.json`
