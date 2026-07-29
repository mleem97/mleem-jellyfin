# ADR 0002: Better MusicDisplay Albums view injection

- Status: Accepted
- Date: 2026-07-29
- Applies to: Jellyfin Web 10.10.x and 10.11.x

## Context

Jellyfin server plugins do not receive a stable public frontend extension point for replacing arbitrary library tabs. Better MusicDisplay must therefore tolerate route and DOM changes without leaving a blank music library.

## Decision

Better MusicDisplay exposes a versioned authenticated JavaScript loader. Administrators load it through the same non-destructive deployment-boundary mechanism described for HDD Display; the plugin never edits Jellyfin Web files.

The loader:

1. extracts `topParentId` or `parentId` from the active route;
2. requires an explicit Albums route or selected Albums tab;
3. validates the parent against `GET /Plugins/BetterMusicDisplay/Albums/Context`;
4. activates only when the parent is a configured music library and the plugin is enabled;
5. waits for a separately registered Albums renderer;
6. hides the native item container only after that renderer reports a successful mount;
7. restores the native view after any error, route change, unregister or unmount.

## Consequences

- Loading the lifecycle asset by itself never hides Jellyfin content.
- Other library types and non-Album music tabs remain untouched.
- Renderer implementation can evolve independently while sharing one fallback contract.
- DOM selectors remain adapters and require compatibility testing for each supported Jellyfin Web generation.
- Uninstalling the plugin or removing the external loader leaves no modified Jellyfin Web file.
