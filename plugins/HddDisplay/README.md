# HDD Display

> Jellyfin Admin Dashboard plugin for mount-level disk usage and NVIDIA `jellyfin-ffmpeg` telemetry.

[![Jellyfin](https://img.shields.io/badge/Jellyfin-10.10%2B-blue?style=for-the-badge)](https://jellyfin.org)
[![.NET](https://img.shields.io/badge/.NET-9.0-purple?style=for-the-badge&logo=dotnet)](https://dotnet.microsoft.com)
[![GPU](https://img.shields.io/badge/NVIDIA-GTX%201060%206GB-green?style=for-the-badge&logo=nvidia)]()

## Overview

HDD Display is intended to show the information that matters directly on the Jellyfin Admin Dashboard:

- all Jellyfin library paths grouped by physical mount/disk;
- total, used, and free capacity per mount;
- stacked usage bars by supported media type: Video, Filme, Serien, Musik;
- live NVIDIA telemetry for active `jellyfin-ffmpeg` transcoding.

The existing settings-page experiment has been retained only as a diagnostics fallback. The actual feature target is the Admin Dashboard.

## Target Hardware

Initial implementation target:

| Component | Target |
|-----------|--------|
| CPU | Intel Xeon E3-1220 class CPU |
| GPU | NVIDIA GeForce GTX 1060 6 GB |
| Memory | DDR3 |
| Transcoder | `jellyfin-ffmpeg` |

## Planned Dashboard Layout

The Jellyfin Admin Dashboard currently has a right-side `Pfade` / `Paths` panel. This plugin should replace or augment that area with a compact hardware/storage panel:

```text
Storage & Transcoding
├── GPU: GTX 1060 6GB
│   ├── ffmpeg processes: 1
│   ├── GPU: 37%
│   ├── Encoder: 61%
│   └── VRAM: 0.8 / 6.0 GB
├── /mnt/media-a
│   ├── 7.4 / 10.9 TB used
│   └── [ Filme | Serien | Musik | Video | Free ]
├── /mnt/media-b
└── /mnt/media-c
```

## Release Metadata

This plugin is described by [`plugin.json`](./plugin.json). The repository release workflow reads that file to build the project and update the shared Jellyfin `manifest.json`.

## Build

```bash
dotnet build plugins/HddDisplay/HddDisplay.csproj -c Release
```

## Feature Plan

See [`../../docs/ADMIN_DASHBOARD_STORAGE_GPU_PLAN.md`](../../docs/ADMIN_DASHBOARD_STORAGE_GPU_PLAN.md).
