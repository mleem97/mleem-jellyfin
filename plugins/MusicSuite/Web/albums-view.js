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

    function contextValue(context, camel, pascal, fallback) {
        if (!context) return fallback;
        if (context[camel] !== undefined && context[camel] !== null) return context[camel];
        if (context[pascal] !== undefined && context[pascal] !== null) return context[pascal];
        return fallback;
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
        apiGet('Plugins/MusicSuite/Albums/Context?parentId=' + encodeURIComponent(parentId))
            .then(function (context) {
                if (serial !== runtime.requestSerial) return;
                const enabled = contextValue(context, 'enabled', 'Enabled', true);
                const isMusicLibrary = contextValue(context, 'isMusicLibrary', 'IsMusicLibrary', false);
                if (!context || !enabled || !isMusicLibrary) {
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

// MusicSuite Albums pure helpers (window.MusicSuiteAlbumsLib).
// Kept DOM-free and classic-script safe so node tests can eval this asset.
// The logic mirrors the backend contracts:
//   GET Plugins/MusicSuite/Albums (AlbumQueryRequest/AlbumQueryPage)
//   GET|PUT|DELETE Plugins/MusicSuite/Users/{userId}/Settings
//   GET Plugins/MusicSuite/Albums/Context (AlbumsViewContext)
(function () {
    'use strict';

    if (window.MusicSuiteAlbumsLib && window.MusicSuiteAlbumsLib.version === 1) return;

    const TILE_SIZES = ['Small', 'Medium', 'Large'];
    const SORT_KEYS = ['SortName', 'AlbumArtist', 'ProductionYear', 'DateCreated'];
    const SORT_ORDERS = ['Ascending', 'Descending'];
    const DEFAULT_BATCH_SIZE = 100;
    const MAX_BATCH_SIZE = 200;
    const DEFAULT_DEBOUNCE_MS = 300;
    const MIN_DEBOUNCE_MS = 100;
    const MAX_DEBOUNCE_MS = 2000;
    const DEFAULT_DOM_CAP = 500;

    function toTrimmedString(value) {
        return String(value === undefined || value === null ? '' : value).trim();
    }

    function clampBatchSize(value, fallback) {
        const fallbackValue = Number.isFinite(fallback) ? Math.floor(fallback) : DEFAULT_BATCH_SIZE;
        const parsed = Math.floor(Number(value));
        if (!Number.isFinite(parsed)) return Math.min(Math.max(fallbackValue, 1), MAX_BATCH_SIZE);
        return Math.min(Math.max(parsed, 1), MAX_BATCH_SIZE);
    }

    function clampDebounceMs(value, fallback) {
        const fallbackValue = Number.isFinite(fallback) ? Math.floor(fallback) : DEFAULT_DEBOUNCE_MS;
        const parsed = Math.floor(Number(value));
        if (!Number.isFinite(parsed)) return Math.min(Math.max(fallbackValue, MIN_DEBOUNCE_MS), MAX_DEBOUNCE_MS);
        return Math.min(Math.max(parsed, MIN_DEBOUNCE_MS), MAX_DEBOUNCE_MS);
    }

    function normalizeTileSize(value, fallback) {
        const fallbackValue = TILE_SIZES.indexOf(fallback) >= 0 ? fallback : 'Medium';
        const normalized = toTrimmedString(value);
        const match = TILE_SIZES.filter(function (size) {
            return size.toLowerCase() === normalized.toLowerCase();
        })[0];
        return match || fallbackValue;
    }

    function normalizeSortBy(value) {
        const normalized = toTrimmedString(value);
        const match = SORT_KEYS.filter(function (key) {
            return key.toLowerCase() === normalized.toLowerCase();
        })[0];
        return match || 'SortName';
    }

    function normalizeSortOrder(value) {
        return String(value || '').toLowerCase() === 'descending' ? 'Descending' : 'Ascending';
    }

    // Builds the query string for GET Plugins/MusicSuite/Albums.
    // Parameter names match AlbumQueryRequest property names.
    function buildAlbumsQuery(options) {
        const settings = options || {};
        const params = new URLSearchParams();
        params.set('StartIndex', String(Math.max(0, Math.floor(Number(settings.startIndex)) || 0)));
        params.set('Limit', String(clampBatchSize(settings.limit, DEFAULT_BATCH_SIZE)));
        if (toTrimmedString(settings.parentId)) params.set('ParentId', toTrimmedString(settings.parentId));
        const searchTerm = toTrimmedString(settings.searchTerm);
        if (searchTerm) params.set('SearchTerm', searchTerm);
        params.set('SortBy', normalizeSortBy(settings.sortBy));
        params.set('SortOrder', normalizeSortOrder(settings.sortOrder));
        if (settings.isFavorite === true) params.set('IsFavorite', 'true');
        if (settings.missingCover === true) params.set('MissingCover', 'true');
        const genre = toTrimmedString(settings.genre);
        if (genre) params.set('Genre', genre);
        const year = Math.floor(Number(settings.year));
        if (Number.isFinite(year) && year >= 1000 && year <= 9999) params.set('Year', String(year));
        params.set('Fields', 'Genres,DateCreated');
        return params.toString();
    }

    function createSequenceGuard() {
        let current = 0;
        return {
            issue: function () {
                current += 1;
                return current;
            },
            isCurrent: function (token) {
                return token === current;
            },
            current: function () {
                return current;
            }
        };
    }

    function itemId(item) {
        if (!item) return '';
        return toTrimmedString(item.Id !== undefined && item.Id !== null ? item.Id : item.id);
    }

    // Merges one page into the seen-map; returns only previously unseen items.
    // seen is a plain object used as a set (id -> true) so tests stay DOM-free.
    function mergeAlbumPage(seen, items) {
        const store = seen || {};
        const fresh = [];
        (items || []).forEach(function (item) {
            const id = itemId(item);
            if (!id || store[id]) return;
            store[id] = true;
            fresh.push(item);
        });
        return fresh;
    }

    // Caps in-memory/DOM growth: keeps the newest tail of the item array.
    function applyDomCap(items, cap) {
        const list = items || [];
        const parsed = Math.floor(Number(cap));
        const limit = Number.isFinite(parsed) && parsed > 0 ? parsed : DEFAULT_DOM_CAP;
        if (list.length <= limit) return list;
        return list.slice(list.length - limit);
    }

    function createFilterState(overrides) {
        const base = {
            searchTerm: '',
            sortBy: 'SortName',
            sortOrder: 'Ascending',
            isFavorite: false,
            missingCover: false,
            genre: '',
            year: ''
        };
        const extra = overrides || {};
        return {
            searchTerm: toTrimmedString(extra.searchTerm),
            sortBy: normalizeSortBy(extra.sortBy || base.sortBy),
            sortOrder: normalizeSortOrder(extra.sortOrder || base.sortOrder),
            isFavorite: extra.isFavorite === true,
            missingCover: extra.missingCover === true,
            genre: toTrimmedString(extra.genre),
            year: toTrimmedString(extra.year)
        };
    }

    // Paging-Reset bei jeder Such-/Sort-/Filteränderung; Filter bleiben erhalten.
    function resetPaging(state) {
        const previous = state || {};
        return {
            filters: previous.filters ? createFilterState(previous.filters) : createFilterState(),
            items: [],
            seen: {},
            nextStart: 0,
            hasMore: true,
            total: null
        };
    }

    function scrollKey(routeKey) {
        return 'musicsuite-albums-scroll|' + String(routeKey || '');
    }

    function saveScrollPosition(storage, routeKey, value) {
        try {
            if (!storage || typeof storage.setItem !== 'function') return false;
            storage.setItem(scrollKey(routeKey), String(Math.max(0, Math.floor(Number(value)) || 0)));
            return true;
        } catch (error) {
            return false;
        }
    }

    function readScrollPosition(storage, routeKey) {
        try {
            if (!storage || typeof storage.getItem !== 'function') return 0;
            const parsed = Math.floor(Number(storage.getItem(scrollKey(routeKey))));
            return Number.isFinite(parsed) && parsed > 0 ? parsed : 0;
        } catch (error) {
            return 0;
        }
    }

    function contextFlag(context, camel, pascal) {
        if (!context) return undefined;
        if (context[camel] !== undefined && context[camel] !== null) return context[camel];
        if (context[pascal] !== undefined && context[pascal] !== null) return context[pascal];
        return undefined;
    }

    // Fail-open: nur bei explizit aktiviertem Musik-Kontext einhängen.
    function shouldActivate(context) {
        if (!context) return false;
        const enabled = contextFlag(context, 'enabled', 'Enabled');
        const isMusicLibrary = contextFlag(context, 'isMusicLibrary', 'IsMusicLibrary');
        return enabled !== false && isMusicLibrary === true;
    }

    function escapeHtml(value) {
        return toTrimmedString(value === undefined || value === null ? '' : String(value))
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#39;');
    }

    function albumLabel(item) {
        const name = toTrimmedString(item && (item.Name || item.name)) || 'Unknown album';
        const artist = toTrimmedString(item && (item.AlbumArtist || item.albumArtist));
        return artist ? name + ' by ' + artist : name;
    }

    // Keyboard- und screenreader-erreichbares Card-Markup; Cover immer lazy.
    function albumCardHtml(item, coverUrl) {
        const safe = item || {};
        const id = escapeHtml(itemId(safe));
        const name = escapeHtml(toTrimmedString(safe.Name || safe.name) || 'Unknown album');
        const artist = escapeHtml(toTrimmedString(safe.AlbumArtist || safe.albumArtist) || 'Unknown artist');
        const yearValue = safe.ProductionYear !== undefined && safe.ProductionYear !== null
            ? safe.ProductionYear
            : safe.productionYear;
        const year = escapeHtml(yearValue !== undefined && yearValue !== null && String(yearValue).trim() !== ''
            ? String(yearValue)
            : '');
        const hasCover = safe.HasPrimaryImage === true || safe.hasPrimaryImage === true;
        const cover = hasCover && toTrimmedString(coverUrl)
            ? '<img src="' + escapeHtml(coverUrl) + '" alt="Cover: ' + escapeHtml(albumLabel(safe)) + '"' +
              ' loading="lazy" decoding="async" />'
            : '<span class="ms-album-cover-missing" aria-hidden="true">' + escapeHtml(name.charAt(0) || '?') + '</span>';
        return '<article class="ms-album-card" role="button" tabindex="0"' +
            ' data-album-id="' + id + '"' +
            ' aria-label="Album ' + escapeHtml(albumLabel(safe)) + '"' +
            (hasCover ? '' : ' data-missing-cover="true"') + '>' +
            '<span class="ms-album-cover">' + cover + '</span>' +
            '<span class="ms-album-meta">' +
            '<span class="ms-album-title">' + name + '</span>' +
            '<span class="ms-album-artist">' + artist + '</span>' +
            (year ? '<span class="ms-album-year">' + year + '</span>' : '') +
            '</span></article>';
    }

    function settingsUrl(userId) {
        return 'Plugins/MusicSuite/Users/' + encodeURIComponent(toTrimmedString(userId)) + '/Settings';
    }

    function albumsUrl(query) {
        return 'Plugins/MusicSuite/Albums' + (query ? '?' + query : '');
    }

    function resolveTileSize(settings, fallback) {
        const source = settings || {};
        return normalizeTileSize(source.TileSize !== undefined ? source.TileSize : source.tileSize, fallback);
    }

    window.MusicSuiteAlbumsLib = {
        version: 1,
        TILE_SIZES: TILE_SIZES,
        SORT_KEYS: SORT_KEYS,
        SORT_ORDERS: SORT_ORDERS,
        DEFAULT_BATCH_SIZE: DEFAULT_BATCH_SIZE,
        MAX_BATCH_SIZE: MAX_BATCH_SIZE,
        DEFAULT_DEBOUNCE_MS: DEFAULT_DEBOUNCE_MS,
        MIN_DEBOUNCE_MS: MIN_DEBOUNCE_MS,
        MAX_DEBOUNCE_MS: MAX_DEBOUNCE_MS,
        DEFAULT_DOM_CAP: DEFAULT_DOM_CAP,
        clampBatchSize: clampBatchSize,
        clampDebounceMs: clampDebounceMs,
        normalizeTileSize: normalizeTileSize,
        normalizeSortBy: normalizeSortBy,
        normalizeSortOrder: normalizeSortOrder,
        buildAlbumsQuery: buildAlbumsQuery,
        createSequenceGuard: createSequenceGuard,
        mergeAlbumPage: mergeAlbumPage,
        applyDomCap: applyDomCap,
        createFilterState: createFilterState,
        resetPaging: resetPaging,
        scrollKey: scrollKey,
        saveScrollPosition: saveScrollPosition,
        readScrollPosition: readScrollPosition,
        shouldActivate: shouldActivate,
        escapeHtml: escapeHtml,
        albumCardHtml: albumCardHtml,
        settingsUrl: settingsUrl,
        albumsUrl: albumsUrl,
        resolveTileSize: resolveTileSize
    };
})();

// MusicSuite Albums renderer: Endless-Scroll-Grid (#37), Live-Suche/Sort/Filter (#38),
// Per-User-Layouts (#39). Fail-open: jeder Mount-/Ladefehler gibt false zurück,
// sodass der Loader die native Jellyfin-Ansicht erhält.
(function () {
    'use strict';

    if (window.MusicSuiteAlbumsRenderer && window.MusicSuiteAlbumsRenderer.version === 1) return;
    if (!window.BetterMusicDisplayAlbums || !window.MusicSuiteAlbumsLib) return;

    const lib = window.MusicSuiteAlbumsLib;
    const GRID_STYLE_ID = 'musicsuite-albums-style';
    const activeMounts = new Map();

    const GRID_CSS = '.ms-albums-toolbar{display:flex;flex-wrap:wrap;gap:.5rem;align-items:center;margin-bottom:.75rem}' +
        '.ms-albums-toolbar input[type=search]{min-width:12rem;flex:1 1 12rem}' +
        '.ms-albums-toolbar label{display:inline-flex;align-items:center;gap:.35rem}' +
        '.ms-albums-grid{display:grid;gap:.75rem;grid-template-columns:repeat(auto-fill,minmax(var(--ms-tile,168px),1fr))}' +
        '.ms-albums-grid[data-tile=Small]{--ms-tile:120px}' +
        '.ms-albums-grid[data-tile=Medium]{--ms-tile:168px}' +
        '.ms-albums-grid[data-tile=Large]{--ms-tile:232px}' +
        '.ms-album-card{display:flex;flex-direction:column;gap:.35rem;cursor:pointer;border-radius:.5rem;padding:.35rem}' +
        '.ms-album-card:focus-visible{outline:2px solid currentColor;outline-offset:2px}' +
        '.ms-album-cover{display:block;aspect-ratio:1/1;overflow:hidden;border-radius:.4rem;background:rgba(127,127,127,.25)}' +
        '.ms-album-cover img{width:100%;height:100%;object-fit:cover;display:block}' +
        '.ms-album-cover-missing{display:flex;width:100%;height:100%;align-items:center;justify-content:center;font-size:2rem}' +
        '.ms-album-meta{display:flex;flex-direction:column;line-height:1.25}' +
        '.ms-album-title{font-weight:600}' +
        '.ms-albums-status{margin-top:.75rem;min-height:1.5rem}' +
        '.ms-albums-sentinel{width:100%;height:1px}';

    function ensureGridStyle(doc) {
        try {
            if (!doc || doc.getElementById(GRID_STYLE_ID)) return;
            const style = doc.createElement('style');
            style.id = GRID_STYLE_ID;
            style.textContent = GRID_CSS;
            (doc.head || doc.documentElement).appendChild(style);
        } catch (error) {
            // Styling ist optional; fail-open.
        }
    }

    function currentUserId() {
        try {
            if (window.ApiClient && typeof window.ApiClient.getCurrentUserId === 'function') {
                return window.ApiClient.getCurrentUserId();
            }
            if (window.Dashboard && typeof window.Dashboard.getCurrentUserId === 'function') {
                return window.Dashboard.getCurrentUserId();
            }
        } catch (error) {
            // Fail-open: ohne UserId keine Settings.
        }
        return null;
    }

    function coverUrl(apiClient, id) {
        const path = 'Items/' + encodeURIComponent(String(id)) + '/Images/Primary?fillWidth=400&fillHeight=400&quality=90';
        try {
            if (apiClient && typeof apiClient.getUrl === 'function') return apiClient.getUrl(path);
        } catch (error) {
            // Fail-open: relativer Pfad.
        }
        return path;
    }

    function readPage(payload) {
        if (!payload || typeof payload !== 'object') return null;
        const rawItems = payload.Items !== undefined && payload.Items !== null
            ? payload.Items
            : (payload.items || []);
        const rawNext = payload.NextStartIndex !== undefined && payload.NextStartIndex !== null
            ? payload.NextStartIndex
            : payload.nextStartIndex;
        const rawHasMore = payload.HasMore !== undefined && payload.HasMore !== null
            ? payload.HasMore
            : payload.hasMore;
        const rawStart = payload.StartIndex !== undefined && payload.StartIndex !== null
            ? payload.StartIndex
            : (payload.startIndex || 0);
        const items = Array.isArray(rawItems) ? rawItems : [];
        const start = Math.max(0, Math.floor(Number(rawStart)) || 0);
        const next = Number.isFinite(Number(rawNext)) ? Math.max(0, Math.floor(Number(rawNext))) : start + items.length;
        return { items: items, nextStart: next, hasMore: rawHasMore === true };
    }

    function fetchAlbums(apiGet, url, signal) {
        if (signal && signal.aborted) return Promise.reject(new Error('Aborted.'));
        // ApiClient-Pfad kennt kein Abort-Signal; Stale-Guard verwirft alte Antworten.
        return apiGet(url);
    }

    function sendJson(url, method, body) {
        try {
            const apiClient = window.ApiClient;
            const payload = body === undefined ? undefined : JSON.stringify(body);
            if (apiClient && typeof apiClient.ajax === 'function') {
                return apiClient.ajax({
                    type: method,
                    url: typeof apiClient.getUrl === 'function' ? apiClient.getUrl(url) : url,
                    data: payload,
                    contentType: 'application/json'
                }).catch(function () { return null; });
            }
        } catch (error) {
            return Promise.resolve(null);
        }
        try {
            return fetch(url, {
                method: method,
                credentials: 'same-origin',
                headers: { 'Content-Type': 'application/json' },
                body: payload
            }).then(function () { return null; }).catch(function () { return null; });
        } catch (error) {
            return Promise.resolve(null);
        }
    }

    function createToolbar(doc, state, callbacks) {
        const toolbar = doc.createElement('div');
        toolbar.className = 'ms-albums-toolbar';
        toolbar.setAttribute('role', 'search');
        toolbar.setAttribute('aria-label', 'Albums search and filter');

        toolbar.innerHTML =
            '<input type="search" class="ms-albums-search" aria-label="Search albums"' +
            ' placeholder="Search albums\u2026" autocomplete="off" />' +
            '<label>Sort <select class="ms-albums-sort" aria-label="Sort albums">' +
            '<option value="SortName">Title</option>' +
            '<option value="AlbumArtist">Album artist</option>' +
            '<option value="ProductionYear">Year</option>' +
            '<option value="DateCreated">Date added</option>' +
            '</select></label>' +
            '<label>Order <select class="ms-albums-order" aria-label="Sort order">' +
            '<option value="Ascending">Ascending</option>' +
            '<option value="Descending">Descending</option>' +
            '</select></label>' +
            '<label><input type="checkbox" class="ms-albums-fav" /> Favorites</label>' +
            '<label><input type="checkbox" class="ms-albums-missing" /> Missing cover</label>' +
            '<label>Genre <input class="ms-albums-genre" aria-label="Filter by genre"' +
            ' placeholder="Genre" autocomplete="off" /></label>' +
            '<label>Year <input type="number" class="ms-albums-year" aria-label="Filter by year"' +
            ' min="1000" max="9999" placeholder="Year" /></label>' +
            '<span class="ms-albums-tiles" role="group" aria-label="Tile size">' +
            '<button type="button" data-tile="Small" aria-pressed="false" title="Small tiles">S</button>' +
            '<button type="button" data-tile="Medium" aria-pressed="true" title="Medium tiles">M</button>' +
            '<button type="button" data-tile="Large" aria-pressed="false" title="Large tiles">L</button>' +
            '</span>' +
            '<button type="button" class="ms-albums-reset">Reset layout</button>';

        const search = toolbar.querySelector('.ms-albums-search');
        const sort = toolbar.querySelector('.ms-albums-sort');
        const order = toolbar.querySelector('.ms-albums-order');
        const fav = toolbar.querySelector('.ms-albums-fav');
        const missing = toolbar.querySelector('.ms-albums-missing');
        const genre = toolbar.querySelector('.ms-albums-genre');
        const year = toolbar.querySelector('.ms-albums-year');

        sort.value = state.filters.sortBy;
        order.value = state.filters.sortOrder;

        search.addEventListener('input', function () { callbacks.onSearch(search.value); });
        genre.addEventListener('input', function () { callbacks.onGenre(genre.value); });
        sort.addEventListener('change', function () { callbacks.onSort(sort.value, order.value); });
        order.addEventListener('change', function () { callbacks.onSort(sort.value, order.value); });
        fav.addEventListener('change', function () { callbacks.onToggle('isFavorite', fav.checked); });
        missing.addEventListener('change', function () { callbacks.onToggle('missingCover', missing.checked); });
        year.addEventListener('change', function () { callbacks.onYear(year.value); });
        toolbar.addEventListener('click', function (event) {
            const tileButton = event.target && event.target.closest
                ? event.target.closest('[data-tile]')
                : null;
            if (tileButton) {
                callbacks.onTileSize(tileButton.getAttribute('data-tile'));
                return;
            }
            if (event.target && event.target.closest
                && event.target.closest('.ms-albums-reset')) {
                callbacks.onResetLayout();
            }
        });
        return toolbar;
    }

    function applyTileSize(grid, toolbar, tileSize) {
        const normalized = lib.normalizeTileSize(tileSize, 'Medium');
        grid.setAttribute('data-tile', normalized);
        Array.from(toolbar.querySelectorAll('[data-tile]')).forEach(function (button) {
            const active = button.getAttribute('data-tile') === normalized;
            button.setAttribute('aria-pressed', active ? 'true' : 'false');
        });
        return normalized;
    }

    function lockCustomization(toolbar, locked) {
        Array.from(toolbar.querySelectorAll('[data-tile], .ms-albums-reset')).forEach(function (node) {
            if ('disabled' in node) node.disabled = locked === true;
            if (locked === true) {
                node.setAttribute('aria-disabled', 'true');
                node.setAttribute('title', 'Customization is disabled by the server administrator.');
            } else {
                node.removeAttribute('aria-disabled');
            }
        });
    }

    function mount(container, context, helpers) {
        const doc = container.ownerDocument || document;
        ensureGridStyle(doc);
        const apiGet = (helpers && helpers.apiGet) || window.BetterMusicDisplayAlbums.apiGet;
        const apiClient = window.ApiClient || null;
        const parentId = (helpers && helpers.parentId) || '';
        const routeKeyValue = (helpers && helpers.routeKey) || '';
        const batchSize = lib.clampBatchSize(
            context && (context.batchSize !== undefined && context.batchSize !== null
                ? context.batchSize
                : context.BatchSize),
            lib.DEFAULT_BATCH_SIZE);
        const debounceMs = lib.clampDebounceMs(
            context && (context.searchDebounceMs !== undefined && context.searchDebounceMs !== null
                ? context.searchDebounceMs
                : context.SearchDebounceMs),
            lib.DEFAULT_DEBOUNCE_MS);

        const state = {
            filters: lib.createFilterState(),
            items: [],
            seen: {},
            nextStart: 0,
            hasMore: true,
            loading: false,
            guard: lib.createSequenceGuard(),
            aborter: null,
            debounceTimer: null,
            tileSize: 'Medium',
            canCustomize: true,
            userId: null,
            destroyed: false
        };
        let grid = null;
        let status = null;
        let sentinel = null;
        let toolbar = null;
        let observer = null;
        let scrollTicking = false;

        function setStatus(mode, message) {
            if (!status || state.destroyed) return;
            if (mode === 'loading') {
                status.innerHTML = '<span>' + lib.escapeHtml(message || 'Loading albums\u2026') + '</span>';
            } else if (mode === 'error') {
                status.innerHTML = '<span>' + lib.escapeHtml(message || 'Loading failed.') + '</span> ' +
                    '<button type="button" class="ms-albums-retry">Retry</button>';
            } else if (mode === 'empty') {
                status.innerHTML = '<span>No albums found.</span>';
            } else {
                const total = state.total !== null && state.total !== undefined ? ' (' + state.total + ')' : '';
                status.innerHTML = state.items.length
                    ? '<span>' + state.items.length + ' albums' + lib.escapeHtml(total) + '</span>'
                    : '';
            }
        }

        function enforceDomCap() {
            if (!grid) return;
            let cards = grid.querySelectorAll('.ms-album-card');
            if (cards.length <= lib.DEFAULT_DOM_CAP) return;
            const overflow = cards.length - lib.DEFAULT_DOM_CAP;
            for (let i = 0; i < overflow; i += 1) {
                const first = grid.querySelector('.ms-album-card');
                if (!first) break;
                grid.removeChild(first);
            }
        }

        function appendItems(fresh) {
            if (!grid || !fresh.length) return;
            const html = fresh.map(function (item) {
                const id = item && (item.Id !== undefined && item.Id !== null ? item.Id : item.id);
                return lib.albumCardHtml(item, id ? coverUrl(apiClient, id) : '');
            }).join('');
            grid.insertAdjacentHTML('beforeend', html);
            enforceDomCap();
        }

        function loadPage(isFirst, isReset) {
            if (state.destroyed || !state.hasMore) return Promise.resolve(false);
            // Filter-Reset bricht einen laufenden Request ab und startet neu;
            // reines Nachladen (Sentinel/Retry) respektiert den In-Flight-Guard.
            if (state.loading && isReset !== true) return Promise.resolve(false);
            if (state.aborter && typeof state.aborter.abort === 'function') {
                try { state.aborter.abort(); } catch (error) { /* fail-open */ }
            }
            const aborter = typeof AbortController === 'function' ? new AbortController() : null;
            state.aborter = aborter;
            state.loading = true;
            if (state.items.length === 0) setStatus('loading');
            const token = state.guard.issue();
            const query = lib.buildAlbumsQuery({
                startIndex: state.nextStart,
                limit: batchSize,
                parentId: parentId,
                searchTerm: state.filters.searchTerm,
                sortBy: state.filters.sortBy,
                sortOrder: state.filters.sortOrder,
                isFavorite: state.filters.isFavorite,
                missingCover: state.filters.missingCover,
                genre: state.filters.genre,
                year: state.filters.year
            });
            return fetchAlbums(apiGet, lib.albumsUrl(query), aborter ? aborter.signal : null)
                .then(function (payload) {
                    if (state.destroyed || !state.guard.isCurrent(token)) return false;
                    const page = readPage(payload);
                    if (!page) throw new Error('Invalid album page.');
                    const fresh = lib.mergeAlbumPage(state.seen, page.items);
                    state.items = state.items.concat(fresh);
                    state.items = lib.applyDomCap(state.items, 10000);
                    state.nextStart = page.nextStart;
                    state.hasMore = page.hasMore;
                    const total = payload && (payload.TotalRecordCount !== undefined && payload.TotalRecordCount !== null
                        ? payload.TotalRecordCount
                        : payload.totalRecordCount);
                    if (Number.isFinite(Number(total))) state.total = Number(total);
                    else if (state.total === undefined) state.total = null;
                    appendItems(fresh);
                    state.loading = false;
                    if (!state.items.length) setStatus('empty');
                    else if (!state.hasMore) setStatus('ready');
                    else setStatus('ready');
                    restoreScrollOnce();
                    observeSentinel();
                    return true;
                })
                .catch(function (error) {
                    if (state.destroyed) return false;
                    if ((aborter && aborter.signal && aborter.signal.aborted)
                        || !state.guard.isCurrent(token)) {
                        state.loading = false;
                        return false;
                    }
                    state.loading = false;
                    if ((isFirst === true || state.items.length === 0) && state.nextStart === 0) {
                        // Fail-open: initiale Seite fehlt -> native Ansicht.
                        throw error;
                    }
                    setStatus('error', error && error.message ? String(error.message) : 'Loading failed.');
                    return false;
                });
        }

        function resetAndLoad() {
            const filters = state.filters;
            const reset = lib.resetPaging({ filters: filters });
            state.filters = reset.filters;
            state.items = reset.items;
            state.seen = reset.seen;
            state.nextStart = reset.nextStart;
            state.hasMore = reset.hasMore;
            state.total = null;
            if (grid) grid.innerHTML = '';
            setStatus('loading');
            return loadPage(true, true).catch(function (error) {
                // Fail-open auch bei Filterwechsel ohne Treffer-Kontext.
                setStatus('error', error && error.message ? String(error.message) : 'Loading failed.');
                return false;
            });
        }

        function scheduleSearch(value) {
            state.filters.searchTerm = String(value || '').trim();
            if (state.debounceTimer !== null) window.clearTimeout(state.debounceTimer);
            state.debounceTimer = window.setTimeout(function () {
                state.debounceTimer = null;
                resetAndLoad();
            }, debounceMs);
        }

        function scheduleGenre(value) {
            state.filters.genre = String(value || '').trim();
            if (state.debounceTimer !== null) window.clearTimeout(state.debounceTimer);
            state.debounceTimer = window.setTimeout(function () {
                state.debounceTimer = null;
                resetAndLoad();
            }, debounceMs);
        }

        let scrollRestored = false;
        function restoreScrollOnce() {
            if (scrollRestored || state.destroyed) return;
            scrollRestored = true;
            let saved = 0;
            try {
                saved = lib.readScrollPosition(window.sessionStorage, routeKeyValue);
            } catch (error) {
                saved = 0;
            }
            if (saved > 0) {
                try {
                    window.requestAnimationFrame(function () {
                        try { window.scrollTo(0, saved); } catch (error) { /* fail-open */ }
                    });
                } catch (error) {
                    try { window.scrollTo(0, saved); } catch (ignored) { /* fail-open */ }
                }
            }
        }

        function persistScroll() {
            try {
                lib.saveScrollPosition(window.sessionStorage, routeKeyValue, window.scrollY || 0);
            } catch (error) {
                // sessionStorage ist optional.
            }
        }

        function onWindowScroll() {
            if (state.destroyed) return;
            if (!scrollTicking) {
                scrollTicking = true;
                const after = function () {
                    scrollTicking = false;
                    if (state.destroyed) return;
                    persistScroll();
                    fallbackSentinelCheck();
                };
                try {
                    window.requestAnimationFrame(after);
                } catch (error) {
                    after();
                }
            }
        }

        function fallbackSentinelCheck() {
            if (observer || !sentinel || state.loading || !state.hasMore || state.destroyed) return;
            try {
                const rect = sentinel.getBoundingClientRect();
                const viewport = window.innerHeight || 800;
                if (rect.top <= viewport + 800) loadPage(false).catch(function () { /* inline retry */ });
            } catch (error) {
                // Fail-open: kein Nachladen ohne Geometrie.
            }
        }

        function observeSentinel() {
            if (!sentinel || state.destroyed || observer) return;
            try {
                if (typeof window.IntersectionObserver !== 'function') return;
                observer = new window.IntersectionObserver(function (entries) {
                    try {
                        if (entries.some(function (entry) { return entry.isIntersecting; })) {
                            loadPage(false).catch(function () { /* inline retry */ });
                        }
                    } catch (error) {
                        // Fail-open.
                    }
                }, { rootMargin: '800px 0px' });
                observer.observe(sentinel);
            } catch (error) {
                observer = null;
            }
        }

        function onGridClick(event) {
            try {
                const card = event.target && event.target.closest
                    ? event.target.closest('.ms-album-card')
                    : null;
                if (!card || !grid.contains(card)) return;
                openAlbum(card.getAttribute('data-album-id'));
            } catch (error) {
                // Fail-open.
            }
        }

        function onGridKeydown(event) {
            try {
                const card = event.target && event.target.closest
                    ? event.target.closest('.ms-album-card')
                    : null;
                if (!card || !grid.contains(card)) return;
                if (event.key === 'Enter' || event.key === ' ') {
                    event.preventDefault();
                    openAlbum(card.getAttribute('data-album-id'));
                }
            } catch (error) {
                // Fail-open.
            }
        }

        function openAlbum(id) {
            if (!id) return;
            try {
                window.location.hash = '#/details?id=' + encodeURIComponent(id);
            } catch (error) {
                // Fail-open.
            }
        }

        function onStatusClick(event) {
            try {
                if (event.target && event.target.closest
                    && event.target.closest('.ms-albums-retry')) {
                    setStatus('loading');
                    loadPage(state.items.length === 0).catch(function (error) {
                        setStatus('error', error && error.message ? String(error.message) : 'Loading failed.');
                    });
                }
            } catch (error) {
                // Fail-open.
            }
        }

        function persistTileSize(tileSize) {
            if (!state.userId || state.canCustomize !== true) return;
            sendJson(lib.settingsUrl(state.userId), 'PUT', { TileSize: tileSize });
        }

        function applySettingsResponse(response) {
            if (!response || typeof response !== 'object') return;
            const canCustomize = response.CanCustomize !== undefined && response.CanCustomize !== null
                ? response.CanCustomize
                : response.canCustomize;
            if (canCustomize === false || canCustomize === true) {
                state.canCustomize = canCustomize;
                lockCustomization(toolbar, canCustomize !== true);
            }
            const settings = response.Settings || response.settings;
            if (settings) {
                state.tileSize = applyTileSize(grid, toolbar, lib.resolveTileSize(settings, state.tileSize));
            }
        }

        function loadSettings() {
            state.userId = currentUserId();
            if (!state.userId) return;
            apiGet(lib.settingsUrl(state.userId)).then(function (response) {
                if (state.destroyed) return;
                applySettingsResponse(response);
            }).catch(function () {
                // Fail-open: Admin-Defaults bleiben aktiv.
            });
        }

        function onTileSize(tileSize) {
            if (state.canCustomize !== true || !grid || !toolbar) return;
            state.tileSize = applyTileSize(grid, toolbar, tileSize);
            persistTileSize(state.tileSize);
        }

        function onResetLayout() {
            if (state.canCustomize !== true || !state.userId) return;
            const url = lib.settingsUrl(state.userId);
            const apiClient = window.ApiClient;
            const done = function (response) {
                if (state.destroyed) return;
                if (response && (response.Settings || response.settings)) {
                    applySettingsResponse(response);
                } else {
                    state.tileSize = applyTileSize(grid, toolbar, 'Medium');
                }
            };
            try {
                if (apiClient && typeof apiClient.ajax === 'function') {
                    apiClient.ajax({
                        type: 'DELETE',
                        url: typeof apiClient.getUrl === 'function' ? apiClient.getUrl(url) : url
                    }).then(done).catch(function () {
                        if (!state.destroyed) state.tileSize = applyTileSize(grid, toolbar, 'Medium');
                    });
                    return;
                }
            } catch (error) {
                // Fail-open: lokaler Reset.
            }
            try {
                fetch(url, { method: 'DELETE', credentials: 'same-origin' })
                    .then(function () { return null; })
                    .then(done)
                    .catch(function () { done(null); });
            } catch (error) {
                done(null);
            }
        }

        const callbacks = {
            onSearch: scheduleSearch,
            onGenre: scheduleGenre,
            onSort: function (sortBy, sortOrder) {
                state.filters.sortBy = lib.normalizeSortBy(sortBy);
                state.filters.sortOrder = lib.normalizeSortOrder(sortOrder);
                resetAndLoad();
            },
            onToggle: function (key, checked) {
                state.filters[key] = checked === true;
                resetAndLoad();
            },
            onYear: function (value) {
                state.filters.year = String(value || '').trim();
                resetAndLoad();
            },
            onTileSize: onTileSize,
            onResetLayout: onResetLayout
        };

        function destroy() {
            state.destroyed = true;
            try { activeMounts.delete(container); } catch (error) { /* noop */ }
            if (state.debounceTimer !== null) {
                try { window.clearTimeout(state.debounceTimer); } catch (error) { /* noop */ }
                state.debounceTimer = null;
            }
            if (state.aborter && typeof state.aborter.abort === 'function') {
                try { state.aborter.abort(); } catch (error) { /* noop */ }
            }
            if (observer && typeof observer.disconnect === 'function') {
                try { observer.disconnect(); } catch (error) { /* noop */ }
            }
            observer = null;
            try { persistScroll(); } catch (error) { /* noop */ }
            try { window.removeEventListener('scroll', onWindowScroll); } catch (error) { /* noop */ }
            try {
                if (grid) {
                    grid.removeEventListener('click', onGridClick);
                    grid.removeEventListener('keydown', onGridKeydown);
                }
                if (status) status.removeEventListener('click', onStatusClick);
            } catch (error) { /* noop */ }
            try { container.innerHTML = ''; } catch (error) { /* noop */ }
            grid = null;
            status = null;
            sentinel = null;
            toolbar = null;
        }

        let initialPromise;
        try {
            if (!lib.shouldActivate(context)) return Promise.resolve(false);
            toolbar = createToolbar(doc, state, callbacks);
            grid = doc.createElement('div');
            grid.className = 'ms-albums-grid';
            grid.setAttribute('role', 'list');
            grid.setAttribute('aria-label', 'Albums');
            sentinel = doc.createElement('div');
            sentinel.className = 'ms-albums-sentinel';
            sentinel.setAttribute('aria-hidden', 'true');
            status = doc.createElement('div');
            status.className = 'ms-albums-status';
            status.setAttribute('role', 'status');
            status.setAttribute('aria-live', 'polite');

            container.appendChild(toolbar);
            container.appendChild(grid);
            container.appendChild(sentinel);
            container.appendChild(status);

            state.tileSize = applyTileSize(grid, toolbar, state.tileSize);
            grid.addEventListener('click', onGridClick);
            grid.addEventListener('keydown', onGridKeydown);
            status.addEventListener('click', onStatusClick);
            window.addEventListener('scroll', onWindowScroll, { passive: true });
            try { container.setAttribute('data-route-key', routeKeyValue); } catch (error) { /* noop */ }
            activeMounts.set(container, destroy);

            loadSettings();
            initialPromise = loadPage(true);
        } catch (error) {
            destroy();
            return Promise.resolve(false);
        }

        return initialPromise.then(function () {
            if (state.destroyed) return false;
            return true;
        }).catch(function () {
            destroy();
            // Fail-open: Loader stellt die native Ansicht wieder her.
            return false;
        });
    }

    function unmount(container) {
        try {
            const destroy = container && activeMounts.get(container);
            if (typeof destroy === 'function') destroy();
            else if (container) container.innerHTML = '';
        } catch (error) {
            // Fail-open.
        }
        try {
            if (container) {
                const routeKeyValue = container.getAttribute('data-route-key') || '';
                if (routeKeyValue) {
                    lib.saveScrollPosition(window.sessionStorage, routeKeyValue, window.scrollY || 0);
                }
            }
        } catch (error) {
            // sessionStorage ist optional.
        }
    }

    const renderer = {
        version: 1,
        mount: mount,
        unmount: unmount
    };
    window.MusicSuiteAlbumsRenderer = renderer;
    try {
        window.BetterMusicDisplayAlbums.registerRenderer(renderer);
    } catch (error) {
        console.warn('Better MusicDisplay renderer registration failed; native view retained.', error);
    }
})();
