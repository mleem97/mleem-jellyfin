/** Optional MusicSuite dashboard navigation. Native album routes always remain available. */
(function () {
    'use strict';
    if (window.MusicSuiteNavigation) return;
    const DASHBOARD_HASH = '#!/configurationpage?name=spotify-dashboard';
    const state = { enabled: false, force: false, ready: false, observer: null, serial: 0, returnRoute: '' };

    function assetUrl(name) {
        if (window.ApiClient && window.ApiClient.getUrl) return window.ApiClient.getUrl('Plugins/MusicSuite/Assets/' + name);
        const path = window.location.pathname;
        const index = path.toLowerCase().indexOf('/web');
        return (index >= 0 ? path.slice(0, index) : '') + '/Plugins/MusicSuite/Assets/' + name;
    }

    function isAlbumRoute() {
        const hash = String(window.location.hash || '').toLowerCase();
        if (hash.includes('album') || hash.includes('musicalbum')) return true;
        return Array.from(document.querySelectorAll('[aria-selected="true"], .emby-tab-button-active')).some(function (node) {
            return /^(albums|alben)$/i.test(String(node.textContent || '').trim());
        });
    }

    function ensureThemeCss() {
        if (document.querySelector('link[data-musicsuite-theme]')) return;
        const link = document.createElement('link');
        link.rel = 'stylesheet';
        link.dataset.musicsuiteTheme = 'true';
        link.href = assetUrl('spotify-theme.css');
        document.head.appendChild(link);
    }

    function navigate() {
        if (!state.enabled || !state.ready) return false;
        state.returnRoute = window.location.hash;
        ensureThemeCss();
        window.location.hash = DASHBOARD_HASH;
        return true;
    }

    function checkRoute() {
        if (!state.enabled || !state.force || !state.ready || isAlbumRoute()) return;
        const hash = String(window.location.hash || '');
        if (hash.includes('spotify-dashboard')) return;
        if (/music\.html|musicPage/.test(hash)) navigate();
    }

    function syncSidebar() {
        if (!window.document) return;
        const existing = document.querySelector('.musicsuite-nav-item');
        if (!state.enabled) {
            if (existing) existing.remove();
            return;
        }
        const drawer = document.querySelector('.mainDrawer-scrollContainer, .sidebarLinks');
        if (!drawer || existing) return;
        const link = document.createElement('a');
        link.className = 'listItem listItem-border emby-button musicsuite-nav-item';
        link.href = DASHBOARD_HASH;
        link.textContent = 'Musik · Spotify-Dashboard';
        link.addEventListener('click', function (event) {
            if (event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
            if (state.ready) {
                event.preventDefault();
                navigate();
            }
        });
        drawer.appendChild(link);
    }

    function refresh() {
        const serial = ++state.serial;
        const api = window.ApiClient;
        if (!api || !api.getJSON || !api.getUrl || !api.getCurrentUserId || !api.getCurrentUserId()) {
            state.enabled = false;
            state.force = false;
            syncSidebar();
            return Promise.resolve();
        }
        return Promise.resolve().then(function () {
            return api.getJSON(api.getUrl('Plugins/MusicSuite/Status'));
        }).then(function (status) {
            if (serial !== state.serial) return;
            state.enabled = status.enabled === true || status.Enabled === true;
            state.force = status.forceForAllUsers === true || status.ForceForAllUsers === true;
            syncSidebar();
            if (!state.enabled) {
                if (state.returnRoute && window.location.hash === DASHBOARD_HASH) window.location.hash = state.returnRoute;
                if (window.BetterMusicDisplayAlbums) window.BetterMusicDisplayAlbums.refresh();
                return;
            }
            if (state.ready) { checkRoute(); return; }
            // A missing dependency/asset keeps native navigation usable.
            return Promise.all(['spotify-dashboard.html', 'spotify-dashboard.js', 'spotify-theme.css'].map(function (name) {
                return window.fetch(assetUrl(name), { credentials: 'same-origin' }).then(function (response) {
                    if (!response.ok) throw new Error('Dashboard asset unavailable.');
                });
            })).then(function () {
                if (serial !== state.serial) return;
                state.ready = true;
                checkRoute();
            });
        }).catch(function () {
            if (serial !== state.serial) return;
            state.enabled = false;
            state.force = false;
            state.ready = false;
            syncSidebar();
        });
    }

    document.addEventListener('click', function (event) {
        if (!state.enabled || !state.force || !state.ready || event.button !== 0
            || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey || isAlbumRoute()) return;
        const target = event.target && event.target.closest ? event.target : null;
        if (!target || target.closest('#spotify-dashboard-root, #better-music-display-albums')) return;
        if (!target.closest('[data-type="music"], [data-type="Music"], [data-collectiontype="music"], a[href*="music.html"], .musicPageLink')) return;
        event.preventDefault();
        navigate();
    }, true);

    function boot() {
        refresh();
        if (!state.observer && document.body) {
            state.observer = new MutationObserver(syncSidebar);
            state.observer.observe(document.body, { childList: true, subtree: true });
        }
    }

    window.MusicSuiteNavigation = { refresh: refresh };
    document.addEventListener('viewshow', refresh);
    window.addEventListener('hashchange', refresh);
    window.addEventListener('pageshow', boot);
    window.addEventListener('pagehide', function () {
        state.serial += 1;
        if (state.observer) state.observer.disconnect();
        state.observer = null;
    });
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', boot, { once: true });
    else boot();
})();
