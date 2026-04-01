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
<script>
(function() {
    const scriptUrl = '/web/plugins/Jellyfin.Plugin.Template/dashboard-widget.js';
    const script = document.createElement('script');
    script.src = scriptUrl;
    script.async = true;
    if (document.head) {
        document.head.appendChild(script);
    } else {
        document.body.appendChild(script);
    }
})();
</script>
</head>
```

**Enabled:** `Yes`

## 4. Jellyfin neustarten

- Oder Admin → **Restart** klicken

## 5. Dashboard aufrufen

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
  - URL des Widgets testen: `http://jellyfin:8096/web/plugins/Jellyfin.Plugin.Template/dashboard-widget.js`

- **Nur weiße Karte, keine Daten:**
  - API antwortet nicht: `/Plugins/StorageDashboard/Storage` in URL-Bar testen
  - CORS/Auth-Fehler in Browser Console prüfen

- **Script lädt nicht:**
  - Plugin muss neu installiert werden (Widget-Files sind neu)
  - Cache löschen: Browser-Cache + Browser-Reload (`Shift+F5`)
