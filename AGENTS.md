# AGENTS.md — Jellyfin plugin workspace

Read `README.md`, `docs/MULTI_PLUGIN_ARCHITECTURE.md`, `CONTRIBUTING.md` and `.agents/state/current.md` before modifying code.

- `plugins/` contains first-party .NET Jellyfin plugins. Do not convert to submodules or silently change GUIDs, assembly identities, saved settings or release tags.
- `external/plugins/` is reserved for approved upstream Git submodules. Never add an upstream repository without a verified URL, license and reviewed commit.
- The single Jellyfin manifest is `manifest.json`, not a source-control mirror of other plugins' code. Never overwrite it based on unverified remote data.
- For each task, keep a concise plan, ADR for material architectural choices, test evidence and current state under `.agents/`.
- Develop on an issue-bound feature branch. No direct pushes to `main`, no automated PR on ordinary dev pushes, no release/deploy without explicit authorization and mandatory checks.
- Prefer cached .NET restores, focused plugin validation and independently verified security scans. Measure coverage; do not claim 100% without evidence.
- A separate real Codex code-check agent must inspect exact SHA before claiming readiness. If unavailable, mark BLOCKED. Human approval remains mandatory.
- Update documentation only from observed code/configuration, not speculative behavior.
