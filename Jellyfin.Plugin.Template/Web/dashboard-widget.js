// Storage Dashboard Widget for Jellyfin Admin Dashboard
// Injects a compact storage overview card at the top of the admin dashboard

(function () {
    const TYPE_CONFIG = {
        movies: { label: 'Filme', color: '#5591c7', icon: '🎬' },
        tvshows: { label: 'Serien', color: '#4f98a3', icon: '📺' },
        music: { label: 'Musik', color: '#fdab43', icon: '🎵' },
        books: { label: 'Bücher', color: '#a86fdf', icon: '📚' },
        photos: { label: 'Fotos', color: '#e8af34', icon: '📷' },
        homevideos: { label: 'Heimvideos', color: '#6daa45', icon: '🎥' },
    };

    function typeInfo(t) {
        const key = String(t || '').toLowerCase().replace(/\s/g, '');
        return TYPE_CONFIG[key] || { label: 'Sonstiges', color: '#888', icon: '📂' };
    }

    function fmtBytes(b) {
        if (b <= 0) return '0 B';
        const units = ['B', 'KB', 'MB', 'GB', 'TB'];
        const i = Math.floor(Math.log(b) / Math.log(1024));
        return (b / Math.pow(1024, i)).toFixed(i > 1 ? 1 : 0) + ' ' + units[i];
    }

    function pct(used, total) {
        return total > 0 ? Math.round((used / total) * 100) : 0;
    }

    function createStorageWidget() {
        const widget = document.createElement('div');
        widget.id = 'storage-dashboard-widget';
        widget.style.cssText = `
            margin-bottom: 24px;
            padding: 20px;
            background: #181818;
            border: 1px solid rgba(255,255,255,0.08);
            border-radius: 10px;
            color: #e0e0e0;
        `;
        widget.innerHTML = `
            <div style="display: flex; align-items: center; justify-content: space-between; margin-bottom: 12px;">
                <div>
                    <h3 style="margin: 0; font-size: 16px; font-weight: 600; color: #fff;">Storage Dashboard</h3>
                    <p style="margin: 2px 0 0; font-size: 12px; color: #888;">HDD-Auslastung</p>
                </div>
                <button id="storage-refresh-btn" style="
                    background: #272727;
                    border: 1px solid rgba(255,255,255,0.08);
                    color: #888;
                    border-radius: 6px;
                    padding: 6px 12px;
                    font-size: 12px;
                    cursor: pointer;
                    transition: color 0.15s;
                ">↻ Aktualisieren</button>
            </div>
            <div id="storage-content" style="font-size: 12px; color: #aaa;">Wird geladen...</div>
        `;
        return widget;
    }

    function apiGet(url) {
        if (window.ApiClient && ApiClient.getJSON && ApiClient.getUrl) {
            return ApiClient.getJSON(ApiClient.getUrl(url));
        }
        return fetch(url).then(r => {
            if (!r.ok) throw new Error('HTTP ' + r.status);
            return r.json();
        });
    }

    function renderStorageWidget(data) {
        const content = document.querySelector('#storage-content');
        if (!content) return;

        const drives = data.drives || [];
        const libraries = data.libraries || [];

        const totalBytes = drives.reduce((s, d) => s + (d.totalBytes || 0), 0);
        const usedBytes = drives.reduce((s, d) => s + (d.usedBytes || 0), 0);
        const freeBytes = drives.reduce((s, d) => s + (d.freeBytes || 0), 0);
        const usedPct = pct(usedBytes, totalBytes);

        let html = `
            <div style="display: grid; grid-template-columns: repeat(auto-fit, minmax(120px, 1fr)); gap: 12px; margin-bottom: 12px;">
                <div style="background: #1f1f1f; padding: 10px; border-radius: 6px;">
                    <div style="font-size: 10px; color: #888; margin-bottom: 4px;">Gesamt</div>
                    <div style="font-size: 14px; font-weight: 600; color: #fff;">${fmtBytes(totalBytes)}</div>
                </div>
                <div style="background: #1f1f1f; padding: 10px; border-radius: 6px;">
                    <div style="font-size: 10px; color: #888; margin-bottom: 4px;">Belegt</div>
                    <div style="font-size: 14px; font-weight: 600; color: #5591c7;">${usedPct}%</div>
                </div>
                <div style="background: #1f1f1f; padding: 10px; border-radius: 6px;">
                    <div style="font-size: 10px; color: #888; margin-bottom: 4px;">Frei</div>
                    <div style="font-size: 14px; font-weight: 600; color: #6daa45;">${fmtBytes(freeBytes)}</div>
                </div>
                <div style="background: #1f1f1f; padding: 10px; border-radius: 6px;">
                    <div style="font-size: 10px; color: #888; margin-bottom: 4px;">Laufwerke</div>
                    <div style="font-size: 14px; font-weight: 600; color: #fff;">${drives.length}</div>
                </div>
            </div>
        `;

        if (drives.length > 0) {
            html += '<div style="margin-top: 12px; border-top: 1px solid rgba(255,255,255,0.08); padding-top: 12px;">';
            html += drives.map(drive => {
                const usedPctDrive = pct(drive.usedBytes || 0, drive.totalBytes || 0);
                const drivePath = drive.name || '/';
                const matchedLibs = libraries.filter(lib =>
                    (lib.paths || []).some(p => String(p).startsWith(drivePath))
                );

                let barHtml = '<div style="display: flex; height: 6px; background: #272727; border-radius: 99px; overflow: hidden; margin: 6px 0;">';
                if (matchedLibs.length > 0) {
                    matchedLibs.forEach(lib => {
                        const segPct = usedPctDrive / matchedLibs.length;
                        const info = typeInfo(lib.type);
                        barHtml += `<div style="width: ${segPct}%; background: ${info.color}; opacity: 0.85;"></div>`;
                    });
                } else {
                    barHtml += `<div style="width: ${usedPctDrive}%; background: #5591c7;"></div>`;
                }
                barHtml += '</div>';

                return `
                    <div style="margin-bottom: 10px; padding-bottom: 10px; border-bottom: 1px solid rgba(255,255,255,0.04);">
                        <div style="display: flex; justify-content: space-between; margin-bottom: 4px; font-size: 11px;">
                            <strong>${drive.label || drive.name}</strong>
                            <span>${fmtBytes(drive.totalBytes || 0)} (${usedPctDrive}%)</span>
                        </div>
                        ${barHtml}
                        <div style="display: flex; flex-wrap: wrap; gap: 4px; margin-top: 4px;">
                            ${(matchedLibs || []).map(lib => {
                                const info = typeInfo(lib.type);
                                return `<span style="font-size: 10px; background: #272727; color: ${info.color}; padding: 2px 6px; border-radius: 3px;">${info.icon} ${lib.name}</span>`;
                            }).join('')}
                        </div>
                    </div>
                `;
            }).join('');
            html += '</div>';
        }

        html += `<div style="margin-top: 12px; font-size: 10px; color: #555;">
            <a href="#/plugins/StorageDashboard" style="color: #5591c7; text-decoration: none;">Zu detailliertem Dashboard →</a>
        </div>`;

        content.innerHTML = html;
    }

    function loadAndRender() {
        const content = document.querySelector('#storage-content');
        if (!content) return;

        content.innerHTML = '<div style="text-align: center; padding: 12px; color: #888;">Wird geladen...</div>';

        apiGet('Plugins/StorageDashboard/Storage')
            .then(data => renderStorageWidget(data || { drives: [], libraries: [] }))
            .catch(e => {
                console.error('Storage Dashboard Widget Error:', e);
                content.innerHTML = '<div style="color: #dd6974;">Fehler beim Laden der Speicherdaten</div>';
            });
    }

    function injectWidget() {
        // Wait for dashboard page to load
        if (!document.body) {
            setTimeout(injectWidget, 100);
            return;
        }

        // Versuche Widget am Anfang der Dashboard-Content einzufügen
        // Jellyfin-Web rendert dynamisch, daher hier verschiedene Selektoren versuchen
        const targets = [
            document.querySelector('[data-role="content"]'),
            document.querySelector('.dashboardPage'),
            document.querySelector('main'),
            document.body
        ];

        let target = null;
        for (const t of targets) {
            if (t && !t.querySelector('#storage-dashboard-widget')) {
                target = t;
                break;
            }
        }

        if (!target) {
            setTimeout(injectWidget, 500);
            return;
        }

        const widget = createStorageWidget();
        target.insertBefore(widget, target.firstChild);

        // Refresh button
        document.querySelector('#storage-refresh-btn')?.addEventListener('click', loadAndRender);

        // Initial load
        loadAndRender();
    }

    // Trigger on page show (Jellyfin-Web event)
    document.addEventListener('pageshow', function (e) {
        if (window.location.hash.includes('dashboard') || !window.location.hash) {
            injectWidget();
        }
    });

    // Fallback for initial load
    if (document.readyState !== 'loading') {
        setTimeout(injectWidget, 500);
    } else {
        document.addEventListener('DOMContentLoaded', injectWidget);
    }
})();
