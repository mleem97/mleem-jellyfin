# ADR 0001 — Owned plugins vs external sources

Status: proposed in Issue #101

Decision: own plugin code stays under `plugins/`; optional external source checkouts become pinned Git submodules under `external/plugins/`; remote manifest feeds remain configured by URL and need no source checkout. Keep published `manifest.json` unchanged until reviewed promotion.

Risks: incompatible upstream manifest fields; GUID collisions; untrusted downloads; upstream outages. Mitigate with strict staging validation, explicit allowlist, provenance report and human release approval.
