// HDD Display Admin Dashboard widget.
// Shows Jellyfin library mounts directly on the Jellyfin dashboard.
(function () {
    const WIDGET_ID = 'hdd-display-dashboard-widget';
    const REFRESH_BUTTON_ID = 'hdd-display-refresh';
    const STATE_ID = 'hdd-display-widget-state';
    const CONTENT_ID = 'hdd-display-widget-content';
    const GPU_ID = 'hdd-display-widget-gpu';

    function mediaColor(mediaType) {
        switch (String(mediaType || '').toLowerCase()) {
            case 'movies':
                return '#5591c7';
            case 'tvshows':
                return '#7aa95c';
            case 'music':
                return '#b78ad6';
            case 'video':
                return '#d6a85c';
            case 'mixed':
                return '#888888';
            default:
                return '#666666';
        }
    }

    function unitLabel(index) {
        switch (index) {
            case 0:
                return 'B';
            case 1:
                return 'KB';
            case 2:
                return 'MB';
            case 3:
                return 'GB';
            case 4:
                return 'TB';
            default:
                return 'PB';
        }
    }

    function fmtBytes(bytes) {
        if (!bytes || bytes <= 0) return '0 B';
        const index = Math.min(Math.floor(Math.log(bytes) / Math.log(1024)), 5);
        return (bytes / Math.pow(1024, index)).toFixed(index > 1 ? 1 : 0) + ' ' + unitLabel(index);
    }

    function pct(used, total) {
        return total > 0 ? Math.round((used / total) * 100) : 0;
    }

    function apiGet(url) {
        const apiClient = window.ApiClient;
        if (apiClient && apiClient.getJSON && apiClient.getUrl) {
            return apiClient.getJSON(apiClient.getUrl(url));
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

    function appendText(parent, tagName, text, cssText) {
        const node = document.createElement(tagName);
        node.textContent = text;
        if (cssText) node.style.cssText = cssText;
        parent.appendChild(node);
        return node;
    }

    function createWidget() {
        const widget = document.createElement('div');
        widget.id = WIDGET_ID;
        widget.style.cssText = 'background:#181818;border:1px solid rgba(255,255,255,.08);border-radius:10px;padding:16px;margin:0 0 16px;color:#e0e0e0;';

        const header = document.createElement('div');
        header.style.cssText = 'display:flex;justify-content:space-between;gap:12px;align-items:center;margin-bottom:12px';
        widget.appendChild(header);

        const titleBlock = document.createElement('div');
        header.appendChild(titleBlock);
        appendText(titleBlock, 'h3', 'HDD Display', 'margin:0;color:#fff;font-size:16px');
        appendText(titleBlock, 'div', 'Storage and GPU overview', 'font-size:12px;color:#888');

        const actions = document.createElement('div');
        actions.style.cssText = 'display:flex;align-items:center;gap:8px';
        header.appendChild(actions);

        const button = document.createElement('button');
        button.id = REFRESH_BUTTON_ID;
        button.type = 'button';
        button.textContent = 'Refresh scan';
        button.style.cssText = 'font-size:11px;padding:4px 8px;border-radius:999px;border:1px solid rgba(255,255,255,.18);background:#222;color:#ddd';
        actions.appendChild(button);

        appendText(actions, 'div', 'Loading...', 'font-size:12px;color:#888').id = STATE_ID;

        const content = document.createElement('div');
        content.id = CONTENT_ID;
        widget.appendChild(content);

        const gpu = document.createElement('div');
        gpu.id = GPU_ID;
        gpu.style.cssText = 'margin-top:14px';
        widget.appendChild(gpu);

        return widget;
    }

    function renderSegments(container, drive) {
        const total = drive.totalBytes || 0;
        const usage = drive.usage || [];
        const bar = document.createElement('div');
        bar.style.cssText = 'height:8px;background:#272727;border-radius:999px;overflow:hidden;display:flex';
        container.appendChild(bar);

        if (!usage.length || !total) {
            const fill = document.createElement('div');
            fill.style.cssText = 'height:100%;width:' + pct(drive.usedBytes || 0, total) + '%;background:#5591c7';
            bar.appendChild(fill);
            return;
        }

        usage.forEach(function (entry) {
            const segment = document.createElement('div');
            const width = Math.max(1, pct(entry.usedBytes || 0, total));
            segment.title = String(entry.mediaType || 'other') + ' · ' + fmtBytes(entry.usedBytes || 0);
            segment.style.cssText = 'height:100%;width:' + width + '%;background:' + mediaColor(entry.mediaType);
            bar.appendChild(segment);
        });

        const legend = document.createElement('div');
        legend.style.cssText = 'font-size:10px;color:#777;margin-top:4px';
        container.appendChild(legend);

        usage.forEach(function (entry) {
            const item = document.createElement('span');
            item.style.cssText = 'display:inline-flex;align-items:center;gap:4px;margin-right:8px';
            legend.appendChild(item);

            const dot = document.createElement('span');
            dot.style.cssText = 'display:inline-block;width:7px;height:7px;border-radius:999px;background:' + mediaColor(entry.mediaType);
            item.appendChild(dot);
            item.appendChild(document.createTextNode(String(entry.mediaType || 'other') + ': ' + fmtBytes(entry.usedBytes || 0)));
        });
    }

    function renderGpu(container, gpu) {
        container.replaceChildren();

        const card = document.createElement('div');
        card.style.cssText = 'border-top:1px solid rgba(255,255,255,.08);padding-top:10px';
        container.appendChild(card);

        if (!gpu || !gpu.isAvailable) {
            card.style.fontSize = '12px';
            card.style.color = '#888';
            card.textContent = 'GPU: unavailable' + (gpu && gpu.diagnostic ? ' · ' + gpu.diagnostic : '');
            return;
        }

        const header = document.createElement('div');
        header.style.cssText = 'display:flex;justify-content:space-between;font-size:12px;color:#aaa';
        card.appendChild(header);
        appendText(header, 'span', 'GPU telemetry');
        appendText(header, 'span', (gpu.jellyfinFfmpegProcessCount || 0) + ' ffmpeg sessions');

        const devices = gpu.devices || [];
        devices.forEach(function (device) {
            const row = document.createElement('div');
            row.style.cssText = 'display:flex;justify-content:space-between;font-size:12px;margin-top:6px';
            card.appendChild(row);
            appendText(row, 'strong', device.name || 'NVIDIA GPU');
            appendText(row, 'span', (device.gpuUtilizationPercent || 0) + '% GPU · ' + (device.memoryUsedMiB || 0) + '/' + (device.memoryTotalMiB || 0) + ' MiB VRAM');
        });
    }

    function renderDrive(container, drive) {
        const usedPct = pct(drive.usedBytes || 0, drive.totalBytes || 0);
        const wrapper = document.createElement('div');
        wrapper.style.cssText = 'margin-top:10px';
        container.appendChild(wrapper);

        const header = document.createElement('div');
        header.style.cssText = 'display:flex;justify-content:space-between;font-size:12px;margin-bottom:5px';
        wrapper.appendChild(header);
        appendText(header, 'strong', drive.label || drive.name || 'Mount');
        appendText(header, 'span', usedPct + '% · ' + fmtBytes(drive.freeBytes || 0) + ' free');

        renderSegments(wrapper, drive);
        appendText(wrapper, 'div', drive.name || '', 'font-size:10px;color:#666;margin-top:3px;font-family:monospace');
    }

    function render(data) {
        const state = document.querySelector('#' + STATE_ID);
        const content = document.querySelector('#' + CONTENT_ID);
        const gpu = document.querySelector('#' + GPU_ID);
        if (!state || !content || !gpu) return;

        const drives = data.drives || [];
        const cacheState = data.usage && data.usage.cacheHit ? 'cached' : 'fresh';
        state.textContent = drives.length + ' mounts · ' + cacheState;
        content.replaceChildren();

        if (!drives.length) {
            appendText(content, 'div', 'No matching Jellyfin library mounts detected.', 'font-size:12px;color:#888');
        } else {
            drives.forEach(function (drive) {
                renderDrive(content, drive);
            });
        }

        renderGpu(gpu, data.gpu);
    }

    function renderError(error) {
        const state = document.querySelector('#' + STATE_ID);
        if (state) state.textContent = 'Error';
        const content = document.querySelector('#' + CONTENT_ID);
        if (!content) return;
        content.replaceChildren();
        appendText(content, 'div', error.message || 'Failed to load HDD Display data.', 'color:#dd6974;font-size:12px');
    }

    function load(refresh) {
        const suffix = refresh ? '?refresh=true' : '';
        apiGet('Plugins/HddDisplay/AdminDashboard/Overview' + suffix)
            .then(render)
            .catch(renderError);
    }

    function bindRefresh() {
        const button = document.querySelector('#' + REFRESH_BUTTON_ID);
        if (!button) return;
        button.addEventListener('click', function () {
            const state = document.querySelector('#' + STATE_ID);
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
