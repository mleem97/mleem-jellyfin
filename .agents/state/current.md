# Current work: Jellyfin plugin workspace overhaul

- Issue: #101
- Branch: `issue-101-multi-plugin-overhaul`
- Baseline: eight first-party plugin folders, published manifest with 18 entries, four configured source URLs.
- Desired architecture: first-party source in `plugins/`, optional reviewed upstream Git submodules in `external/plugins/`, centralized curated Jellyfin manifest for installation.
- Work prepared: stage-only validated manifest aggregator, tests, architecture notes.
- Remaining: validate current legacy manifest against the new strict parser; inspect actual upstream manifests before syncing; CI/coverage/security modernization; plugin release workflow and docs reconciliation; independent Codex review; GitHub review gate.
- Do not publish/mirror new binaries, merge, release or deploy.
