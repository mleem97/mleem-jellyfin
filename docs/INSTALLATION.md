# Installation der Jellyfin-Plugins

## Server und Paket müssen zusammenpassen

Die aktuell veröffentlichten eigenen Pakete im Katalog wurden für Jellyfin 12.1 und .NET 10 gebaut. Der aktuelle Entwicklungsstand benötigt Jellyfin 12.2 und .NET 10. Prüfe vor der Installation die Serverversion und den `targetAbi`-Eintrag der jeweiligen Paketversion. Für die Installation fertiger Pakete wird kein .NET SDK benötigt; das SDK aus `global.json` wird nur zum Bauen des Quellcodes gebraucht.

Ein Paket für .NET 10 lässt sich nicht durch einen geänderten Manifest-Eintrag auf Jellyfin 10.x verwenden. Ältere Server benötigen separat gebaute und getestete kompatible Plugin-Versionen.

## Installation aus dem Katalog

1. Öffne **Dashboard → Plugins → Repositories** als Administrator.
2. Ergänze `https://raw.githubusercontent.com/mleem97/mleem-jellyfin/main/manifest.json`.
3. Wähle im Katalog das gewünschte Plugin und eine zur Serverversion passende Paketversion.
4. Installiere das Paket und starte Jellyfin neu.
5. Öffne die Plugin-Konfiguration und aktiviere nur die benötigten Funktionen.

Die Plugins können unabhängig voneinander installiert werden. MusicToolkit wird bis zu einem korrigierten Release nicht im Katalog angeboten: Dem historischen Archiv 1.0.0.1 fehlt `TagLibSharp.dll`.

## Web-Integration und Begleitplugins

HDD Display und MusicSuite ergänzen Jellyfin Web. Je nach gewünschter Einbindung werden Paradox **File Transformation** und **Plugin Pages** verwendet. Installiere passende Versionen aus dem Originalkatalog:

```text
https://www.iamparadox.dev/jellyfin/plugins/manifest.json
```

Die im Repository als Submodule aufgenommenen Quellen ersetzen die Plugin-Installation auf dem Server nicht. Einzelheiten und festgehaltene Quellversionen stehen in [Monorepo Maintenance](MONOREPO_MAINTENANCE.md).

HDD Display zeigt seine Hauptfunktion im Administrator-Dashboard. Wenn die Web-Injection nicht verfügbar ist, muss das normale Jellyfin-Dashboard weiter nutzbar bleiben. MusicSuite bietet eine eigene Musikansicht; die native Ansicht bleibt der Rückweg bei deaktivierter oder fehlgeschlagener Integration.

NVIDIA-Werte sind optional. In Docker sind der NVIDIA Container Toolkit und expliziter GPU-Passthrough erforderlich; `nvidia-smi` muss im Jellyfin-Container erreichbar sein. Ein Server ohne NVIDIA-GPU kann die Speicheranzeige trotzdem verwenden. Die noch erforderlichen Umgebungstests sind in der [HDD-Testmatrix](HDD_DISPLAY_TEST_MATRIX.md) aufgeführt.

## Entwicklungspaket installieren

Baue das gewünschte Plugin und erzeuge ein validiertes ZIP:

```bash
dotnet restore Jellyfin.Plugins.sln --locked-mode
dotnet build plugins/HddDisplay/HddDisplay.csproj -c Release --no-restore
python .github/scripts/package_smoke_test.py --plugin HddDisplay --output artifacts
```

Stoppe Jellyfin, entpacke das ZIP in einen eigenen Plugin-Ordner unter `<Jellyfin-Datenverzeichnis>/plugins/` und starte den Server neu. Verwende die DLLs aus dem ZIP; kopiere nicht unkontrolliert alle Jellyfin- und Framework-Assemblies aus dem Build-Verzeichnis. Kontrolliere anschließend die Plugin-Statusanzeige und Serverlogs auf Ladefehler.

## Update und Deinstallation

Für ein Update dieselbe Plugin-GUID verwenden, eine höhere Paketversion installieren und Jellyfin neu starten. Für die Deinstallation das Plugin über das Dashboard entfernen und den Server neu starten. Bei einer manuellen Installation den Server stoppen und nur den betroffenen Plugin-Ordner entfernen.

Nach Entfernung eines UI-Plugins prüfen, ob die native Ansicht zurückkehrt. Bei verbleibenden Browserdateien die Jellyfin-Webseite vollständig neu laden. Verschlüsselte Provider-Zugangsdaten sind an den serverseitigen Schlüsselbestand gebunden; für einen Serverumzug müssen Plugin-Konfiguration und zugehöriger Schlüsselbestand zusammen gesichert werden.

## Lokal prüfen

```bash
dotnet test Jellyfin.Plugins.sln -c Release
python -m unittest discover -s tests/release-tooling -v
npm ci --prefix tests/dashboard-widget
npm test --prefix tests/dashboard-widget
npm ci --prefix tests/music-suite
npm test --prefix tests/music-suite
```

Automatisierte Tests ersetzen nicht die Installations-, Update-, Berechtigungs- und Browserprüfung auf einer realen Jellyfin-Installation. Der aktuelle Nachweisstand steht in [Release Readiness](RELEASE_READINESS.md).
