# Storage Dashboard Widget Installation & Setup

Damit das Storage-Dashboard-Widget **oben auf der Admin-Startseite** angezeigt wird, brauchst du:

## 1. Dieses Plugin selbst installieren

Wie normal: `.zip` hochladen in Jellyfin Admin → Plugins.

## 2. FileTransformation Plugin installieren

- GitHub: https://github.com/IAmParadox/Jellyfin.Plugin.FileTransformation
- oder Jellyfin Packages durchsuchen nach `Jellyfin.Plugin.FileTransformation`

## 3. FileTransformation konfigurieren

Nach Installation im Admin → **Plugins** → **FileTransformation** konfigurieren.

### Transformation hinzufügen:

**Match Path:** `/jellyfin/jellyfin-web/index.html`  
**Replace Pattern:** `</head>`  
**Replace With:**
```html
<script src="/web/Dashboard.html?page=StorageDashboard" id="storage-widget"></script>
</head>
```

**ODER (empfohlen für direkte Injection):**

**Match Path:** `/jellyfin/jellyfin-web/index.html`  
**Replace Pattern:** `</head>`  
**Replace With (COMPLETE INLINE):**
```html
<script>
(function(){var s=document.createElement('script');s.textContent=`... widget code here ...`;document.head.appendChild(s);})();
</script>
</head>
```

**Einfacher Weg (kopiere die gesamte `dashboard-widget-injection.html` ins Replace-Feld):**

1. Öffne `dashboard-widget-injection.html`
2. Kopiere den kompletten `<script>...</script>` Block (von `<script>` bis `</script>`)
3. In FileTransformation:
   - **Match Path:** `/jellyfin/jellyfin-web/index.html`
   - **Replace Pattern:** `</head>`
   - **Replace With:** [hier den kompletten `<script>` Block einfügen]
   - **Enabled:** `Yes`

4. **Jellyfin neustarten** (wichtig!)

## 4. Dashboard aufrufen

- Geh zu **Admin → Dashboard** (oder nur **Admin** wenn du dort landest)
- Oben sollte jetzt die **Storage Dashboard** Karte sichtbar sein mit:
  - Gesamtspeicher KPIs
  - Gestapelte Balken pro Laufwerk (farbig nach Medientyp)
  - Schnelllink zur detaillierten Seite

## Fallback: Detaillierte Seite

Wenn die Injection nicht funktioniert, ist die vollständige Seite immer verfügbar unter:  
**Admin → Plugins → Storage Dashboard** (oder über Zahnrad-Icon)

## Troubleshooting

- **Widget taucht nicht auf:** 
  - FileTransformation-Log prüfen (`/config/log/...`)
  - Browser-Console: `F12` → Console auf Fehler prüfen
  - Browser-Cache löschen (`Shift+F5`)
  - Jellyfin neu starten

- **Nur weiße Karte, keine Daten:**
  - API antwortet nicht: `/Plugins/StorageDashboard/Storage` in URL-Bar testen
  - CORS/Auth-Fehler in Browser Console prüfen
  - Im Console `fetch('/Plugins/StorageDashboard/Storage').then(r=>r.json()).then(console.log)` probieren

- **Transformation wird nicht angewendet:**
  - FileTransformation ist aktiv? (Admin → Plugins prüfen)
  - Jellyfin nach Änderung neu gestartet?
  - Match Path genau `/jellyfin/jellyfin-web/index.html`?
  - Replac ement Pattern `</head>` exakt geschrieben?
