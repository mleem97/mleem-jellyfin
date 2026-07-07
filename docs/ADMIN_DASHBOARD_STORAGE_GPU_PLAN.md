# Admin Dashboard Storage + GPU Feature Plan

## Goal

Replace the current `Pfade` / `Paths` style information on the Jellyfin Admin Dashboard with a compact live overview that answers two questions:

1. How full are the Jellyfin media disks?
2. Is `jellyfin-ffmpeg` using the NVIDIA GPU right now?

This is explicitly not meant to be a plugin-settings-page feature. The settings page is only a fallback for diagnostics.

## Target Environment

Initial implementation target provided by the server owner:

- CPU: Intel Xeon E3-1220 class CPU
- GPU: NVIDIA GeForce GTX 1060 6 GB
- RAM: DDR3
- Transcoding process: `jellyfin-ffmpeg`
- Jellyfin host: Linux-style paths expected, with Docker/LXC/native support to be handled by path discovery

## Dashboard Placement

Target area: the Admin Dashboard card currently showing `Pfade` / `Paths`.

Implementation approach:

1. Detect the Admin Dashboard page.
2. Locate the existing paths panel by heading text or stable DOM structure.
3. Replace the panel body with HDD Display's card.
4. If replacement fails, insert the card directly above the detected dashboard content as fallback.

## Storage Model

### Required Output

Per mount/disk:

- mount path
- display label
- filesystem/device if available
- total bytes
- used bytes
- free bytes
- Jellyfin library paths on that mount
- media type segments by bytes

Supported segment types for the first pass:

| Type | Jellyfin mapping | Label | Color |
|------|------------------|-------|-------|
| `movies` | `movies` | Filme | blue |
| `tvshows` | `tvshows` | Serien | teal |
| `music` | `music` | Musik | orange |
| `video` | `homevideos`, generic video folders | Video | green |
| `other` | unknown / mixed | Sonstiges | grey |
| `free` | remaining disk space | Frei | dark |

### Mount Resolution

The current prototype uses a simple `/mnt/<name>` or `/media/<name>` path split. That is not sufficient.

New backend service:

```text
MountResolver
├── read /proc/self/mountinfo on Linux
├── normalize Jellyfin library paths
├── choose the deepest matching mount point
├── fallback to DriveInfo on Windows or restricted containers
└── expose diagnostics for unmatched paths
```

This is important for Docker bind mounts, LXC mount passthroughs, and non-standard media paths.

### Media Usage Calculation

The dashboard bar must not divide disk usage equally between libraries. It must use actual byte counts.

Preferred sequence:

1. Use Jellyfin indexed items and media sources where file path and size are available.
2. Aggregate item sizes by resolved mount and normalized media type.
3. Cache the result to avoid scanning on every dashboard load.
4. Refresh after library scan events or on a low-frequency timer.
5. Optional fallback: controlled filesystem scan for configured library paths.

## NVIDIA GPU Model

Because the server has a GTX 1060 6 GB, first implementation should target NVIDIA.

### Data Sources

Preferred provider order:

1. `nvidia-smi --query-gpu=utilization.gpu,utilization.encoder,utilization.decoder,memory.used,memory.total,name --format=csv,noheader,nounits`
2. `nvidia-smi pmon` or process query for `jellyfin-ffmpeg` where available
3. process table fallback: count active `jellyfin-ffmpeg` processes only

### Required Output

```json
{
  "available": true,
  "provider": "nvidia-smi",
  "gpuName": "NVIDIA GeForce GTX 1060 6GB",
  "ffmpegProcessCount": 1,
  "gpuUtilizationPercent": 37,
  "encoderUtilizationPercent": 61,
  "decoderUtilizationPercent": 18,
  "vramUsedBytes": 812000000,
  "vramTotalBytes": 6000000000
}
```

### Runtime Considerations

- The Jellyfin process/container must be able to execute `nvidia-smi` or read equivalent GPU telemetry.
- Docker setups need NVIDIA runtime/device passthrough.
- If telemetry is unavailable, the UI must show `GPU telemetry unavailable` instead of failing the whole widget.
- Polling interval should default to 2-5 seconds for GPU values, while storage usage can refresh more slowly.

## API Design

Planned endpoint:

```http
GET /Plugins/HddDisplay/AdminDashboard/Overview
```

Response shape:

```json
{
  "generatedAtUtc": "2026-04-01T12:00:00Z",
  "storage": {
    "mounts": [
      {
        "mountPath": "/mnt/media-a",
        "label": "media-a",
        "totalBytes": 12000000000000,
        "usedBytes": 7400000000000,
        "freeBytes": 4600000000000,
        "segments": [
          { "type": "movies", "label": "Filme", "bytes": 4100000000000, "color": "#5591c7" },
          { "type": "tvshows", "label": "Serien", "bytes": 2500000000000, "color": "#4f98a3" },
          { "type": "music", "label": "Musik", "bytes": 300000000000, "color": "#fdab43" },
          { "type": "video", "label": "Video", "bytes": 500000000000, "color": "#6daa45" }
        ],
        "libraryPaths": [
          "/mnt/media-a/Filme",
          "/mnt/media-a/Serien"
        ]
      }
    ]
  },
  "gpu": {
    "available": true,
    "provider": "nvidia-smi",
    "gpuName": "NVIDIA GeForce GTX 1060 6GB",
    "ffmpegProcessCount": 1,
    "gpuUtilizationPercent": 37,
    "encoderUtilizationPercent": 61,
    "decoderUtilizationPercent": 18,
    "vramUsedBytes": 812000000,
    "vramTotalBytes": 6000000000
  }
}
```

## Implementation Phases

### Phase 1: Repository and release base

- Multi-plugin repository layout.
- One folder per plugin.
- `plugin.json` metadata per plugin.
- Shared manifest release workflow.
- Root README and plugin README.

### Phase 2: Storage backend

- Add `MountResolver`.
- Add media usage aggregation by actual bytes.
- Add cache layer.
- Add diagnostics for unmatched paths and inaccessible mounts.

### Phase 3: NVIDIA telemetry backend

- Add `GpuUsageProvider` abstraction.
- Add `NvidiaSmiGpuUsageProvider`.
- Add `jellyfin-ffmpeg` process detection.
- Add safe fallback response.

### Phase 4: Admin Dashboard UI

- Replace/augment the `Pfade` panel.
- Render three-disk layout cleanly.
- Render stacked media-type bars.
- Render live GPU card.
- Add compact error states.

### Phase 5: Packaging and release

- Tag as `HddDisplay-v<version>`.
- Verify ZIP contains only plugin DLL/PDB and required artifacts.
- Verify `manifest.json` updates only the `HDD Display` entry.

## Non-goals for the first implementation

- Full historical GPU charts.
- Cross-vendor GPU support beyond clean fallbacks.
- Per-user quota accounting.
- Manual library path configuration unless Jellyfin metadata is insufficient.
