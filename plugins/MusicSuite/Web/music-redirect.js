/**
 * MusicSuite Spotify UI Interceptor & Router
 * Seamlessly overrides native Jellyfin music pages and redirects to the 3-column Spotify experience.
 */
(function () {
    'use strict';

    var ASSET_BASE = '/Plugins/MusicSuite/Assets/';
    var DASHBOARD_ROUTE = '/pages/spotify-music';
    var STANDALONE_URL = ASSET_BASE + 'spotify-dashboard.html';
    var state = { cssLoaded: false, observer: null };

    function ensureThemeCss() {
        if (state.cssLoaded) return;
        if (document.querySelector('link[href*="spotify-theme.css"]')) {
            state.cssLoaded = true;
            return;
        }
        state.cssLoaded = true;
        var link = document.createElement('link');
        link.rel = 'stylesheet';
        link.href = ASSET_BASE + 'spotify-theme.css';
        document.head.appendChild(link);
    }

    function navigateToSpotifyDashboard() {
        ensureThemeCss();

        // 1. If inside Jellyfin web router, try Emby.Page.show
        if (window.Emby && window.Emby.Page && typeof window.Emby.Page.show === 'function') {
            try {
                window.Emby.Page.show(DASHBOARD_ROUTE);
                return;
            } catch (e) {
                // Fallback to hash navigation
            }
        }

        // 2. Fragment router (Paradox PluginPages or Fallback)
        var targetHash = '#!' + DASHBOARD_ROUTE;
        if (window.location.hash !== targetHash) {
            window.location.hash = targetHash;
        }
    }

    function isMusicElement(node) {
        if (!node || !node.closest) return false;

        // Music tab, drawer item, home screen music card, or link to music.html
        var hit = node.closest([
            '[data-type="music"]',
            '[data-type="Music"]',
            '[data-collectiontype="music"]',
            'a[href*="music.html"]',
            'a[href*="item?id="][data-type="music"]',
            'button[data-type="music"]',
            '.musicPageLink'
        ].join(','));

        return !!hit;
    }

    // Intercept clicks globally
    document.addEventListener('click', function (ev) {
        var target = ev.target;
        if (!isMusicElement(target)) return;

        // Don't intercept if clicking inside the Spotify dashboard itself
        if (target.closest('#MusicSuiteRoot') || target.closest('#spotify-dashboard-root')) {
            return;
        }

        ev.preventDefault();
        ev.stopPropagation();
        navigateToSpotifyDashboard();
    }, true);

    function checkAndRedirectNativePage() {
        var hash = window.location.hash || '';

        // Check if currently on native music views
        if (hash.indexOf('music.html') !== -1 ||
            hash.indexOf('musicPage') !== -1 ||
            (hash.indexOf('serverId=') !== -1 && hash.indexOf('parentId=') !== -1 && document.querySelector('div[data-role="page"].musicPage'))) {

            // Prevent redirect loop if already on spotify-music
            if (hash.indexOf('spotify-music') === -1 && hash.indexOf('MusicSuite') === -1) {
                navigateToSpotifyDashboard();
            }
        }
    }

    function injectSidebarItem() {
        // Ensure "Musik (Spotify)" link exists in the main drawer/nav if not already present
        var navDrawer = document.querySelector('.mainDrawer-scrollContainer') || document.querySelector('.sidebarLinks');
        if (!navDrawer || navDrawer.querySelector('.musicsuite-nav-item')) return;

        var musicBtn = document.createElement('a');
        musicBtn.className = 'listItem listItem-border emby-button musicsuite-nav-item';
        musicBtn.href = '#!' + DASHBOARD_ROUTE;
        musicBtn.innerHTML = `
            <div class="listItemIcon" style="display:flex;align-items:center;color:#1db954;">
                <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M9 18V5l12-2v13"></path><circle cx="6" cy="18" r="3"></circle><circle cx="18" cy="16" r="3"></circle></svg>
            </div>
            <div class="listItemBody">
                <div class="listItemBodyText">Musik</div>
            </div>
        `;
        musicBtn.addEventListener('click', function (e) {
            e.preventDefault();
            navigateToSpotifyDashboard();
        });

        navDrawer.appendChild(musicBtn);
    }

    function boot() {
        ensureThemeCss();
        checkAndRedirectNativePage();
        injectSidebarItem();

        // Observer for dynamic UI updates and drawer rendering
        if (!state.observer && typeof MutationObserver !== 'undefined') {
            state.observer = new MutationObserver(function () {
                checkAndRedirectNativePage();
                injectSidebarItem();
            });
            state.observer.observe(document.body, { childList: true, subtree: true });
        }
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', boot);
    } else {
        boot();
    }

    document.addEventListener('viewshow', checkAndRedirectNativePage);
    window.addEventListener('hashchange', checkAndRedirectNativePage);
})();
