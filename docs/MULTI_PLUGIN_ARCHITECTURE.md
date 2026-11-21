# Multi-plugin architecture and curated Jellyfin repository

## Ownership boundary

- `plugins/<Slug>/` contains **first-party code** for all eight current plugins. These are normal Git-tracked files, **never submodules**.
- `external/plugins/<Name>/` is reserved for reviewed third-party **Git submodules** only when their source code must be checked out. No upstream repository has been authorized yet; therefore there is no `.gitmodules` entry.
- `configs/repositories.json` is an allowlist of Jellyfin **manifest feeds**. Fetching a manifest does *not* require cloning a source repository.
- `manifest.json` is the existing published single Jellyfin repository URL and MUST NOT be overwritten by the aggregator before approval.

## Stage-only aggregator

`scripts/aggregate_jellyfin_manifests.py` validates the owned baseline and configured HTTPS upstream feeds and writes `build/aggregated-manifest.json` with a separate provenance/conflict report. No CI publication, tags, push, release or deployment are triggered.

```bash
python3 -m unittest discover -s tests -p 'test_manifest_aggregator.py'
python3 scripts/aggregate_jellyfin_manifests.py --offline
# Actual remote fetch only with network access and reviewed allowlist:
python3 scripts/aggregate_jellyfin_manifests.py
```

Own baseline takes priority in GUID conflicts. Existing metadata is not silently rewritten. URLs are currently restricted to explicitly configured publication hosts. Network/validation errors fail without rewriting `manifest.json`; use the previous approved published manifest.

## Before activating production sync

1. Verify each upstream URL, license, source identity, plugin ZIP, checksums and version compatibility.
2. Review legacy entries in the current published manifest: some may not meet the new strict validation policy.
3. Compare the staged proposed manifest with the published 18-entry baseline and inspect collisions.
4. Ensure Jellyfin supports every intended ABI. Do not assume an ABI from the root .NET version.
5. Validate the existing release scripts update only owned plugin entries.
6. Add a reviewed, explicit release workflow; do not allow unreviewed pushes to `main`.
7. Enable submodule checkout only for specifically approved source repositories.
8. Enforce independent code review and human GitHub PR approval before merging.

## Existing architecture findings

The repository contains eight plugin folders: HddDisplay, MusicSuite, MusicHoarderzProvider, SpotifyProvider, GracenoteProvider, MusicToolkit, MediaTools and MetadataDashboard. Older README/status documentation mentioned fewer. Multiple workflows overlap in build/testing; the current manual release workflow lists only five plugins. Those are separate migration tasks under Issue #101.

This repository is **not** a generic template repository. Own plugin GUIDs, binary identity, per-plugin version streams, configuration and published URLs must remain stable.
