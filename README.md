# mleem Jellyfin Plugins

> Multi-plugin Jellyfin repository for self-hosted admin dashboard extensions, media server utilities, and hardware telemetry plugins.

[![License](https://img.shields.io/badge/License-GPLv3-green?style=for-the-badge)](./LICENSE)
[![Jellyfin](https://img.shields.io/badge/Jellyfin-10.10%2B-blue?style=for-the-badge)](https://jellyfin.org)
[![.NET](https://img.shields.io/badge/.NET-9.0-purple?style=for-the-badge&logo=dotnet)](https://dotnet.microsoft.com)
[![Plugins](https://img.shields.io/badge/Plugins-Multi--Plugin-orange?style=for-the-badge)](./plugins)

## Links

- **Repository:** https://github.com/mleem97/mleem-jellyfin
- **Jellyfin repository manifest:** `https://raw.githubusercontent.com/mleem97/mleem-jellyfin/master/manifest.json`
- **Latest release manifest:** `https://github.com/mleem97/mleem-jellyfin/releases/latest/download/manifest.json`

## Overview

This repository is structured as a Jellyfin plugin catalog. Each plugin lives in its own folder under [`plugins/`](./plugins), has its own metadata file, can be built independently, and is published into the shared Jellyfin `manifest.json` so it can be installed through Jellyfin's plugin repository UI.

The first plugin in this repository is **HDD Display**. Its current code has been moved into `plugins/HddDisplay`; future work will replace the previous settings-page focused approach with an Admin Dashboard widget for disk usage, media-type allocation, and NVIDIA transcoding telemetry.

## Plugins

| Plugin | Folder | Status | Purpose |
|--------|--------|--------|---------|
| HDD Display | [`plugins/HddDisplay`](./plugins/HddDisplay) | Planned dashboard rewrite | Admin Dashboard storage overview and NVIDIA `jellyfin-ffmpeg` telemetry |

## Repository Layout

```text
mleem-jellyfin/
├── plugins/
│   └── HddDisplay/
│       ├── plugin.json              # plugin metadata used by CI/release
│       ├── HddDisplay.csproj         # plugin project
│       ├── Plugin.cs                 # Jellyfin plugin entry point
│       ├── Configuration/            # plugin configuration model
│       ├── Controllers/              # plugin API endpoints
│       └── Web/                      # embedded Jellyfin web resources
├── docs/
│   └── ADMIN_DASHBOARD_STORAGE_GPU_PLAN.md
├── .github/
│   ├── workflows/release-plugin.yml  # build/release pipeline
│   └── FUNDING.yml
├── manifest.json                     # Jellyfin plugin repository manifest
├── Jellyfin.Plugins.sln              # solution including all plugin projects
├── PLUGIN_REPOSITORY.md              # multi-plugin repository guide
├── LICENSE
└── README.md
```

## Add another plugin

1. Create a new folder under `plugins/<PluginSlug>/`.
2. Add a `<PluginSlug>.csproj` in that folder.
3. Add a `plugin.json` file next to the project.
4. Add the project to `Jellyfin.Plugins.sln`.
5. Add a new object to `manifest.json` with a unique `guid` and an empty or initial `versions` array.
6. Release it with a tag in this format:

```bash
git tag <PluginSlug>-v1.0.0
git push origin <PluginSlug>-v1.0.0
```

Example:

```bash
git tag HddDisplay-v1.0.0.21
git push origin HddDisplay-v1.0.0.21
```

The release workflow reads `plugins/<PluginSlug>/plugin.json`, builds only that plugin, creates `<PackagePrefix>_<version>.zip`, uploads the ZIP plus `manifest.json`, and updates the matching plugin entry in the repository manifest.

## Build from Source

Requirements:

- .NET 9 SDK
- Jellyfin package dependencies restored from NuGet

Build all plugins:

```bash
git clone https://github.com/mleem97/mleem-jellyfin.git
cd mleem-jellyfin
dotnet restore Jellyfin.Plugins.sln
dotnet build Jellyfin.Plugins.sln -c Release
```

Build one plugin:

```bash
dotnet build plugins/HddDisplay/HddDisplay.csproj -c Release
```

## Feature Planning

The HDD Display plugin is being redesigned for the Admin Dashboard, not for the plugin settings page. The planning document is here:

- [`docs/ADMIN_DASHBOARD_STORAGE_GPU_PLAN.md`](./docs/ADMIN_DASHBOARD_STORAGE_GPU_PLAN.md)

Target hardware for the first implementation pass:

- Intel Xeon E3-1220 class CPU
- NVIDIA GeForce GTX 1060 6 GB
- DDR3 system memory
- Jellyfin using `jellyfin-ffmpeg` for transcoding

## Contributing

See [`CONTRIBUTING.md`](./CONTRIBUTING.md).

## License

This project is licensed under the **GNU General Public License v3.0**. See [`LICENSE`](./LICENSE).

---

**mleem Jellyfin Plugins — small, focused extensions for self-hosted Jellyfin servers.**
