# HDD Display

> Jellyfin Admin Dashboard plugin for mount-level disk usage and NVIDIA `jellyfin-ffmpeg` telemetry.

## Purpose

HDD Display is the storage and transcoding plugin in this Jellyfin plugin library. Its target surface is the Jellyfin Admin Dashboard, not the plugin settings area.

## Current Functionality

- Adds a Jellyfin plugin page named **HDD Display**.
- Exposes `GET /Plugins/HddDisplay/Storage`.
- Exposes `GET /Plugins/HddDisplay/Storage?refresh=true` to bypass the media scan cache.
- Exposes `POST /Plugins/HddDisplay/Storage/Cache/Clear` to clear the media scan cache.
- Exposes `GET /Plugins/HddDisplay/SystemUsage` for exclusive Jellyfin system-path usage.
- Exposes `GET /Plugins/HddDisplay/SystemUsage?refresh=true` to bypass the system-path cache.
- Exposes `POST /Plugins/HddDisplay/SystemUsage/Cache/Clear` to clear the system-path cache.
- Exposes `GET /Plugins/HddDisplay/AdminDashboard/Overview` for mount and GPU data.
- Exposes the embedded widget bundle at `GET /Plugins/HddDisplay/Assets/DashboardWidget.js`.
- Exposes immutable versioned assets at `GET /Plugins/HddDisplay/Assets/{assemblyVersion}/DashboardWidget.js`.
- Reads Jellyfin virtual folders and resolves them through Linux mount information when available.
- Uses `DriveInfo` fallback for Windows paths or restricted environments.
- Aggregates real media sizes by mount and media type.
- Aggregates cache, image cache, metadata, transcodes, logs, temp, plugins, configuration, program data and web resources.
- Excludes nested configured system paths from their parent category to prevent double counting.
- Uses independent configurable cache lifetimes for media and system scans.
- Reads NVIDIA GPU telemetry through `nvidia-smi` when available.
- Detects GPU processes that look like Jellyfin ffmpeg sessions.
- Returns mount, scan, system-path and GPU diagnostics for troubleshooting.

## Dashboard Loader

HDD Display does not modify Jellyfin Web files. Load the stable asset through the opt-in deployment mechanism described in [`../../docs/adr/0001-hdd-dashboard-injection.md`](../../docs/adr/0001-hdd-dashboard-injection.md). The versioned endpoint returns an immutable response and prevents stale script reuse after plugin upgrades.

## Dashboard Display

- Augments the Admin Dashboard `Pfade` / `Paths` area.
- Shows all Jellyfin media mounts in a compact card.
- Shows colored media segments for movies, series, music and video.
- Shows a separate exclusive system-path segment bar per mount.
- Shows live NVIDIA usage while `jellyfin-ffmpeg` is active.

## Target Hardware

| Component | Target |
|-----------|--------|
| CPU | Intel Xeon E3-1220 class CPU |
| GPU | NVIDIA GeForce GTX 1060 6 GB |
| Memory | DDR3 |
| Transcoder | `jellyfin-ffmpeg` |

## Build

```bash
dotnet build plugins/HddDisplay/HddDisplay.csproj -c Release
```

## Feature Plan

See [`../../docs/ADMIN_DASHBOARD_STORAGE_GPU_PLAN.md`](../../docs/ADMIN_DASHBOARD_STORAGE_GPU_PLAN.md) and [`../../docs/PLUGIN_BATCH_PLAN.md`](../../docs/PLUGIN_BATCH_PLAN.md).
