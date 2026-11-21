# Upstream plugin sources

This directory contains separately maintained Git repositories. The parent repository
records an exact commit for each submodule. The original upstream sources, history
and license files remain in the submodule; do not copy their classes into our plugins.

| Submodule | Pinned release | Used by |
| --- | --- | --- |
| `iamparadox/file-transformation` | `3.0.3.0` | HDD Display, MusicSuite, MusicToolkit |
| `iamparadox/plugin-pages` | `3.0.3.0` | HDD Display, MusicSuite, MusicToolkit, MediaTools, MetadataDashboard |

The exact commits, source repositories, runtime assets and upstream relationships
are recorded in [`dependencies/plugins.json`](../dependencies/plugins.json).
The `runtimeDependencies` lists in each `plugin.json` refer to those IDs. This is
repository metadata, not an automatic Jellyfin dependency installer.

## Clone and initialize

```bash
git clone --recurse-submodules https://github.com/mleem97/mleem-jellyfin.git
cd mleem-jellyfin
git switch dev
git submodule update --init --recursive
```

Existing clones only need the last command after pulling a change to a gitlink.
Ordinary builds of our solution do not require submodule initialization: no upstream
project is added to our solution or referenced by our plugin project files.

## Runtime installation is separate

Adding source code here does not install either plugin on a Jellyfin server. Add the
upstream catalog `https://www.iamparadox.dev/jellyfin/plugins/manifest.json`, install
**File Transformation first**, then **Plugin Pages**, using assets for the server's
exact Jellyfin version, and restart Jellyfin. The selected upstream release includes
assets for Jellyfin `12.2.0`; this repository's current development code targets
Jellyfin `12.2.0.0` and .NET 10.

The upstream File Transformation README explicitly requires reflection registration
because Jellyfin loads plugins in separate assembly contexts. Keep these optional
runtime bridges; do not introduce a `ProjectReference` to a submodule or bundle its
DLL into an unrelated plugin package. Absence must preserve native Jellyfin behavior.

## Review an update

1. Read the upstream release notes and select an exact tag compatible with the server.
2. Create a normal issue branch from `dev`.
3. Fetch tags in the relevant submodule and check out the reviewed tag detached.
4. Review the source diff, public registration contract and upstream license changes.
5. Update the parent gitlink and matching release/commit/asset metadata in
   `dependencies/plugins.json`. Do not modify upstream files in the parent commit.
6. Run `python tools/validate_dependencies.py --initialized`, all relevant plugin tests,
   and the real Jellyfin integration checks. Commit both the gitlink and metadata.

For example, after selecting a specific release:

```bash
git -C external/iamparadox/plugin-pages fetch --tags origin
git -C external/iamparadox/plugin-pages checkout --detach 3.0.3.0
git diff --submodule=log
python tools/validate_dependencies.py --initialized
git add external/iamparadox/plugin-pages dependencies/plugins.json
```

Dependabot may propose submodule commits. Review each proposal together with the
metadata and compatibility tests; an upstream branch tip is not a release qualification.

## Build isolation and upstream contributions

The three `Directory.*` files here stop the parent repository's MSBuild and central
package settings from leaking into upstream projects. Their own project files remain
authoritative. For a deliberate upstream build, use its documented version property,
for example `dotnet build <upstream-project.csproj> -p:JellyfinVersion=12.2.0`.

For persistent upstream changes, create a fork, make commits there, and intentionally
change the submodule URL to that fork with the original repository documented as
upstream. A dirty submodule worktree is not a reproducible dependency update. Preserve
and consult the license included with each upstream repository when distributing it.
