(function () {
    'use strict';

    var TARGET = '/pages/spotify-music';

    function isMusicLink(node) {
        if (!node || !node.closest) {
            return false;
        }
        var hit = node.closest('[data-type="music"], a[href*="music.html"], button[data-type="music"]');
        return !!hit;
    }

    document.addEventListener('click', function (ev) {
        var target = ev.target;
        if (!isMusicLink(target)) {
            return;
        }
        ev.preventDefault();
        ev.stopPropagation();
        if (window.Emby && window.Emby.Page && typeof window.Emby.Page.show === 'function') {
            window.Emby.Page.show(TARGET);
        } else {
            window.location.hash = '#' + TARGET;
        }
    }, true);

    function redirectIfNativeMusicPage() {
        var hash = window.location.hash || '';
        if (hash.indexOf('music.html') !== -1 || hash.indexOf('musicPage') !== -1) {
            if (window.Emby && window.Emby.Page && typeof window.Emby.Page.show === 'function') {
                window.Emby.Page.show(TARGET);
            }
        }
        var nativePage = document.querySelector('div[data-role="page"].musicPage');
        if (nativePage && window.location.hash !== '#' + TARGET) {
            if (window.Emby && window.Emby.Page && typeof window.Emby.Page.show === 'function') {
                window.Emby.Page.show(TARGET);
            }
        }
    }

    document.addEventListener('viewshow', redirectIfNativeMusicPage, true);
    window.addEventListener('hashchange', redirectIfNativeMusicPage, false);
    document.addEventListener('DOMContentLoaded', redirectIfNativeMusicPage, false);
})();
