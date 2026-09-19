(function () {
    'use strict';

    var ASSET_BASE = '/MusicToolkit/asset/';
    var TARGET_URL = '/MusicToolkit/asset/dashboard';
    var state = { css: false, js: false, observer: null };

    function loadCss() {
        if (state.css) return;
        state.css = true;
        var link = document.createElement('link');
        link.rel = 'stylesheet';
        link.href = ASSET_BASE + 'spotify-theme.css';
        document.head.appendChild(link);
    }

    function loadDashboardJs() {
        if (state.js) return;
        state.js = true;
        var s = document.createElement('script');
        s.src = ASSET_BASE + 'spotify-dashboard.js';
        s.async = true;
        document.body.appendChild(s);
    }

    function ensureDashboardAssets() {
        loadCss();
        // Defer JS until the container exists so the dashboard boots immediately.
        if (document.getElementById('mtkRoot')) {
            loadDashboardJs();
        }
    }

    function observeDashboard() {
        if (state.observer || typeof MutationObserver === 'undefined') {
            ensureDashboardAssets();
            return;
        }
        state.observer = new MutationObserver(function () {
            ensureDashboardAssets();
        });
        state.observer.observe(document.body, { childList: true, subtree: true });
        ensureDashboardAssets();
    }

    function isMusicLink(node) {
        if (!node || !node.closest) {
            return false;
        }
        var hit = node.closest('[data-type="music"], a[href*="music.html"], button[data-type="music"]');
        return !!hit;
    }

    function goToDashboard() {
        // Prefer PluginPages fragment host when present (keeps jellyfin-web chrome + ApiClient).
        var hash = '#/userpluginsettings.html?pageUrl=' + encodeURIComponent(TARGET_URL);
        if (document.querySelector('.pluginMenuOptions') || document.querySelector('[data-plugin-pages="true"]')) {
            window.location.hash = hash;
            return;
        }
        window.location.hash = hash;
    }

    document.addEventListener('click', function (ev) {
        var target = ev.target;
        if (!isMusicLink(target)) {
            return;
        }
        ev.preventDefault();
        ev.stopPropagation();
        goToDashboard();
    }, true);

    function redirectIfNativeMusicPage() {
        var hash = window.location.hash || '';
        if (hash.indexOf('music.html') !== -1 || hash.indexOf('musicPage') !== -1) {
            goToDashboard();
            return;
        }
        var nativePage = document.querySelector('div[data-role="page"].musicPage');
        if (nativePage && hash.indexOf('userpluginsettings.html') === -1) {
            goToDashboard();
        }
    }

    function boot() {
        observeDashboard();
        redirectIfNativeMusicPage();
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', boot);
    } else {
        boot();
    }
    document.addEventListener('viewshow', redirectIfNativeMusicPage);
    window.addEventListener('hashchange', redirectIfNativeMusicPage);
})();
