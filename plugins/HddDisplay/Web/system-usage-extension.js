// HDD Display system-path segment extension.
(function () {
    'use strict';

    if (window.__hddDisplaySystemUsageLoaded) return;
    window.__hddDisplaySystemUsageLoaded = true;

    const WIDGET_ID = 'hdd-display-dashboard-widget';
    const SECTION_ID = 'hdd-display-system-usage';
    const REFRESH_BUTTON_ID = 'hdd-display-refresh';
    const POLL_INTERVAL_MS = 30000;

    const runtime = {
        timer: null,
        observer: null,
        requestSerial: 0
    };

    function isDashboardRoute() {
        const hash = String(window.location.hash || '').toLowerCase();
        return hash === '#/dashboard'
            || hash.startsWith('#/dashboard?')
            || hash === '#dashboard'
            || hash.includes('/dashboard.html');
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

    function color(category) {
        switch (String(category || '').toLowerCase()) {
            case 'image-cache': return '#58a6a6';
            case 'transcodes': return '#dd6974';
            case 'metadata': return '#b78ad6';
            case 'logs': return '#d6a85c';
            case 'temp': return '#8d99ae';
            case 'plugins': return '#7aa95c';
            case 'configuration': return '#e0a96d';
            case 'cache': return '#5591c7';
            case 'program-data': return '#777777';
            case 'web': return '#9c89b8';
            default: return '#666666';
        }
    }

    function label(category) {
        return String(category || 'other')
            .split('-')
            .map(function (part) { return part.charAt(0).toUpperCase() + part.slice(1); })
            .join(' ');
    }

    function bytes(value) {
        const size = Number(value || 0);
        if (size <= 0) return '0 B';
        const units = ['B', 'KB', 'MB', 'GB', 'TB', 'PB'];
        const index = Math.min(Math.floor(Math.log(size) / Math.log(1024)), units.length - 1);
        return (size / Math.pow(1024, index)).toFixed(index > 1 ? 1 : 0) + ' ' + units[index];
    }

    function createSection() {
        const section = document.createElement('div');
        section.id = SECTION_ID;
        section.style.cssText = 'border-top:1px solid rgba(255,255,255,.08);margin-top:14px;padding-top:10px';

        const title = document.createElement('div');
        title.textContent = 'Jellyfin system usage';
        title.style.cssText = 'font-size:12px;font-weight:600;margin-bottom:8px';
        section.appendChild(title);

        const state = document.createElement('div');
        state.dataset.role = 'state';
        state.textContent = 'Loading system paths…';
        state.style.cssText = 'font-size:11px;opacity:.65';
        state.setAttribute('aria-live', 'polite');
        section.appendChild(state);

        const content = document.createElement('div');
        content.dataset.role = 'content';
        section.appendChild(content);
        return section;
    }

    function ensureSection() {
        const widget = document.getElementById(WIDGET_ID);
        if (!widget) return null;

        let section = document.getElementById(SECTION_ID);
        if (!section) {
            section = createSection();
            const gpu = document.getElementById('hdd-display-widget-gpu');
            widget.insertBefore(section, gpu || null);
        }

        return section;
    }

    function renderMount(container, mountPath, entries) {
        const total = entries.reduce(function (sum, entry) {
            return sum + Number(entry.usedBytes || 0);
        }, 0);

        const block = document.createElement('div');
        block.style.cssText = 'margin-top:9px';
        container.appendChild(block);

        const heading = document.createElement('div');
        heading.style.cssText = 'display:flex;justify-content:space-between;gap:8px;font-size:11px;flex-wrap:wrap';
        block.appendChild(heading);

        const mount = document.createElement('span');
        mount.textContent = mountPath || 'Mount';
        mount.style.fontFamily = 'monospace';
        heading.appendChild(mount);

        const totalLabel = document.createElement('span');
        totalLabel.textContent = bytes(total) + ' classified';
        totalLabel.style.opacity = '.65';
        heading.appendChild(totalLabel);

        const bar = document.createElement('div');
        bar.style.cssText = 'height:7px;background:rgba(255,255,255,.1);border-radius:999px;overflow:hidden;display:flex;margin-top:4px';
        block.appendChild(bar);

        if (total > 0) {
            entries.forEach(function (entry) {
                const segment = document.createElement('div');
                const width = Math.max(1, Math.round((Number(entry.usedBytes || 0) / total) * 100));
                segment.style.cssText = 'height:100%;width:' + width + '%;background:' + color(entry.category);
                segment.title = label(entry.category) + ' · ' + bytes(entry.usedBytes);
                bar.appendChild(segment);
            });
        }

        const legend = document.createElement('div');
        legend.style.cssText = 'display:flex;gap:8px;flex-wrap:wrap;font-size:10px;opacity:.65;margin-top:4px';
        block.appendChild(legend);

        entries.forEach(function (entry) {
            const item = document.createElement('span');
            item.style.cssText = 'display:inline-flex;align-items:center;gap:4px';

            const dot = document.createElement('span');
            dot.style.cssText = 'width:7px;height:7px;border-radius:999px;background:' + color(entry.category);
            dot.setAttribute('aria-hidden', 'true');
            item.appendChild(dot);
            item.appendChild(document.createTextNode(label(entry.category) + ': ' + bytes(entry.usedBytes)));
            legend.appendChild(item);
        });
    }

    function render(data) {
        if (!isDashboardRoute()) return;
        const section = ensureSection();
        if (!section) return;

        const state = section.querySelector('[data-role="state"]');
        const content = section.querySelector('[data-role="content"]');
        const entries = Array.isArray(data.entries) ? data.entries : [];
        const grouped = new Map();
        entries.forEach(function (entry) {
            const key = String(entry.mountPath || '');
            if (!grouped.has(key)) grouped.set(key, []);
            grouped.get(key).push(entry);
        });

        state.textContent = entries.length
            ? entries.length + ' exclusive path categories · ' + (data.cacheHit ? 'cached' : 'fresh')
            : 'No readable Jellyfin system paths detected.';
        content.replaceChildren();
        grouped.forEach(function (mountEntries, mountPath) {
            renderMount(content, mountPath, mountEntries);
        });
    }

    function renderError(error) {
        const section = ensureSection();
        if (!section) return;
        const state = section.querySelector('[data-role="state"]');
        const content = section.querySelector('[data-role="content"]');
        state.textContent = 'System usage unavailable';
        content.textContent = error && error.message || 'The system-path scan failed.';
        content.style.cssText = 'font-size:11px;color:#dd6974;margin-top:4px';
    }

    function load(forceRefresh) {
        if (!isDashboardRoute() || !document.getElementById(WIDGET_ID)) return;
        const requestId = ++runtime.requestSerial;
        const suffix = forceRefresh ? '?refresh=true' : '';
        apiGet('Plugins/HddDisplay/SystemUsage' + suffix)
            .then(function (data) {
                if (requestId === runtime.requestSerial) render(data);
            })
            .catch(function (error) {
                if (requestId === runtime.requestSerial) renderError(error);
            });
    }

    function activate() {
        if (!isDashboardRoute()) {
            cleanup();
            return;
        }

        if (ensureSection()) {
            load(false);
            if (runtime.timer === null) {
                runtime.timer = window.setInterval(function () { load(false); }, POLL_INTERVAL_MS);
            }
        }

        if (!runtime.observer && document.body) {
            runtime.observer = new MutationObserver(function () {
                if (isDashboardRoute() && !document.getElementById(SECTION_ID)) activate();
            });
            runtime.observer.observe(document.body, { childList: true, subtree: true });
        }
    }

    function cleanup() {
        runtime.requestSerial += 1;
        if (runtime.timer !== null) {
            window.clearInterval(runtime.timer);
            runtime.timer = null;
        }
        if (runtime.observer) {
            runtime.observer.disconnect();
            runtime.observer = null;
        }
        const section = document.getElementById(SECTION_ID);
        if (section) section.remove();
    }

    document.addEventListener('click', function (event) {
        const target = event.target;
        if (target && target.id === REFRESH_BUTTON_ID) load(true);
    }, true);

    window.addEventListener('hashchange', activate);
    window.addEventListener('popstate', activate);
    window.addEventListener('pageshow', activate);
    window.addEventListener('pagehide', cleanup);

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', activate, { once: true });
    } else {
        activate();
    }
})();
