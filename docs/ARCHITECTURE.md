# Architecture – Music Toolkit & Spotify UX

## Überblick
`Jellyfin.Plugin.MusicToolkit` (GUID `9f3e582a-281b-4b21-8c43-b295cbfa51de`) bereinigt Musikbibliotheken
(Hash-Präfixe, Duplikate, SQLite-FK19) und legt per Paradox-Bridge eine Spotify-3-Spalten-Oberfläche über die native Musikseite.

## Komponenten
- `Services/FilenameCleaner`: Hash-Strip + Tag-Fallback-Parser + Pattern-Builder.
- `Services/AudioHashService`: MD5 über Audio-Region (ID3v2/ID3v1-Skip), Scoring FLAC 100+ / MP3-320 80 / Bitrate/10, Master-Wahl.
- `Services/SafeRenameService`: `File.Move` + `ILibraryManager.UpdateItemAsync(MetadataEdit)` – ItemId stabil, kein FK19 in `UserData`.
- `Services/GracenoteClient`: MusicID-XML (`REGISTER`, `ALBUM_SEARCH EXTENDED COVER,REVIEW,GENRE`), `IHttpClientFactory`.
- `Providers/MusicHoardersImageProvider` (`Order=1`, `MusicAlbum|MusicArtist`, `Primary`).
- `Providers/GracenoteMetadataProvider` (`Order=2`, Auto-Register, `GN_ID`).
- `Integrations/ParadoxBridge`: Reflection auf `PluginPages` + `FileTransformation`, fail-open.
- `Integrations/SmartListsIntegration`: lädt `configs/smartlists-rules.json`.
- `Api/MusicToolkitController` (`RequiresElevation`): `DryRun`, `ExecuteRename`, `Deduplicate`, `TestGracenote`.
- `Web/spotify-dashboard.*` + `spotify-theme.css` + `music-redirect.js`.

## Datenfluss
1. Admin ruft `DryRun` → `FilenameCleaner.Parse` + Confidence → Tabelle.
2. `ExecuteRename` → `SafeRenameService.RenameAsync` → Item-Pfad-Update.
3. `Deduplicate` → `AudioHashService.FindDuplicatesAsync` → Quarantäne via `SafeRenameService`.
4. Dashboard lädt via `ApiClient` (Items, Playlists, Resume) und spielt via `PlaybackManager`.

## Entscheidungen
- Lose Paradox-Kopplung (keine harte Referenz) für optionale Installation.
- Audio-Hash statt Datei-Hash, damit Retags keine Duplikate vortäuschen.
- ItemId-stabiles Rename statt Delete+Rescan (FK19-Fix).
