# HDD Display

> Jellyfin Admin Dashboard plugin for mount-level disk usage and NVIDIA `jellyfin-ffmpeg` telemetry.

## Purpose

HDD Display is the storage and transcoding plugin in this Jellyfin plugin library. Its target surface is the Jellyfin Admin Dashboard, not the plugin settings area.

## Current Functionality

- Adds a Jellyfin plugin page named **HDD Display**.
- Exposes `GET /Plugins/HddDisplay/Storage`.
- Exposes `GET /Plugins/HddDisplay/Storage?refresh=true` to bypass the storage scan cache.
- Exposes `POST /Plugins/HddDisplay/Storage/Cache/Clear` to clear the in-memory storage scan cache.
- Exposes `GET /Plugins/HddDisplay/AdminDashboard/Overview` for the dashboard widget.
- Reads Jellyfin virtual folders.
- Resolves configured Jellyfin library paths through Linux mount information when available.
- Uses `DriveInfo` fallback for Windows paths or restricted environments.
- Groups configured Jellyfin library paths by detected drive or mount.
- Aggregates real file sizes by resolved mount and media type.
- Caches storage scans with a configurable cache lifetime.
- Reads NVIDIA GPU telemetry through `nvidia-smi` when available.
- Detects GPU processes that look like Jellyfin ffmpeg sessions.
- Returns per-path mount, scan and GPU diagnostics for troubleshooting.
- Displays total, used and free bytes for detected drives.

## Target Dashboard Functionality

- Replace or augment the Admin Dashboard `Pfade` panel.
- Show all Jellyfin media mounts in a compact card.
- Show colored usage segments for Filme, Serien, Musik and Video.
- Show live NVIDIA usage while `jellyfin-ffmpeg` is active.

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
