// HDD Display Admin Dashboard widget.
// Shows Jellyfin library mounts directly on the Jellyfin dashboard.
(function () {
    const WIDGET_ID = 'hdd-display-dashboard-widget';
    const MEDIA_COLORS = {
        movies: '#5591c7',
        tvshows: '#7aa95c',
        music: '#b78ad6',
        video: '#d6a85c',
        mixed: '#888888',
        other: '#666666'
    };

    function esc(value) {
        return String(value || '').replace(/[&<>"']/g, function (char) {
            return ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[char];
        });
    }

    function fmtBytes(bytes) {
        if (!bytes || bytes <= 0) return '0 B';
        const units = ['B', 'KB', 'MB', 'GB', 'TB', 'PB'];
        const index = Math.min(Math.floor(Math.log(bytes) / Math.log(1024)), units.length - 1);
        return (bytes / Math.pow(1024, index)).toFixed(index > 1 ? 1 : 0) + ' ' + units[index];
    }

    function pct(used, total) {
        return total > 0 ? Math.round((used / total) * 100) : 0;
    }

    function apiGet(url) {
        if (window.ApiClient && ApiClient.getJSON && ApiClient.getUrl) {
            return ApiClient.getJSON(ApiClient.getUrl(url));
        }

        return fetch(url).then(function (response) {
            if (!response.ok) throw new Error('HTTP ' + response.status);
            return response.json();
        });
    }

    function findTarget() {
        const dashboardCards = Array.from(document.querySelectorAll('.paperList, .cardBox, .dashboardSection, [data-role="content"]'));
        const pathsCandidate = dashboardCards.find(function (node) {
            return /Pfade|Paths/i.test(node.textContent || '');
        });

        return pathsCandidate || document.querySelector('[data-role="content"]') || document.querySelector('main') || document.body;
    }

    function createWidget() {
        const widget = document.createElement('div');
        widget.id = WIDGET_ID;
        widget.style.cssText = 'background:#181818;border:1px solid rgba(255,255,255,.08);border-radius:10px;padding:16px;margin:0 0 16px;color:#e0e0e0;';
        widget.innerHTML = '<div style="display:flex;justify-content:space-between;gap:12px;align-items:center;margin-bottom:12px">'
            + '<div><h3 style="margin:0;color:#fff;font-size:16px">HDD Display</h3><div style="font-size:12px;color:#888">Storage and GPU overview</div></div>'
            + '<div style="display:flex;align-items:center;gap:8px"><button id="hdd-display-refresh" type="button" style="font-size:11px;padding:4px 8px;border-radius:999px;border:1px solid rgba(255,255,255,.18);background:#222;color:#ddd">Refresh scan</button><div id="hdd-display-widget-state" style="font-size:12px;color:#888">Loading...</div></div>'
            + '</div><div id="hdd-display-widget-content"></div><div id="hdd-display-widget-gpu" style="margin-top:14px"></div>';
        return widget;
    }

    function renderSegments(drive) {
        const total = drive.totalBytes || 0;
        const usage = drive.usage || [];
        if (!usage.length || !total) {
            return '<div style="height:8px;background:#272727;border-radius:999px;overflow:hidden"><div style="height:100%;width:' + pct(drive.usedBytes || 0, total) + '%;background:#5591c7"></div></div>';
        }

        const segments = usage.map(function (entry) {
            const width = Math.max(1, pct(entry.usedBytes || 0, total));
            const color = MEDIA_COLORS[entry.mediaType] || MEDIA_COLORS.other;
            return '<div title="' + esc(entry.mediaType) + ' · ' + fmtBytes(entry.usedBytes || 0) + '" style="height:100%;width:' + width + '%;background:' + color + '"></div>';
        }).join('');

        const legend = usage.map(function (entry) {
            const color = MEDIA_COLORS[entry.mediaType] || MEDIA_COLORS.other;
            return '<span style="display:inline-flex;align-items:center;gap:4px;margin-right:8px"><span style="display:inline-block;width:7px;height:7px;border-radius:999px;background:' + color + '"></span>' + esc(entry.mediaType) + ': ' + fmtBytes(entry.usedBytes || 0) + '</span>';
        }).join('');

        return '<div style="height:8px;background:#272727;border-radius:999px;overflow:hidden;display:flex">' + segments + '</div>'
            + '<div style="font-size:10px;color:#777;margin-top:4px">' + legend + '</div>';
    }

    function renderGpu(gpu) {
        if (!gpu || !gpu.isAvailable) {
            return '<div style="font-size:12px;color:#888;border-top:1px solid rgba(255,255,255,.08);padding-top:10px">GPU: unavailable' + (gpu && gpu.diagnostic ? ' · ' + esc(gpu.diagnostic) : '') + '</div>';
        }

        const devices = gpu.devices || [];
        const deviceHtml = devices.map(function (device) {
            return '<div style="display:flex;justify-content:space-between;font-size:12px;margin-top:6px">'
                + '<strong>' + esc(device.name) + '</strong>'
                + '<span>' + (device.gpuUtilizationPercent || 0) + '% GPU · ' + (device.memoryUsedMiB || 0) + '/' + (device.memoryTotalMiB || 0) + ' MiB VRAM</span>'
                + '</div>';
        }).join('');

        return '<div style="border-top:1px solid rgba(255,255,255,.08);padding-top:10px">'
            + '<div style="display:flex;justify-content:space-between;font-size:12px;color:#aaa"><span>GPU telemetry</span><span>' + (gpu.jellyfinFfmpegProcessCount || 0) + ' ffmpeg sessions</span></div>'
            + deviceHtml
            + '</div>';
    }

    function render(data) {
        const state = document.querySelector('#hdd-display-widget-state');
        const content = document.querySelector('#hdd-display-widget-content');
        const gpu = document.querySelector('#hdd-display-widget-gpu');
        if (!state || !content || !gpu) return;

        const drives = data.drives || [];
        const cacheState = data.usage && data.usage.cacheHit ? 'cached' : 'fresh';
        state.textContent = drives.length + ' mounts · ' + cacheState;
        content.innerHTML = drives.map(function (drive) {
            const usedPct = pct(drive.usedBytes || 0, drive.totalBytes || 0);
            return '<div style="margin-top:10px">'
                + '<div style="display:flex;justify-content:space-between;font-size:12px;margin-bottom:5px"><strong>' + esc(drive.label || drive.name) + '</strong><span>' + usedPct + '% · ' + fmtBytes(drive.freeBytes || 0) + ' free</span></div>'
                + renderSegments(drive)
                + '<div style="font-size:10px;color:#666;margin-top:3px;font-family:monospace">' + esc(drive.name) + '</div>'
                + '</div>';
        }).join('') || '<div style="font-size:12px;color:#888">No matching Jellyfin library mounts detected.</div>';
        gpu.innerHTML = renderGpu(data.gpu);
    }

    function load(refresh) {
        const suffix = refresh ? '?refresh=true' : '';
        apiGet('Plugins/HddDisplay/AdminDashboard/Overview' + suffix)
            .then(render)
            .catch(function (error) {
                const state = document.querySelector('#hdd-display-widget-state');
                if (state) state.textContent = 'Error';
                const content = document.querySelector('#hdd-display-widget-content');
                if (content) content.innerHTML = '<div style="color:#dd6974;font-size:12px">' + esc(error.message) + '</div>';
            });
    }

    function bindRefresh() {
        const button = document.querySelector('#hdd-display-refresh');
        if (!button) return;
        button.addEventListener('click', function () {
            const state = document.querySelector('#hdd-display-widget-state');
            if (state) state.textContent = 'Refreshing...';
            load(true);
        });
    }

    function inject() {
        if (document.querySelector('#' + WIDGET_ID)) return;
        const target = findTarget();
        if (!target) {
            window.setTimeout(inject, 500);
            return;
        }

        const widget = createWidget();
        target.insertBefore(widget, target.firstChild);
        bindRefresh();
        load(false);
        window.setInterval(function () { load(false); }, 5000);
    }

    document.addEventListener('pageshow', function () {
        if (window.location.hash.includes('dashboard') || !window.location.hash) {
            inject();
        }
    });

    if (document.readyState !== 'loading') {
        window.setTimeout(inject, 500);
    } else {
        document.addEventListener('DOMContentLoaded', inject);
    }
})();
