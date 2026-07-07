# Plugin Roadmap

## Repository Direction

This repository is the plugin library for Marvin's Jellyfin projects. Plugin folders must contain working plugin code.

## HDD Display

Purpose: replace the Admin Dashboard `Pfade` area with a compact storage panel.

Implementation order:

1. Resolve Jellyfin library paths to mounts.
2. Aggregate real byte usage by media type.
3. Cache storage calculations.
4. Add NVIDIA usage data.
5. Render the Admin Dashboard widget.

## Music Dashboard

Purpose: provide a better overview of the Jellyfin music library.

Implementation order:

1. Show configured music libraries and paths.
2. Add missing metadata counters.
3. Add missing cover counters.
4. Add duplicate album and duplicate track detection.
5. Add codec and bitrate overview.
6. Add recently added music cards.

## Packaging

Each plugin releases independently with plugin-scoped tags.
