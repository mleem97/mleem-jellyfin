# Contributing

This repository hosts multiple Jellyfin plugins. Keep changes isolated to the plugin folder they belong to unless the change affects shared release tooling, tests or documentation.

## Repository Rules

- One plugin per folder under `plugins/<PluginSlug>/`.
- Every plugin folder must contain a `plugin.json` metadata file.
- Every plugin project must be listed in `Jellyfin.Plugins.sln`.
- Shared release automation must update only the manifest entry matching the plugin GUID.
- Do not add plugin-specific assumptions to root-level tooling unless they are described in `plugin.json`.

## Issue, Branch and Pull Request Policy

Every implementation change must be linked to exactly one GitHub issue.

1. Create or select the implementation issue before changing code.
2. Create a dedicated branch from the current `dev` branch.
3. Name the branch `issue-<number>-<short-description>`.
4. Reference the issue number in commit messages, for example `fix: validate package checksum (#22)`.
5. Open a pull request against `dev` for integration. Promote reviewed releases through a separate `dev` to `main` pull request.
6. Include `Closes #<number>` in the pull-request body.
7. Merge the pull request only after the change has been reviewed and validated.
8. Do not close implementation issues merely to clean up the issue list. GitHub closing keywords take effect when fixes reach the default `main` branch; while a fix is only in `dev`, keep its issue linked to the tested integration PR. Release issues remain open until their actual acceptance checks pass.
9. Delete the issue branch after the merge.

One branch must not combine unrelated implementation issues. Release issues may depend on several previously merged implementation issues, but their release branch still closes only the release issue assigned to that branch.

Maintainer-requested consolidation may use an integration PR containing the focused
issue commits and existing branch histories. Record the original branch heads,
validation evidence and any remaining release qualification instead of treating an
aggregate merge as proof that every release criterion passed.

## Adding a Plugin

Prefer the maintained generator and shared metadata validator:

```bash
python tools/new_plugin.py MyPlugin --name "My Plugin" --dry-run
python tools/new_plugin.py MyPlugin --name "My Plugin"
dotnet restore Jellyfin.Plugins.sln
python .github/scripts/validate_repository.py
```

Commit the new projects' dependency lockfiles. See
[`docs/MONOREPO_MAINTENANCE.md`](docs/MONOREPO_MAINTENANCE.md) and
[`external/README.md`](external/README.md) for package updates and optional upstream
plugin submodules. New package versions belong in `Directory.Packages.props`.

1. Create `plugins/<PluginSlug>/`.
2. Add `<PluginSlug>.csproj`.
3. Add `plugin.json` with a unique GUID.
4. Add the project to `Jellyfin.Plugins.sln`.
5. Add the plugin entry to `manifest.json` only through a verified release with an existing package asset.
6. Add a plugin README.
7. Add behavior and authorization tests under `tests/<PluginSlug>.Tests/`.
8. Build and test locally with:

```bash
dotnet restore Jellyfin.Plugins.sln
dotnet build Jellyfin.Plugins.sln -c Release --no-restore
dotnet test Jellyfin.Plugins.sln -c Release --no-restore --no-build
```

## Release Tags

Use plugin-scoped tags:

```bash
<PluginSlug>-v<version>
```

Examples:

```bash
HddDisplay-v0.1.0.0
MusicSuite-v0.1.0.0
MusicHoarderzProvider-v0.2.0.0
SpotifyProvider-v1.0.0.0
GracenoteProvider-v1.0.0.0
MusicToolkit-v1.0.0.0
```

This avoids version collisions when the repository contains multiple plugins.

## Coding Style

- Keep plugin APIs explicit and stable.
- Prefer safe fallback behavior over hard failures in Jellyfin UI code.
- Avoid expensive filesystem scans on dashboard page load.
- Keep hardware telemetry optional and diagnostic when unavailable.
- Add or update tests for every behavior change.

## Pull Requests

A pull request must state:

- the linked issue and include `Closes #<number>`;
- affected plugin or shared component;
- release impact;
- validation or test steps;
- screenshots or dashboard notes for UI changes.
