// Better MusicDisplay Albums view lifecycle and native-view fallback.
(function () {
    'use strict';

    if (window.BetterMusicDisplayAlbums && window.BetterMusicDisplayAlbums.loaderVersion === 1) return;

    const CONTAINER_ID = 'better-music-display-albums';
    const runtime = {
        renderer: null,
        container: null,
        nativeView: null,
        nativeDisplay: '',
        observer: null,
        retryTimer: null,
        requestSerial: 0,
        activeRouteKey: ''
    };

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

    function queryParameters() {
        const hash = String(window.location.hash || '');
        const questionMark = hash.indexOf('?');
        const hashQuery = questionMark >= 0 ? hash.substring(questionMark + 1) : '';
        const merged = new URLSearchParams(window.location.search || '');
        new URLSearchParams(hashQuery).forEach(function (value, key) {
            merged.set(key, value);
        });
        return merged;
    }

    function parseGuid(value) {
        const normalized = String(value || '').trim();
        return /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(normalized)
            || /^[0-9a-f]{32}$/i.test(normalized)
            ? normalized
            : '';
    }

    function routeParentId() {
        const query = queryParameters();
        return parseGuid(query.get('topParentId')) || parseGuid(query.get('parentId'));
    }

    function selectedTabIsAlbums() {
        const selectors = [
            '.emby-tab-button-active',
            '.activeTabButton',
            '[role="tab"][aria-selected="true"]',
            '.emby-tab-button[aria-selected="true"]'
        ];
        return selectors.some(function (selector) {
            return Array.from(document.querySelectorAll(selector)).some(function (node) {
                const text = String(node.textContent || '').replace(/\s+/g, ' ').trim().toLowerCase();
                return text === 'albums' || text === 'alben';
            });
        });
    }

    function routeLooksLikeAlbums() {
        const hash = String(window.location.hash || '').toLowerCase();
        const query = queryParameters();
        const explicitType = String(
            query.get('includeItemTypes') || query.get('type') || query.get('view') || ''
        ).toLowerCase();
        return hash.includes('albums')
            || explicitType.split(',').includes('musicalbum')
            || selectedTabIsAlbums();
    }

    function activePage() {
        const pages = Array.from(document.querySelectorAll('[data-role="page"], .page'));
        return pages.reverse().find(function (page) {
            if (page.classList.contains('hide') || page.classList.contains('ui-page-hidden')) return false;
            return page.getAttribute('aria-hidden') !== 'true';
        }) || null;
    }

    function nativeAlbumsView(page) {
        if (!page) return null;
        const candidates = [
            '.itemsContainer',
            '.vertical-wrap',
            '.cardCollection',
            '[data-testid="items-container"]'
        ];
        for (const selector of candidates) {
            const node = page.querySelector(selector);
            if (node) return node;
        }
        return page.querySelector('[data-role="content"]') || null;
    }

    function routeKey(parentId) {
        return String(window.location.hash || '') + '|' + parentId;
    }

    function createContainer(page, nativeView, parentId) {
        const container = document.createElement('section');
        container.id = CONTAINER_ID;
        container.dataset.parentId = parentId;
        container.setAttribute('aria-label', 'Better MusicDisplay Albums');
        container.hidden = true;
        container.style.cssText = 'width:100%;min-height:1px';

        if (nativeView && nativeView.parentElement) {
            nativeView.insertAdjacentElement('beforebegin', container);
        } else {
            const content = page && page.querySelector('[data-role="content"]');
            if (!content) return null;
            content.insertBefore(container, content.firstChild);
        }
        return container;
    }

    function hideNativeView() {
        if (!runtime.nativeView) return;
        runtime.nativeDisplay = runtime.nativeView.style.display;
        runtime.nativeView.style.display = 'none';
        runtime.nativeView.setAttribute('aria-hidden', 'true');
    }

    function restoreNativeView() {
        if (!runtime.nativeView) return;
        runtime.nativeView.style.display = runtime.nativeDisplay;
        runtime.nativeView.removeAttribute('aria-hidden');
    }

    function cleanupView() {
        runtime.requestSerial += 1;
        if (runtime.renderer && runtime.container && typeof runtime.renderer.unmount === 'function') {
            try {
                runtime.renderer.unmount(runtime.container);
            } catch (error) {
                console.warn('Better MusicDisplay renderer cleanup failed.', error);
            }
        }
        restoreNativeView();
        if (runtime.container) runtime.container.remove();
        runtime.container = null;
        runtime.nativeView = null;
        runtime.nativeDisplay = '';
        runtime.activeRouteKey = '';
    }

    function activate(parentId, context, serial) {
        if (serial !== runtime.requestSerial || !runtime.renderer) return;
        const page = activePage();
        const nativeView = nativeAlbumsView(page);
        if (!page || !nativeView) {
            scheduleSync(250);
            return;
        }

        const key = routeKey(parentId);
        if (runtime.container && runtime.activeRouteKey === key) return;
        cleanupView();
        const container = createContainer(page, nativeView, parentId);
        if (!container) {
            scheduleSync(250);
            return;
        }

        runtime.container = container;
        runtime.nativeView = nativeView;
        runtime.activeRouteKey = key;
        const helpers = {
            apiGet: apiGet,
            parentId: parentId,
            routeKey: key
        };

        Promise.resolve(runtime.renderer.mount(container, context, helpers))
            .then(function (mounted) {
                if (serial !== runtime.requestSerial || runtime.container !== container) return;
                if (mounted === false) {
                    cleanupView();
                    return;
                }
                container.hidden = false;
                hideNativeView();
            })
            .catch(function (error) {
                console.warn('Better MusicDisplay Albums view failed; native view restored.', error);
                cleanupView();
            });
    }

    function syncRoute() {
        const parentId = routeParentId();
        if (!parentId || !routeLooksLikeAlbums() || !runtime.renderer) {
            cleanupView();
            return;
        }

        const serial = ++runtime.requestSerial;
        apiGet('Plugins/BetterMusicDisplay/Albums/Context?parentId=' + encodeURIComponent(parentId))
            .then(function (context) {
                if (serial !== runtime.requestSerial) return;
                if (!context || !context.enabled || !context.isMusicLibrary) {
                    cleanupView();
                    return;
                }
                activate(parentId, context, serial);
            })
            .catch(function (error) {
                console.warn('Better MusicDisplay context validation failed; native view retained.', error);
                cleanupView();
            });
    }

    function scheduleSync(delay) {
        if (runtime.retryTimer !== null) window.clearTimeout(runtime.retryTimer);
        runtime.retryTimer = window.setTimeout(function () {
            runtime.retryTimer = null;
            syncRoute();
        }, delay);
    }

    function startObserver() {
        if (runtime.observer || !document.body) return;
        runtime.observer = new MutationObserver(function () { scheduleSync(75); });
        runtime.observer.observe(document.body, {
            childList: true,
            subtree: true,
            attributes: true,
            attributeFilter: ['class', 'aria-selected']
        });
    }

    const publicApi = {
        loaderVersion: 1,
        registerRenderer: function (renderer) {
            if (!renderer || typeof renderer.mount !== 'function') {
                throw new TypeError('A Better MusicDisplay renderer requires a mount function.');
            }
            runtime.renderer = renderer;
            scheduleSync(0);
        },
        unregisterRenderer: function (renderer) {
            if (runtime.renderer === renderer) {
                cleanupView();
                runtime.renderer = null;
            }
        },
        refresh: function () { scheduleSync(0); },
        apiGet: apiGet
    };
    window.BetterMusicDisplayAlbums = publicApi;

    window.addEventListener('hashchange', function () { scheduleSync(0); });
    window.addEventListener('popstate', function () { scheduleSync(0); });
    window.addEventListener('pageshow', function () {
        startObserver();
        scheduleSync(0);
    });
    window.addEventListener('pagehide', cleanupView);

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', function () {
            startObserver();
            scheduleSync(0);
        }, { once: true });
    } else {
        startObserver();
        scheduleSync(0);
    }
})();
