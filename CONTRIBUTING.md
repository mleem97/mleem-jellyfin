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
2. Create a dedicated branch from the current `main` branch.
3. Name the branch `issue-<number>-<short-description>`.
4. Reference the issue number in commit messages, for example `fix: validate package checksum (#22)`.
5. Open a pull request against `main`.
6. Include `Closes #<number>` in the pull-request body.
7. Merge the pull request only after the change has been reviewed and validated.
8. Do not close implementation issues manually; the merge must close them automatically.
9. Delete the issue branch after the merge.

One branch must not combine unrelated implementation issues. Release issues may depend on several previously merged implementation issues, but their release branch still closes only the release issue assigned to that branch.

## Adding a Plugin

1. Create `plugins/<PluginSlug>/`.
2. Add `<PluginSlug>.csproj`.
3. Add `plugin.json` with a unique GUID.
4. Add the project to `Jellyfin.Plugins.sln`.
5. Add or update the plugin entry in `manifest.json`.
6. Add a plugin README.
7. Add baseline tests under `tests/Jellyfin.Plugins.Tests/`.
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
BetterMusicDisplay-v0.1.0.0
MusicHoarderzProvider-v0.2.0.0
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
