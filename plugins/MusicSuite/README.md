# MusicSuite

MusicSuite contains the enhanced album view originally planned as **Better MusicDisplay**, plus the newer, optional Spotify-style dashboard. The original `0.1.0.0` milestone describes the album MVP; existing newer releases must not be downgraded to satisfy that historical version label.

## Enhanced albums

Open the **Albums / Alben** tab of a Jellyfin music library. With MusicSuite enabled and the FileTransformation integration available, the plugin replaces only the album item container after its authenticated context and first page have loaded successfully.

- Albums load in batches of 100 by default; administrators can choose 1–200.
- A virtual window renders at most 500 album cards. Spacer rows preserve scroll geometry and earlier albums remain reachable when scrolling back.
- Covers load lazily; missing or failed covers display a keyboard-accessible album card with a placeholder.
- Live search and genre input are debounced. Changing a query aborts its HTTP request and invalidates older responses immediately.
- Title, album artist, year and date-added sorts combine with favorites, missing-cover, genre and year filters.
- Query changes reset paging and scroll position. Empty intermediate missing-cover batches continue scanning rather than incorrectly showing an empty library.
- Returning from album details restores loaded pages, filters and scroll position. The three most recent in-memory view snapshots are scoped by Jellyfin user and library route.
- Small, medium and large tile choices are saved through the authenticated per-user settings API. Failed saves restore the last saved layout and display an error.

## Administrator configuration

The MusicSuite configuration page saves real server settings through Jellyfin's plugin-configuration API:

| Setting | Default | Effect |
|---|---|---|
| `Enabled` | `true` | Enables the enhanced album view and optional navigation. |
| `ForceForAllUsers` | `false` | Optionally redirects the native music overview to the Spotify dashboard. Album routes remain available. |
| `AllowUserCustomization` | `true` | Enables per-user layout writes. When disabled, stored personal choices are retained but administrator defaults are displayed. |
| `DefaultTileSize` | `Medium` | Applies to new users, reset layouts and users whose customization is disabled. |
| `BatchSize` | `100` | Album query size, bounded to 1–200. |
| `SearchDebounceMs` | `300` | Search delay, bounded to 100–2,000 milliseconds. |

Layout controls stay disabled until the server confirms the user's permission. Changing a tile size preserves the user's other preferences. Concurrent changes are saved in selection order. The reset action uses the administrator's actual defaults.

## Installation and native fallback

Install the plugin using the repository catalog and restart Jellyfin. The optional Paradox FileTransformation bridge is initialized during server startup. Without that dependency, the native music pages remain usable; the plugin does not modify Jellyfin's web files on disk.

The additional Spotify dashboard remains available through its Jellyfin plugin page. Automatic navigation is off by default and checks its assets before redirecting. Its stylesheet does not hide native Jellyfin pages.

When initial context validation, loading or injection fails, the native album container stays visible. Leaving the album route or disabling MusicSuite restores the original container's display and accessibility attributes. The injected asset URLs support a Jellyfin server URL prefix such as `/jellyfin`.

For an uninstall, remove MusicSuite and restart Jellyfin, then reload the browser so the server no longer injects its scripts. Installation, real Paradox compatibility, screenshots and upgrade/uninstall checks must be verified on the target Jellyfin host before a production release is marked verified.

## API

All data and settings endpoints require an authenticated Jellyfin session:

- `GET /Plugins/MusicSuite/Albums/Context?parentId=<library-id>`
- `GET /Plugins/MusicSuite/Albums`
- `GET /Plugins/MusicSuite/Users/{userId}/Settings`
- `PUT /Plugins/MusicSuite/Users/{userId}/Settings`
- `DELETE /Plugins/MusicSuite/Users/{userId}/Settings`

Album query parameters are `startIndex`, `limit`, `parentId`, `searchTerm`, `sortBy`, `sortOrder`, `isFavorite`, `missingCover`, `genre`, `year` and `fields`. Queries execute in the authenticated user's library context. Normal pages contain at most 200 albums. Missing-cover queries inspect at most 1,000 source rows per request and return `nextStartIndex` for continuation; a page with no matching items can still have `hasMore=true`.

Users can access their own layout; administrators may manage another user's layout. Disabling customization rejects both save and reset writes at the API.

## Build and verification

```bash
dotnet build plugins/MusicSuite/MusicSuite.csproj -c Release
dotnet test tests/Jellyfin.Plugins.Tests/Jellyfin.Plugins.Tests.csproj -c Release
npm ci --prefix tests/music-suite
npm test --prefix tests/music-suite
```

The JavaScript tests simulate 0, 10, 1,000 and 10,000 albums, initial and later request failures, stale replies and rejections, real `AbortSignal` propagation, empty continuation pages, keyboard activation, failed covers, bounded rendering, back navigation, per-user snapshot isolation, denied settings, failed/ordered saves, configuration persistence, disablement and missing dashboard assets. They use a mock backend and do not connect to a production library. The C# tests cover authentication, settings isolation, JSON round-trips, administrator defaults, embedded assets and injection behavior.
