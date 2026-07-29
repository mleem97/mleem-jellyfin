# ADR 0001: HDD Display dashboard injection

- Status: Accepted
- Date: 2026-07-29
- Applies to: Jellyfin 10.10.x and 10.11.x

## Context

Jellyfin server plugins can provide configuration pages through `IHasWebPages`, but Jellyfin Web does not provide a stable public extension point for arbitrary cards on the Admin Dashboard. Coupling HDD Display to internal React/DOM implementation details or rewriting Jellyfin Web files would make updates and recovery unsafe.

Official references:

- https://github.com/jellyfin/jellyfin-plugin-template
- https://github.com/jellyfin/jellyfin-web/issues/5234

## Decision

HDD Display uses a two-part, fail-open integration:

1. The plugin exposes its dashboard JavaScript as a versioned, authenticated server asset.
2. Administrators opt in to loading that asset at the deployment boundary, for example through a custom Jellyfin Web image or a reverse-proxy response transformation that inserts one `<script>` element into `/web/index.html` responses.

The plugin itself does **not** edit, replace or persist changes to Jellyfin Web files. The loader configuration remains outside the Jellyfin data and plugin directories and can be removed independently.

The JavaScript:

- activates only on the Admin Dashboard route;
- verifies that the current user is an administrator before requesting diagnostics;
- augments the `Paths`/`Pfade` area when it can be identified;
- falls back to a separate card without hiding native Jellyfin content;
- cleans up timers and DOM nodes when navigation leaves the dashboard;
- treats all backend or DOM failures as non-fatal.

The normal HDD Display plugin configuration page remains the diagnostic fallback when no loader is configured.

## Security and recovery

- No system or Jellyfin Web file is modified by the plugin.
- The script and data endpoints require an authenticated administrator.
- Missing assets, disabled configuration or incompatible DOM structures leave the native dashboard unchanged.
- Removing the loader or uninstalling the plugin leaves no persistent browser or server-side modification.
- Cache-busting uses the plugin version so upgrades do not reuse stale JavaScript.

## Compatibility policy

The supported baseline is Jellyfin Server/Web 10.10.x and 10.11.x. Each new Jellyfin Web major version requires an explicit compatibility test before it is added to the support matrix. DOM selectors are treated as adapters and must have a fallback path.

## Alternatives rejected

### Rewrite Jellyfin Web files from the plugin

Rejected because it requires write access to installation files, is fragile across updates and can leave a broken dashboard after uninstall or partial failure.

### Depend on undocumented internal frontend modules

Rejected because they are not a stable plugin contract and may change without server-plugin compatibility guarantees.

### Configuration page only

Retained as a fallback, but rejected as the primary experience because HDD Display is intended to provide at-a-glance operational information on the Admin Dashboard.

## Consequences

- Dashboard integration is explicit rather than automatic.
- Docker and reverse-proxy deployments can add or remove the loader without rebuilding the plugin.
- Native installs need a custom Jellyfin Web package or equivalent non-destructive loader configuration.
- The plugin remains safe when the loader is absent.
