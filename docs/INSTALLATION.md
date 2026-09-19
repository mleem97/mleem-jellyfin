# Installation – Music Toolkit

## Voraussetzungen
- Jellyfin 12.1, .NET 10 SDK für alle Plugins dieser Repo.
- Plugin-Repositories (Jellyfin Dashboard → Plugins → Repositories):
  - `https://raw.githubusercontent.com/mleem97/mleem-jellyfin/main/manifest.json`
  - `https://www.iamparadox.dev/jellyfin/plugins/manifest.json`
  - `https://raw.githubusercontent.com/jyourstone/jellyfin-plugin-manifest/main/manifest.json`
  - `https://raw.githubusercontent.com/Felitendo/jellyfin-plugin-lyrics/master/manifest.json (correct branch; a configured `manifest` branch 404s)`
- Optional: Paradox `PluginPages` + `FileTransformation` für `/pages/spotify-music`.

## Installieren
1. Toolkit-Plugin aus dem Katalog installieren, Jellyfin neu starten.
2. Dashboard → Plugins → Music Toolkit: Gracenote-ID eintragen, „Gracenote testen".
3. „Vorschau berechnen" prüfen, dann „Umbenennen ausführen" und „Duplikate bereinigen".
4. Musikseite öffnen → Redirect auf `/pages/spotify-music`.

## Build
```bash
dotnet restore plugins/MusicToolkit/MusicToolkit.csproj
dotnet build plugins/MusicToolkit/MusicToolkit.csproj -c Release
dotnet test tests/Jellyfin.Plugin.MusicToolkit.Tests/Jellyfin.Plugin.MusicToolkit.Tests.csproj -c Release
```
