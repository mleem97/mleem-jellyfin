# Maintaining an extensible Jellyfin plugin monorepo

## Intended architecture

The repository is one development workspace and one plugin catalog, with independently
versioned and packaged plugins. A change to one plugin does not require a release of
every other plugin. New plugins are discovered from metadata rather than added to
copied workflows. The current development baseline is Jellyfin 12.2 and .NET 10;
historical catalog entries describe their actual published binaries separately.

| Boundary | Contents | Source of truth |
| --- | --- | --- |
| `plugins/<Slug>/` | One owned plugin, configuration, APIs/services, optional web resources and README | `plugin.json` and the plugin project |
| `tests/<Slug>.Tests/` | Independent behavior/security tests for a new plugin | Its test project, registered in the solution |
| `tests/Jellyfin.Plugins.Tests/` | Existing shared regression/integration suite | Existing test project; split gradually when editing a plugin |
| `tests/music-suite/`, `tests/dashboard-widget/` | Browser DOM and interaction tests | `package.json` plus `package-lock.json` |
| `external/<upstream>/<project>/` | Unmodified upstream repositories with independent histories | Git submodule commit and upstream license |
| `dependencies/plugins.json` | Runtime relationships, pinned source versions and exact server assets | Validated dependency metadata |
| `Directory.Packages.props` | NuGet package versions shared by owned projects | Central Package Management |
| `Directory.Build.props`, `global.json` | Common framework, deterministic builds, lockfile policy and SDK | Shared build configuration |
| `.github/scripts/` | Discovery, validation, packaging and release operations | Shared tooling and its tests |
| `tools/` | Development scaffolding and dependency checks | Version-controlled CLI tools |
| `docs/` | Architecture decisions, operation and real-server qualification | Evidence with explicit pending checks |
| `manifest.json` | Installable, published release entries | Verified release assets and checksums |
| `build.yaml` | Human/tool-readable summary of current plugin metadata | Generated; never edited independently |

Keep plugin implementations independent. A plugin must not rely on another owned
plugin's static singleton, controllers or internal storage. If several plugins need a
real stable contract, define a small versioned abstraction deliberately; do not create
a large shared framework before repeated behavior has been identified and tested.

## What changed and why

The previous structure repeated package versions across ten projects, maintained a
partial plugin list in workflows and in `build.yaml`, and contained overlapping
release paths. An upgrade could therefore leave projects on different Jellyfin APIs
or omit an entire plugin from CI. The new central package file, lockfiles and metadata
validator prevent this drift. Generated `build.yaml` remains a convenient summary
without becoming another registry to maintain.

The upstream sources now live in two pinned submodules. MSBuild isolation files in
`external/` prevent our central package and build defaults from changing their own
project behavior. They remain optional runtime plugins accessed through reflection.
Source presence does not install them on a server and does not qualify compatibility.

The catalog previously advertised several Jellyfin 10.x variants of the same .NET 10
binary. Those aliases were removed after the actual release archives were inspected.
Compatibility is an assertion about a tested binary, not a list of desired versions.

## Start working

```bash
git clone --branch dev --recurse-submodules https://github.com/mleem97/mleem-jellyfin.git
cd mleem-jellyfin
git switch dev
dotnet restore Jellyfin.Plugins.sln --locked-mode
dotnet build Jellyfin.Plugins.sln -c Release --no-restore
dotnet test Jellyfin.Plugins.sln -c Release --no-build --no-restore
python .github/scripts/validate_repository.py
python tools/validate_dependencies.py --initialized
```

Install the SDK recorded in `global.json` and Node.js for the browser tests. Restricted
containers may need `-m:1 -p:UseSharedCompilation=false` for the build because MSBuild
worker processes and compiler servers use local IPC.

## Add a plugin

```bash
python tools/new_plugin.py PlaybackInsights --name "Playback Insights" --dry-run
python tools/new_plugin.py PlaybackInsights --name "Playback Insights"
dotnet restore Jellyfin.Plugins.sln
dotnet test tests/PlaybackInsights.Tests/PlaybackInsights.Tests.csproj -c Release
python .github/scripts/validate_repository.py
```

The scaffolder creates a unique GUID, source entry point, disabled-by-default
configuration, metadata, README and a separate test project. It adds both projects
to the solution and regenerates `build.yaml`. It refuses existing names and unsafe
paths. It does not create a release, catalog entry or placeholder success tests.

Implement the behavior, add meaningful tests, generate and commit both lockfiles,
then validate a real Jellyfin install before publishing. A README must explain scope,
configuration, access requirements, optional dependencies, compatibility and recovery.

`plugin.json` owns the plugin slug, GUID, display metadata, target ABI, framework,
output assembly, package files and runtime dependency IDs. The project and metadata
versions must agree; the validator checks this. Release tooling changes the plugin's
four-part version as a single controlled operation. Never reuse a GUID for a new plugin.

## Dependency updates

### NuGet

Update versions in `Directory.Packages.props`, not individual project files. Keep
Jellyfin Common/Controller/Model aligned. To intentionally refresh the resolved graph:

```bash
dotnet restore Jellyfin.Plugins.sln --force-evaluate
dotnet build Jellyfin.Plugins.sln -c Release --no-restore
dotnet test Jellyfin.Plugins.sln -c Release --no-build --no-restore
```

Review and commit the affected `packages.lock.json` files. CI uses locked restore,
so an unreviewed change in a transitive package cannot silently replace the tested
graph. A dependency upgrade that changes the server baseline also requires updating
plugin `targetAbi`, dependency runtime assets and the compatibility evidence. Run
`python .github/scripts/validate_repository.py --sync-build` afterwards.

### Upstream Jellyfin plugins

See [`external/README.md`](../external/README.md). File Transformation and Plugin
Pages are pinned to release `3.0.3.0`, with exact 12.2 runtime asset references.
Dependabot proposes updates; it does not grant runtime compatibility. Update the
gitlink and `dependencies/plugins.json` together and validate their relationships.

When you need to change an upstream plugin, work in a fork and point the submodule
to a committed fork revision. Keep the upstream remote documented. Never commit
untracked local edits as though the parent repository had captured them.

### Browser dependencies

Commit npm lockfiles and use `npm ci` in CI. Keep browser tools scoped to the plugin
test directory; ordinary .NET plugin builds should not require a full frontend toolchain
unless that plugin actually builds a frontend bundle.

## Branches and releases

Use `dev` for integration and `main` for reviewed release-ready changes. Keep fixes
small and linked to issues; use issue branches and PRs into `dev`. A final `dev` to
`main` PR promotes a tested batch when explicitly requested. Branch names do not
prove that their content is integrated: verify commit ancestry and the resulting tree.

Each plugin keeps its own version and tag namespace, for example
`MusicSuite-v1.0.0.1`. Only the selected plugin's catalog entry is updated. Package
contents are declared explicitly; do not copy every DLL from `bin/` because that can
accidentally include Jellyfin host assemblies or unrelated plugins. Additional runtime
libraries such as TagLibSharp must be declared and present in the archive.

A release requires a successful build, behavior and authorization tests, package
validation, real-server install/upgrade/uninstall checks and matching downloaded asset
hashes. Jellyfin's catalog checksum is MD5; release tooling additionally verifies
SHA-256 and publishes a sidecar. A Git tag or a green unit test alone does not establish
that a plugin is installable or its UI works in the server.

Do not recreate lower historical versions to close an old issue. The HDD and provider
release plans predate existing 1.x versions. Preserve history, document the superseded
version target and qualify the next forward release. See
[`RELEASE_READINESS.md`](RELEASE_READINESS.md) for the actual evidence and gaps.

## How to scale beyond the current eight plugins

The present metadata-driven pipeline has no hard-coded plugin list. It runs the whole
suite deliberately while the repository is small. The following changes should be
introduced when build duration or ownership actually requires them:

1. **Split existing shared tests by plugin.** Move plugin-specific tests into
   `tests/<Slug>.Tests`, retaining a small cross-plugin contract suite. This reduces
   accidental build coupling. New scaffolds already use separate test projects.
2. **Select affected plugins for PR jobs.** Derive them from the Git diff and metadata.
   A shared build/package/tooling change must select all plugins; an upstream update
   must select every consumer. Keep a scheduled full-suite check to catch drift.
3. **Add explicit browser test metadata.** When another plugin needs UI tests, describe
   its test directory and command in a constrained schema so CI can discover it as
   it discovers .NET projects. Avoid arbitrary command strings from external metadata.
4. **Extract repeated adapters after contract tests exist.** The Paradox registration
   pattern is a good candidate for a small source-linked adapter or a separately
   versioned internal package. Avoid making one installed plugin the shared library.
5. **Shard large build matrices.** CI runners and provider matrix limits are finite;
   a growing plugin inventory needs bounded parallelism and affected-project selection.
6. **Add ownership when more maintainers join.** Use CODEOWNERS per plugin and shared
   tooling, with branch rules requiring the actual CI jobs. Do not enable requirements
   for jobs that cannot run because of account/runner infrastructure problems.

The old checked-in `package/` DLL/PDB and `HDDDisplay_test.zip` were removed from the
development tree after confirming that current source, workflows and catalog do not
reference them. They remain in Git history. Historical template setup notes are
marked under `docs/legacy/`. Production packages belong in releases and development
outputs in ignored artifact directories.

## Sources and decisions

- [Git submodules](https://git-scm.com/docs/gitsubmodules): independent history and exact gitlink commits.
- [NuGet Central Package Management](https://learn.microsoft.com/nuget/consume-packages/Central-Package-Management): shared versions and project references without version attributes.
- [NuGet lock files](https://learn.microsoft.com/nuget/consume-packages/package-references-in-project-files#locking-dependencies): repeatable restores and locked mode.
- [File Transformation](https://github.com/IAmParadox27/jellyfin-plugin-file-transformation): reflection integration and version-specific runtime support.
- [Plugin Pages](https://github.com/IAmParadox27/jellyfin-plugin-pages): page registration and File Transformation dependency.
