(function () {
    'use strict';

    function el(tag, cls, text) {
        var n = document.createElement(tag);
        if (cls) n.className = cls;
        if (text) n.textContent = text;
        return n;
    }

    function imageUrl(item, width) {
        try {
            if (item && item.ImageTags && item.ImageTags.Primary) {
                return window.ApiClient.getImageUrl(item.Id, { type: 'Primary', width: width || 300 });
            }
        } catch (e) { /* ignore */ }
        return '';
    }

    function playItem(item) {
        try {
            if (item && item.Type === 'MusicArtist') {
                window.ApiClient.getItems(window.ApiClient.getCurrentUserId(), {
                    Recursive: true,
                    IncludeItemTypes: 'Audio',
                    ArtistIds: item.Id,
                    SortBy: 'DatePlayed',
                    SortOrder: 'Descending',
                    Limit: 50
                }).then(function (res) {
                    var ids = (res.Items || []).map(function (x) { return x.Id; });
                    window.PlaybackManager.play({ ids: ids });
                });
                return;
            }
            if (item && item.Type === 'MusicAlbum') {
                window.PlaybackManager.play({ ids: [item.Id] });
                return;
            }
            window.PlaybackManager.play({ ids: [item.Id] });
        } catch (e) {
            if (window.Dashboard) window.Dashboard.alert('Wiedergabe fehlgeschlagen.');
        }
    }

    function card(item, artistMode) {
        var c = el('div', 'mtk-card' + (artistMode ? ' artist' : ''));
        var img = el('img');
        img.alt = item.Name || '';
        img.loading = 'lazy';
        var src = imageUrl(item, 300);
        if (src) img.src = src;
        var name = el('div', 'mtk-name', item.Name || '');
        var sub = el('div', 'mtk-sub', item.AlbumArtist || item.Artists && item.Artists.join(', ') || '');
        var btn = el('button', 'mtk-play', '▶');
        btn.type = 'button';
        btn.setAttribute('aria-label', 'Abspielen');
        btn.addEventListener('click', function (ev) { ev.stopPropagation(); playItem(item); });
        c.appendChild(img);
        c.appendChild(name);
        c.appendChild(sub);
        c.appendChild(btn);
        c.addEventListener('click', function () { playItem(item); });
        return c;
    }

    function row(item, artistMode) {
        var r = el('div', 'mtk-row' + (artistMode ? ' artist' : ''));
        var img = el('img');
        img.alt = '';
        img.loading = 'lazy';
        var src = imageUrl(item, 160);
        if (src) img.src = src;
        var name = el('div', 'mtk-name', item.Name || '');
        r.appendChild(img);
        r.appendChild(name);
        r.addEventListener('click', function () { playItem(item); });
        return r;
    }

    function quickTile(title, item) {
        var t = el('div', 'mtk-tile');
        var img = el('img');
        img.alt = '';
        var src = item ? imageUrl(item, 160) : '';
        if (src) img.src = src;
        var name = el('div', 'mtk-name', title);
        var btn = el('button', 'mtk-play', '▶');
        btn.type = 'button';
        btn.addEventListener('click', function (ev) { ev.stopPropagation(); if (item) playItem(item); });
        t.appendChild(img);
        t.appendChild(name);
        t.appendChild(btn);
        if (item) t.addEventListener('click', function () { playItem(item); });
        return t;
    }

    function renderList(container, items, artistMode, filter) {
        container.innerHTML = '';
        (items || []).filter(function (x) {
            if (!filter) return true;
            return (x.Name || '').toLowerCase().indexOf(filter.toLowerCase()) !== -1;
        }).slice(0, 60).forEach(function (x) { container.appendChild(row(x, artistMode)); });
    }

    function renderCards(container, items, artistMode) {
        container.innerHTML = '';
        (items || []).slice(0, 24).forEach(function (x) { container.appendChild(card(x, artistMode)); });
    }

    function renderQuick(items) {
        var q = document.getElementById('mtkQuick');
        q.innerHTML = '';
        var tiles = [
            { title: 'Liked Songs', item: items[0] },
            { title: 'Heavy Rotation', item: items[1] },
            { title: 'Zuletzt gehört', item: items[2] },
            { title: 'Entdecken', item: items[3] },
            { title: 'Neuheiten', item: items[4] },
            { title: 'Mix der Woche', item: items[5] }
        ];
        tiles.forEach(function (t) { q.appendChild(quickTile(t.title, t.item)); });
    }

    function renderHero(item) {
        var hero = document.getElementById('mtkHero');
        hero.innerHTML = '';
        if (!item) { hero.textContent = 'Keine Neuheiten gefunden.'; return; }
        var img = el('img');
        img.alt = item.Name || '';
        var src = imageUrl(item, 600);
        if (src) img.src = src;
        var name = el('div', 'mtk-name', item.Name || '');
        var sub = el('div', 'mtk-sub', item.AlbumArtist || '');
        var btn = el('button', 'mtk-play', '▶ Abspielen');
        btn.type = 'button';
        btn.style.position = 'static';
        btn.style.opacity = '1';
        btn.style.transform = 'none';
        btn.style.marginTop = '10px';
        btn.addEventListener('click', function () { playItem(item); });
        hero.appendChild(img);
        hero.appendChild(name);
        hero.appendChild(sub);
        hero.appendChild(btn);
    }

    function load() {
        if (!window.ApiClient) return;
        var userId = window.ApiClient.getCurrentUserId();
        var tabs = document.querySelectorAll('.mtk-tab');
        var list = document.getElementById('mtkLibraryList');
        var search = document.getElementById('mtkSearch');
        var cache = { playlists: [], artists: [], albums: [] };
        var active = 'playlists';

        function applyFilter() { renderList(list, cache[active], active === 'artists', search.value); }

        tabs.forEach(function (t) {
            t.addEventListener('click', function () {
                tabs.forEach(function (x) { x.classList.remove('active'); });
                t.classList.add('active');
                active = t.getAttribute('data-tab');
                applyFilter();
            });
        });
        search.addEventListener('input', applyFilter);

        window.ApiClient.getUserPlaylists(userId, { Limit: 50 }).then(function (res) {
            cache.playlists = res.Items || [];
            if (active === 'playlists') applyFilter();
        });
        window.ApiClient.getItems(userId, { Recursive: true, IncludeItemTypes: 'MusicArtist', SortBy: 'SortName', Limit: 60 }).then(function (res) {
            cache.artists = res.Items || [];
            renderCards(document.getElementById('mtkArtists'), cache.artists, true);
            if (active === 'artists') applyFilter();
        });
        window.ApiClient.getItems(userId, { Recursive: true, IncludeItemTypes: 'MusicAlbum', SortBy: 'DateCreated', SortOrder: 'Descending', Limit: 60 }).then(function (res) {
            cache.albums = res.Items || [];
            renderQuick(cache.albums);
            renderHero(cache.albums[0]);
            renderCards(document.getElementById('mtkForYou'), cache.albums, false);
            if (active === 'albums') applyFilter();
        });
        window.ApiClient.getItems(userId, { Recursive: true, IncludeItemTypes: 'MusicAlbum', SortBy: 'DatePlayed', SortOrder: 'Descending', Limit: 24, Filters: 'IsPlayed' }).then(function (res) {
            renderCards(document.getElementById('mtkResume'), res.Items || [], false);
        });
        window.ApiClient.getItems(userId, { Recursive: true, IncludeItemTypes: 'Playlist', SortBy: 'DateCreated', Limit: 24 }).then(function (res) {
            renderCards(document.getElementById('mtkMixes'), res.Items || [], false);
        });
    }

    document.addEventListener('viewshow', function (e) {
        var view = e && e.target;
        if (view && view.id === 'mtkRoot') load();
    });
    if (document.getElementById('mtkRoot')) load();
})();
