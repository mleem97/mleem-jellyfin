// HDD Display Admin Dashboard widget.
// Loaded through the opt-in deployment integration documented in ADR 0001.
(function () {
    'use strict';

    if (window.__hddDisplayDashboardWidgetLoaded) return;
    window.__hddDisplayDashboardWidgetLoaded = true;

    const WIDGET_ID = 'hdd-display-dashboard-widget';
    const REFRESH_BUTTON_ID = 'hdd-display-refresh';
    const STATE_ID = 'hdd-display-widget-state';
    const CONTENT_ID = 'hdd-display-widget-content';
    const GPU_ID = 'hdd-display-widget-gpu';
    const POLL_INTERVAL_MS = 15000;
    const RETRY_INTERVAL_MS = 500;

    const runtime = {
        pollTimer: null,
        retryTimer: null,
        observer: null,
        requestSerial: 0
    };

    function isAdminDashboardRoute() {
        const hash = String(window.location.hash || '').toLowerCase();
        return hash === '#/dashboard'
            || hash.startsWith('#/dashboard?')
            || hash === '#dashboard'
            || hash.includes('/dashboard.html');
    }

    function mediaColor(mediaType) {
        switch (String(mediaType || '').toLowerCase()) {
            case 'movies': return '#5591c7';
            case 'tvshows': return '#7aa95c';
            case 'music': return '#b78ad6';
            case 'video': return '#d6a85c';
            case 'mixed': return '#888888';
            default: return '#666666';
        }
    }

    function unitLabel(index) {
        return ['B', 'KB', 'MB', 'GB', 'TB', 'PB'][index] || 'PB';
    }

    function fmtBytes(bytes) {
        const value = Number(bytes || 0);
        if (value <= 0) return '0 B';
        const index = Math.min(Math.floor(Math.log(value) / Math.log(1024)), 5);
        const digits = index > 1 ? 1 : 0;
        return (value / Math.pow(1024, index)).toFixed(digits) + ' ' + unitLabel(index);
    }

    function pct(used, total) {
        const maximum = Number(total || 0);
        if (maximum <= 0) return 0;
        return Math.max(0, Math.min(100, Math.round((Number(used || 0) / maximum) * 100)));
    }

    function apiGet(url) {
        const apiClient = window.ApiClient;
        if (apiClient && apiClient.getJSON && apiClient.getUrl) {
            return apiClient.getJSON(apiClient.getUrl(url));
        }

        return fetch(url, { credentials: 'same-origin' }).then(function (response) {
            if (!response.ok) throw new Error('HTTP ' + response.status);
            return response.json();
        });
    }

    function normalizedTitle(node) {
        return String(node && node.textContent || '').replace(/\s+/g, ' ').trim().toLowerCase();
    }

    function findPathsCard() {
        const cards = Array.from(document.querySelectorAll(
            '.dashboardSection, .verticalSection, .paperList, .cardBox, .section0'
        ));

        return cards.find(function (card) {
            const titleNodes = card.querySelectorAll(
                'h1, h2, h3, .sectionTitle, .sectionTitleText, .listItemBodyText'
            );
            return Array.from(titleNodes).some(function (titleNode) {
                const title = normalizedTitle(titleNode);
                return title === 'paths' || title === 'pfade';
            });
        }) || null;
    }

    function findFallbackTarget() {
        return document.querySelector('.dashboardPage [data-role="content"]')
            || document.querySelector('[data-role="content"]')
            || document.querySelector('main');
    }

    function appendText(parent, tagName, text, cssText) {
        const node = document.createElement(tagName);
        node.textContent = text;
        if (cssText) node.style.cssText = cssText;
        parent.appendChild(node);
        return node;
    }

    function createWidget() {
        const widget = document.createElement('section');
        widget.id = WIDGET_ID;
        widget.setAttribute('aria-label', 'HDD Display storage and GPU overview');
        widget.style.cssText = [
            'background:var(--background-color-card,#181818)',
            'border:1px solid rgba(255,255,255,.08)',
            'border-radius:10px',
            'padding:16px',
            'margin:0 0 16px',
            'color:var(--primary-foreground-color,#e0e0e0)'
        ].join(';');

        const header = document.createElement('div');
        header.style.cssText = 'display:flex;justify-content:space-between;gap:12px;align-items:center;margin-bottom:12px;flex-wrap:wrap';
        widget.appendChild(header);

        const titleBlock = document.createElement('div');
        header.appendChild(titleBlock);
        appendText(titleBlock, 'h3', 'HDD Display', 'margin:0;color:inherit;font-size:16px');
        appendText(titleBlock, 'div', 'Storage and GPU overview', 'font-size:12px;opacity:.65');

        const actions = document.createElement('div');
        actions.style.cssText = 'display:flex;align-items:center;gap:8px';
        header.appendChild(actions);

        const button = document.createElement('button');
        button.id = REFRESH_BUTTON_ID;
        button.type = 'button';
        button.className = 'raised button-submit emby-button';
        button.textContent = 'Refresh scan';
        button.style.cssText = 'font-size:11px;padding:5px 10px;min-height:auto';
        actions.appendChild(button);

        const state = appendText(actions, 'div', 'Loading…', 'font-size:12px;opacity:.65');
        state.id = STATE_ID;
        state.setAttribute('aria-live', 'polite');

        const content = document.createElement('div');
        content.id = CONTENT_ID;
        widget.appendChild(content);

        const gpu = document.createElement('div');
        gpu.id = GPU_ID;
        gpu.style.cssText = 'margin-top:14px';
        widget.appendChild(gpu);

        button.addEventListener('click', function () {
            state.textContent = 'Refreshing…';
            load(true);
        });

        return widget;
    }

    function renderSegments(container, drive) {
        const total = Number(drive.totalBytes || 0);
        const usage = Array.isArray(drive.usage) ? drive.usage : [];
        const bar = document.createElement('div');
        bar.style.cssText = 'height:8px;background:rgba(255,255,255,.1);border-radius:999px;overflow:hidden;display:flex';
        bar.setAttribute('role', 'img');
        bar.setAttribute('aria-label', pct(drive.usedBytes, total) + ' percent used');
        container.appendChild(bar);

        if (!usage.length || !total) {
            const fill = document.createElement('div');
            fill.style.cssText = 'height:100%;width:' + pct(drive.usedBytes, total) + '%;background:#5591c7';
            bar.appendChild(fill);
            return;
        }

        usage.forEach(function (entry) {
            const segment = document.createElement('div');
            const width = Math.max(1, pct(entry.usedBytes, total));
            segment.title = String(entry.mediaType || 'other') + ' · ' + fmtBytes(entry.usedBytes);
            segment.style.cssText = 'height:100%;width:' + width + '%;background:' + mediaColor(entry.mediaType);
            bar.appendChild(segment);
        });

        const legend = document.createElement('div');
        legend.style.cssText = 'font-size:10px;opacity:.65;margin-top:4px;display:flex;gap:8px;flex-wrap:wrap';
        container.appendChild(legend);

        usage.forEach(function (entry) {
            const item = document.createElement('span');
            item.style.cssText = 'display:inline-flex;align-items:center;gap:4px';
            legend.appendChild(item);

            const dot = document.createElement('span');
            dot.setAttribute('aria-hidden', 'true');
            dot.style.cssText = 'display:inline-block;width:7px;height:7px;border-radius:999px;background:' + mediaColor(entry.mediaType);
            item.appendChild(dot);
            item.appendChild(document.createTextNode(
                String(entry.mediaType || 'other') + ': ' + fmtBytes(entry.usedBytes)
            ));
        });
    }

    function renderGpu(container, gpu) {
        container.replaceChildren();

        const card = document.createElement('div');
        card.style.cssText = 'border-top:1px solid rgba(255,255,255,.08);padding-top:10px';
        container.appendChild(card);

        if (!gpu || !gpu.isAvailable) {
            card.style.fontSize = '12px';
            card.style.opacity = '.65';
            card.textContent = 'GPU: unavailable' + (gpu && gpu.diagnostic ? ' · ' + gpu.diagnostic : '');
            return;
        }

        const header = document.createElement('div');
        header.style.cssText = 'display:flex;justify-content:space-between;gap:8px;font-size:12px;opacity:.75;flex-wrap:wrap';
        card.appendChild(header);
        appendText(header, 'span', 'GPU telemetry');
        appendText(header, 'span', (gpu.jellyfinFfmpegProcessCount || 0) + ' ffmpeg sessions');

        (gpu.devices || []).forEach(function (device) {
            const row = document.createElement('div');
            row.style.cssText = 'display:flex;justify-content:space-between;gap:8px;font-size:12px;margin-top:6px;flex-wrap:wrap';
            card.appendChild(row);
            appendText(row, 'strong', device.name || 'NVIDIA GPU');
            appendText(
                row,
                'span',
                (device.gpuUtilizationPercent || 0) + '% GPU · '
                    + (device.memoryUsedMiB || 0) + '/' + (device.memoryTotalMiB || 0) + ' MiB VRAM'
            );
        });
    }

    function renderDrive(container, drive) {
        const usedPct = pct(drive.usedBytes, drive.totalBytes);
        const wrapper = document.createElement('div');
        wrapper.style.cssText = 'margin-top:10px';
        container.appendChild(wrapper);

        const header = document.createElement('div');
        header.style.cssText = 'display:flex;justify-content:space-between;gap:8px;font-size:12px;margin-bottom:5px;flex-wrap:wrap';
        wrapper.appendChild(header);
        appendText(header, 'strong', drive.label || drive.name || 'Mount');
        appendText(header, 'span', usedPct + '% · ' + fmtBytes(drive.freeBytes) + ' free');

        renderSegments(wrapper, drive);
        appendText(wrapper, 'div', drive.name || '', 'font-size:10px;opacity:.55;margin-top:3px;font-family:monospace;overflow-wrap:anywhere');
    }

    function render(data) {
        if (!isAdminDashboardRoute()) return;
        const widget = document.getElementById(WIDGET_ID);
        const state = document.getElementById(STATE_ID);
        const content = document.getElementById(CONTENT_ID);
        const gpu = document.getElementById(GPU_ID);
        if (!widget || !state || !content || !gpu) return;

        const drives = Array.isArray(data.drives) ? data.drives : [];
        const cacheState = data.usage && data.usage.cacheHit ? 'cached' : 'fresh';
        state.textContent = drives.length + ' mounts · ' + cacheState;
        content.replaceChildren();

        if (!drives.length) {
            appendText(content, 'div', 'No matching Jellyfin library mounts detected.', 'font-size:12px;opacity:.65');
        } else {
            drives.forEach(function (drive) { renderDrive(content, drive); });
        }

        renderGpu(gpu, data.gpu);
    }

    function renderError(error) {
        if (!isAdminDashboardRoute()) return;
        const state = document.getElementById(STATE_ID);
        const content = document.getElementById(CONTENT_ID);
        if (state) state.textContent = 'Error';
        if (!content) return;
        content.replaceChildren();
        appendText(
            content,
            'div',
            error && error.message || 'Failed to load HDD Display data.',
            'color:#dd6974;font-size:12px'
        );
    }

    function load(refresh) {
        if (!isAdminDashboardRoute() || !document.getElementById(WIDGET_ID)) return;
        const requestId = ++runtime.requestSerial;
        const suffix = refresh ? '?refresh=true' : '';
        apiGet('Plugins/HddDisplay/AdminDashboard/Overview' + suffix)
            .then(function (data) {
                if (requestId === runtime.requestSerial) render(data);
            })
            .catch(function (error) {
                if (requestId === runtime.requestSerial) renderError(error);
            });
    }

    function startPolling() {
        if (runtime.pollTimer !== null) return;
        runtime.pollTimer = window.setInterval(function () { load(false); }, POLL_INTERVAL_MS);
    }

    function startObserver() {
        if (runtime.observer || !document.body) return;
        runtime.observer = new MutationObserver(function () {
            if (isAdminDashboardRoute() && !document.getElementById(WIDGET_ID)) scheduleSync(50);
        });
        runtime.observer.observe(document.body, { childList: true, subtree: true });
    }

    function insertWidget() {
        if (!isAdminDashboardRoute() || document.getElementById(WIDGET_ID)) return false;

        const pathsCard = findPathsCard();
        const fallbackTarget = findFallbackTarget();
        const widget = createWidget();

        if (pathsCard && pathsCard.parentElement) {
            pathsCard.insertAdjacentElement('afterend', widget);
        } else if (fallbackTarget) {
            fallbackTarget.insertBefore(widget, fallbackTarget.firstChild);
        } else {
            return false;
        }

        load(false);
        startPolling();
        return true;
    }

    function cleanup() {
        runtime.requestSerial += 1;
        if (runtime.pollTimer !== null) {
            window.clearInterval(runtime.pollTimer);
            runtime.pollTimer = null;
        }
        if (runtime.retryTimer !== null) {
            window.clearTimeout(runtime.retryTimer);
            runtime.retryTimer = null;
        }
        if (runtime.observer) {
            runtime.observer.disconnect();
            runtime.observer = null;
        }
        const widget = document.getElementById(WIDGET_ID);
        if (widget) widget.remove();
    }

    function scheduleSync(delay) {
        if (runtime.retryTimer !== null) window.clearTimeout(runtime.retryTimer);
        runtime.retryTimer = window.setTimeout(function () {
            runtime.retryTimer = null;
            syncRoute();
        }, delay);
    }

    function syncRoute() {
        if (!isAdminDashboardRoute()) {
            cleanup();
            return;
        }

        startObserver();
        if (document.getElementById(WIDGET_ID)) {
            startPolling();
            return;
        }

        if (!insertWidget()) scheduleSync(RETRY_INTERVAL_MS);
    }

    window.addEventListener('hashchange', function () { scheduleSync(0); });
    window.addEventListener('popstate', function () { scheduleSync(0); });
    window.addEventListener('pageshow', function () { scheduleSync(0); });
    window.addEventListener('pagehide', cleanup);

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', function () { scheduleSync(0); }, { once: true });
    } else {
        scheduleSync(0);
    }
})();
