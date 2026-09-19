/**
 * MusicSuite: Spotify-Inspired 3-Column Streaming Experience for Jellyfin
 */
(function () {
    'use strict';

    // State management
    const state = {
        userId: null,
        libraryItems: {
            all: [],
            playlists: [],
            artists: [],
            albums: []
        },
        currentFilter: 'all',
        history: [],
        historyIndex: -1,
        activeDetailItem: null,
        searchDebounceTimer: null
    };

    // DOM Elements cache
    const dom = {};

    function initDom() {
        dom.navHome = document.getElementById('navHome');
        dom.navSearch = document.getElementById('navSearch');
        dom.navExplore = document.getElementById('navExplore');
        dom.btnNewPlaylist = document.getElementById('btnNewPlaylist');
        dom.pills = document.querySelectorAll('.ms-pill[data-filter]');
        dom.msLibraryList = document.getElementById('msLibraryList');
        
        dom.btnNavBack = document.getElementById('btnNavBack');
        dom.btnNavForward = document.getElementById('btnNavForward');
        dom.msSearchInput = document.getElementById('msSearchInput');

        dom.viewHome = document.getElementById('viewHome');
        dom.viewDetail = document.getElementById('viewDetail');
        dom.viewSearch = document.getElementById('viewSearch');

        dom.msGreeting = document.getElementById('msGreeting');
        dom.msQuickGrid = document.getElementById('msQuickGrid');
        dom.shelfRecentlyPlayed = document.getElementById('shelfRecentlyPlayed');
        dom.shelfAlbums = document.getElementById('shelfAlbums');
        dom.shelfArtists = document.getElementById('shelfArtists');
        dom.shelfPlaylists = document.getElementById('shelfPlaylists');

        dom.msDetailHeroCover = document.getElementById('msDetailHeroCover');
        dom.msDetailType = document.getElementById('msDetailType');
        dom.msDetailTitle = document.getElementById('msDetailTitle');
        dom.msDetailMeta = document.getElementById('msDetailMeta');
        dom.msBtnDetailPlay = document.getElementById('msBtnDetailPlay');
        dom.msBtnDetailShuffle = document.getElementById('msBtnDetailShuffle');
        dom.msTracklistBody = document.getElementById('msTracklistBody');

        dom.msSearchResults = document.getElementById('msSearchResults');

        dom.msNowCover = document.getElementById('msNowCover');
        dom.msNowTitle = document.getElementById('msNowTitle');
        dom.msNowArtist = document.getElementById('msNowArtist');
        dom.msQualityText = document.getElementById('msQualityText');
        dom.msBioCard = document.getElementById('msBioCard');
        dom.msNextUpItem = document.getElementById('msNextUpItem');

        dom.modalNewPlaylist = document.getElementById('modalNewPlaylist');
        dom.inputPlaylistName = document.getElementById('inputPlaylistName');
        dom.btnCancelPlaylist = document.getElementById('btnCancelPlaylist');
        dom.btnConfirmPlaylist = document.getElementById('btnConfirmPlaylist');
    }

    // Helper: Image URL builder
    function getImageUrl(item, options = {}) {
        if (!item) return '';
        const width = options.width || 300;
        const height = options.height || 300;
        
        if (window.ApiClient && ApiClient.getScaledImageUrl) {
            const hasImage = item.ImageTags && (item.ImageTags.Primary || item.ImageTags.Thumb);
            const imageItemId = (item.ImageTags && item.ImageTags.Primary) ? item.Id : (item.AlbumId || item.Id);
            if (hasImage || item.AlbumId) {
                return ApiClient.getScaledImageUrl(imageItemId, {
                    type: 'Primary',
                    maxWidth: width,
                    maxHeight: height,
                    tag: item.ImageTags ? item.ImageTags.Primary : null,
                    fillWidth: width,
                    fillHeight: height
                });
            }
        }

        // SVG fallback placeholder
        return "data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='" + width + "' height='" + height + "' viewBox='0 0 24 24' fill='%23222'%3E%3Crect width='24' height='24' rx='4'/%3E%3Cpath fill='%23555' d='M12 3v10.55c-.59-.34-1.27-.55-2-.55-2.21 0-4 1.79-4 4s1.79 4 4 4 4-1.79 4-4V7h4V3h-6z'/%3E%3C/svg%3E";
    }

    // Helper: Format runtime (ticks / ms to MM:SS or H:MM:SS)
    function formatTicks(ticks) {
        if (!ticks) return '0:00';
        const totalSeconds = Math.floor(ticks / 10000000);
        const hours = Math.floor(totalSeconds / 3600);
        const minutes = Math.floor((totalSeconds % 3600) / 60);
        const seconds = totalSeconds % 60;
        const paddedSec = seconds < 10 ? '0' + seconds : seconds;
        if (hours > 0) {
            const paddedMin = minutes < 10 ? '0' + minutes : minutes;
            return `${hours}:${paddedMin}:${paddedSec}`;
        }
        return `${minutes}:${paddedSec}`;
    }

    // Dynamic Greeting
    function updateGreeting() {
        const hour = new Date().getHours();
        let greeting = 'Guten Tag';
        if (hour >= 4 && hour < 12) {
            greeting = 'Guten Morgen';
        } else if (hour >= 12 && hour < 18) {
            greeting = 'Guten Tag';
        } else if (hour >= 18 && hour < 22) {
            greeting = 'Guten Abend';
        } else {
            greeting = 'Gute Nacht';
        }
        if (dom.msGreeting) dom.msGreeting.textContent = greeting;
    }

    // API: Load User Data & Content
    async function loadDashboardData() {
        if (!window.ApiClient) {
            console.warn('[MusicSuite] ApiClient unavailable.');
            return;
        }

        state.userId = ApiClient.getCurrentUserId();
        if (!state.userId) {
            console.warn('[MusicSuite] No active user logged in.');
            return;
        }

        try {
            // Parallel load of library sections
            const [recentItems, albums, artists, playlists] = await Promise.all([
                ApiClient.getItems(state.userId, {
                    IncludeItemTypes: 'Audio',
                    SortBy: 'DatePlayed',
                    SortOrder: 'Descending',
                    Filters: 'IsPlayed',
                    Limit: 12,
                    Recursive: true
                }).catch(() => ({ Items: [] })),
                ApiClient.getItems(state.userId, {
                    IncludeItemTypes: 'MusicAlbum',
                    SortBy: 'SortName',
                    SortOrder: 'Ascending',
                    Limit: 30,
                    Recursive: true
                }).catch(() => ({ Items: [] })),
                ApiClient.getItems(state.userId, {
                    IncludeItemTypes: 'MusicArtist',
                    SortBy: 'SortName',
                    SortOrder: 'Ascending',
                    Limit: 30,
                    Recursive: true
                }).catch(() => ({ Items: [] })),
                ApiClient.getItems(state.userId, {
                    IncludeItemTypes: 'Playlist',
                    SortBy: 'SortName',
                    SortOrder: 'Ascending',
                    Limit: 40,
                    Recursive: true
                }).catch(() => ({ Items: [] }))
            ]);

            state.libraryItems.playlists = playlists.Items || [];
            state.libraryItems.artists = artists.Items || [];
            state.libraryItems.albums = albums.Items || [];
            state.libraryItems.all = [
                ...state.libraryItems.playlists,
                ...state.libraryItems.artists,
                ...state.libraryItems.albums
            ];

            renderLibraryList();
            renderQuickGrid(recentItems.Items, albums.Items);
            renderRecentShelf(recentItems.Items);
            renderAlbumsShelf(albums.Items);
            renderArtistsShelf(artists.Items);
            renderPlaylistsShelf(playlists.Items);
        } catch (err) {
            console.error('[MusicSuite] Error loading dashboard data:', err);
        }
    }

    // Left Column: Library List Rendering
    function renderLibraryList() {
        if (!dom.msLibraryList) return;
        const filter = state.currentFilter;
        let items = [];

        if (filter === 'all') items = state.libraryItems.all;
        else if (filter === 'playlist') items = state.libraryItems.playlists;
        else if (filter === 'artist') items = state.libraryItems.artists;
        else if (filter === 'album') items = state.libraryItems.albums;

        if (items.length === 0) {
            dom.msLibraryList.innerHTML = '<div style="padding: 1rem; color: var(--ms-text-muted); font-size: 0.85rem; text-align: center;">Keine Einträge gefunden.</div>';
            return;
        }

        dom.msLibraryList.innerHTML = items.map(item => {
            const isArtist = item.Type === 'MusicArtist';
            const typeLabel = item.Type === 'Playlist' ? 'Playlist' : (isArtist ? 'Künstler' : 'Album');
            const sub = item.Artist || item.AlbumArtist || typeLabel;
            const thumbClass = isArtist ? 'ms-lib-thumb round' : 'ms-lib-thumb';
            const img = getImageUrl(item, { width: 92, height: 92 });

            return `
                <div class="ms-lib-entry" data-id="${item.Id}" data-type="${item.Type}">
                    <img class="${thumbClass}" src="${img}" alt="${escapeHtml(item.Name)}" loading="lazy">
                    <div class="ms-lib-meta">
                        <div class="ms-lib-name">${escapeHtml(item.Name)}</div>
                        <div class="ms-lib-desc">${escapeHtml(sub)}</div>
                    </div>
                </div>
            `;
        }).join('');

        // Attach click listeners to library entries
        dom.msLibraryList.querySelectorAll('.ms-lib-entry').forEach(el => {
            el.addEventListener('click', () => {
                const id = el.getAttribute('data-id');
                const type = el.getAttribute('data-type');
                openItemDetail(id, type);
            });
        });
    }

    // 6 Quick-Access Tiles
    function renderQuickGrid(recentTracks, albums) {
        if (!dom.msQuickGrid) return;
        
        // Pick top items from albums & recent
        let quickItems = [];
        if (albums && albums.length > 0) {
            quickItems = albums.slice(0, 6);
        } else if (recentTracks && recentTracks.length > 0) {
            quickItems = recentTracks.slice(0, 6);
        }

        if (quickItems.length === 0) {
            dom.msQuickGrid.style.display = 'none';
            return;
        }
        dom.msQuickGrid.style.display = 'grid';

        dom.msQuickGrid.innerHTML = quickItems.map(item => {
            const img = getImageUrl(item, { width: 128, height: 128 });
            return `
                <div class="ms-quick-tile" data-id="${item.Id}" data-type="${item.Type}">
                    <img class="ms-quick-thumb" src="${img}" alt="${escapeHtml(item.Name)}" loading="lazy">
                    <span class="ms-quick-title">${escapeHtml(item.Name)}</span>
                    <button class="ms-play-btn-float" data-play-id="${item.Id}" title="Wiedergeben">
                        <svg width="20" height="20" viewBox="0 0 24 24" fill="currentColor"><polygon points="5 3 19 12 5 21 5 3"/></svg>
                    </button>
                </div>
            `;
        }).join('');

        dom.msQuickGrid.querySelectorAll('.ms-quick-tile').forEach(tile => {
            tile.addEventListener('click', (e) => {
                if (e.target.closest('.ms-play-btn-float')) {
                    const playId = e.target.closest('.ms-play-btn-float').getAttribute('data-play-id');
                    playItem(playId);
                    return;
                }
                const id = tile.getAttribute('data-id');
                const type = tile.getAttribute('data-type');
                openItemDetail(id, type);
            });
        });
    }

    // Shelf: Zuletzt gehört
    function renderRecentShelf(items) {
        if (!dom.shelfRecentlyPlayed) return;
        const section = document.getElementById('sectionRecent');
        if (!items || items.length === 0) {
            if (section) section.style.display = 'none';
            return;
        }
        if (section) section.style.display = 'block';

        dom.shelfRecentlyPlayed.innerHTML = items.slice(0, 8).map(item => {
            const img = getImageUrl(item, { width: 340, height: 340 });
            return `
                <div class="ms-card" data-id="${item.AlbumId || item.Id}" data-type="${item.AlbumId ? 'MusicAlbum' : item.Type}">
                    <div class="ms-card-cover-wrap">
                        <img class="ms-card-cover" src="${img}" alt="${escapeHtml(item.Name)}" loading="lazy">
                        <button class="ms-play-btn-float" data-play-id="${item.Id}" title="Wiedergeben">
                            <svg width="20" height="20" viewBox="0 0 24 24" fill="currentColor"><polygon points="5 3 19 12 5 21 5 3"/></svg>
                        </button>
                    </div>
                    <div class="ms-card-title">${escapeHtml(item.Name)}</div>
                    <div class="ms-card-sub">${escapeHtml(item.ArtistItems && item.ArtistItems[0] ? item.ArtistItems[0].Name : (item.AlbumArtist || 'Song'))}</div>
                </div>
            `;
        }).join('');

        attachShelfListeners(dom.shelfRecentlyPlayed);
    }

    // Shelf: Alben
    function renderAlbumsShelf(items) {
        if (!dom.shelfAlbums) return;
        const section = document.getElementById('sectionAlbums');
        if (!items || items.length === 0) {
            if (section) section.style.display = 'none';
            return;
        }
        if (section) section.style.display = 'block';

        dom.shelfAlbums.innerHTML = items.slice(0, 8).map(item => {
            const img = getImageUrl(item, { width: 340, height: 340 });
            return `
                <div class="ms-card" data-id="${item.Id}" data-type="MusicAlbum">
                    <div class="ms-card-cover-wrap">
                        <img class="ms-card-cover" src="${img}" alt="${escapeHtml(item.Name)}" loading="lazy">
                        <button class="ms-play-btn-float" data-play-id="${item.Id}" title="Wiedergeben">
                            <svg width="20" height="20" viewBox="0 0 24 24" fill="currentColor"><polygon points="5 3 19 12 5 21 5 3"/></svg>
                        </button>
                    </div>
                    <div class="ms-card-title">${escapeHtml(item.Name)}</div>
                    <div class="ms-card-sub">${escapeHtml(item.AlbumArtist || (item.ProductionYear ? item.ProductionYear.toString() : 'Album'))}</div>
                </div>
            `;
        }).join('');

        attachShelfListeners(dom.shelfAlbums);
    }

    // Shelf: Künstler
    function renderArtistsShelf(items) {
        if (!dom.shelfArtists) return;
        const section = document.getElementById('sectionArtists');
        if (!items || items.length === 0) {
            if (section) section.style.display = 'none';
            return;
        }
        if (section) section.style.display = 'block';

        dom.shelfArtists.innerHTML = items.slice(0, 8).map(item => {
            const img = getImageUrl(item, { width: 340, height: 340 });
            return `
                <div class="ms-card" data-id="${item.Id}" data-type="MusicArtist">
                    <div class="ms-card-cover-wrap">
                        <img class="ms-card-cover round" src="${img}" alt="${escapeHtml(item.Name)}" loading="lazy">
                        <button class="ms-play-btn-float" data-play-id="${item.Id}" title="Wiedergeben">
                            <svg width="20" height="20" viewBox="0 0 24 24" fill="currentColor"><polygon points="5 3 19 12 5 21 5 3"/></svg>
                        </button>
                    </div>
                    <div class="ms-card-title">${escapeHtml(item.Name)}</div>
                    <div class="ms-card-sub">Künstler</div>
                </div>
            `;
        }).join('');

        attachShelfListeners(dom.shelfArtists);
    }

    // Shelf: Playlists
    function renderPlaylistsShelf(items) {
        if (!dom.shelfPlaylists) return;
        const section = document.getElementById('sectionPlaylists');
        if (!items || items.length === 0) {
            if (section) section.style.display = 'none';
            return;
        }
        if (section) section.style.display = 'block';

        dom.shelfPlaylists.innerHTML = items.slice(0, 8).map(item => {
            const img = getImageUrl(item, { width: 340, height: 340 });
            return `
                <div class="ms-card" data-id="${item.Id}" data-type="Playlist">
                    <div class="ms-card-cover-wrap">
                        <img class="ms-card-cover" src="${img}" alt="${escapeHtml(item.Name)}" loading="lazy">
                        <button class="ms-play-btn-float" data-play-id="${item.Id}" title="Wiedergeben">
                            <svg width="20" height="20" viewBox="0 0 24 24" fill="currentColor"><polygon points="5 3 19 12 5 21 5 3"/></svg>
                        </button>
                    </div>
                    <div class="ms-card-title">${escapeHtml(item.Name)}</div>
                    <div class="ms-card-sub">Playlist</div>
                </div>
            `;
        }).join('');

        attachShelfListeners(dom.shelfPlaylists);
    }

    function attachShelfListeners(container) {
        container.querySelectorAll('.ms-card').forEach(card => {
            card.addEventListener('click', (e) => {
                if (e.target.closest('.ms-play-btn-float')) {
                    const playId = e.target.closest('.ms-play-btn-float').getAttribute('data-play-id');
                    playItem(playId);
                    return;
                }
                const id = card.getAttribute('data-id');
                const type = card.getAttribute('data-type');
                openItemDetail(id, type);
            });
        });
    }

    // DETAIL VIEW: Load & Display Album / Artist / Playlist
    async function openItemDetail(id, type) {
        if (!id || !window.ApiClient) return;

        try {
            pushHistory({ view: 'detail', id, type });

            dom.viewHome.style.display = 'none';
            dom.viewSearch.style.display = 'none';
            dom.viewDetail.style.display = 'block';

            // Scroll main view to top
            const centerCol = document.getElementById('msCenterColumn');
            if (centerCol) centerCol.scrollTop = 0;

            // Fetch main item info
            const item = await ApiClient.getItem(state.userId, id);
            state.activeDetailItem = item;

            // Set hero header
            const isArtist = item.Type === 'MusicArtist';
            dom.msDetailType.textContent = item.Type === 'Playlist' ? 'PLAYLIST' : (isArtist ? 'KÜNSTLER' : 'ALBUM');
            dom.msDetailTitle.textContent = item.Name;
            dom.msDetailHeroCover.src = getImageUrl(item, { width: 380, height: 380 });
            if (isArtist) dom.msDetailHeroCover.classList.add('round');
            else dom.msDetailHeroCover.classList.remove('round');

            // Fetch tracks
            let tracksQuery = {
                ParentId: id,
                SortBy: 'SortName',
                SortOrder: 'Ascending'
            };

            if (item.Type === 'MusicAlbum') {
                tracksQuery = {
                    ParentId: id,
                    IncludeItemTypes: 'Audio',
                    SortBy: 'IndexNumber,SortName',
                    SortOrder: 'Ascending'
                };
            } else if (item.Type === 'MusicArtist') {
                tracksQuery = {
                    ArtistIds: id,
                    IncludeItemTypes: 'Audio',
                    SortBy: 'PlayCount,SortName',
                    SortOrder: 'Descending',
                    Limit: 50,
                    Recursive: true
                };
            } else if (item.Type === 'Playlist') {
                tracksQuery = {
                    ParentId: id,
                    SortBy: 'SortName',
                    SortOrder: 'Ascending'
                };
            }

            const tracksResult = await ApiClient.getItems(state.userId, tracksQuery);
            const tracks = tracksResult.Items || [];

            // Compute meta line
            let metaString = '';
            if (item.Type === 'MusicAlbum') {
                metaString = `${escapeHtml(item.AlbumArtist || 'Verschiedene')} • ${item.ProductionYear || ''} • ${tracks.length} Titel`;
            } else if (item.Type === 'MusicArtist') {
                metaString = `${tracks.length} Top-Titel verfügbar`;
            } else {
                metaString = `${tracks.length} Titel in Playlist`;
            }
            dom.msDetailMeta.innerHTML = metaString;

            // Render tracklist
            if (tracks.length === 0) {
                dom.msTracklistBody.innerHTML = '<tr><td colspan="4" style="text-align:center; padding: 2rem; color: var(--ms-text-muted);">Keine Titel vorhanden.</td></tr>';
            } else {
                dom.msTracklistBody.innerHTML = tracks.map((track, idx) => {
                    const duration = formatTicks(track.RunTimeTicks);
                    const artist = track.ArtistItems && track.ArtistItems[0] ? track.ArtistItems[0].Name : (track.AlbumArtist || '');
                    return `
                        <tr class="ms-track-row" data-id="${track.Id}" data-index="${idx}">
                            <td class="ms-track-idx" style="text-align: center;">${idx + 1}</td>
                            <td>
                                <div style="font-weight: 600; color: var(--ms-text);">${escapeHtml(track.Name)}</div>
                                <div style="font-size: 0.78rem; color: var(--ms-text-muted);">${escapeHtml(artist)}</div>
                            </td>
                            <td style="color: var(--ms-text-sub);">${escapeHtml(track.Album || '')}</td>
                            <td style="text-align: right; color: var(--ms-text-muted);">${duration}</td>
                        </tr>
                    `;
                }).join('');

                // Track row click plays track in sequence
                dom.msTracklistBody.querySelectorAll('.ms-track-row').forEach(row => {
                    row.addEventListener('click', () => {
                        const trackIdx = parseInt(row.getAttribute('data-index'), 10);
                        playTrackList(tracks, trackIdx);
                    });
                });
            }

            // Detail hero play / shuffle actions
            dom.msBtnDetailPlay.onclick = () => {
                if (tracks.length > 0) playTrackList(tracks, 0);
                else playItem(id);
            };

            dom.msBtnDetailShuffle.onclick = () => {
                shuffleItem(id, tracks);
            };

        } catch (err) {
            console.error('[MusicSuite] Error opening detail:', err);
        }
    }

    // Playback Helpers
    function playItem(itemId) {
        if (!itemId) return;
        if (window.PlaybackManager) {
            PlaybackManager.play({
                ids: [itemId]
            });
        } else if (window.ApiClient && ApiClient.play) {
            ApiClient.play(itemId);
        }
    }

    function playTrackList(tracks, startIndex) {
        if (!tracks || tracks.length === 0) return;
        if (window.PlaybackManager) {
            PlaybackManager.play({
                items: tracks,
                startIndex: startIndex || 0
            });
        }
    }

    function shuffleItem(containerId, tracks) {
        if (window.PlaybackManager) {
            if (PlaybackManager.shuffle) {
                PlaybackManager.shuffle(containerId);
            } else if (tracks && tracks.length > 0) {
                const shuffled = [...tracks].sort(() => Math.random() - 0.5);
                PlaybackManager.play({
                    items: shuffled,
                    startIndex: 0
                });
            }
        }
    }

    // Real-Time Now Playing & Hi-Res Stream Tracking (Column 3)
    function attachPlaybackEvents() {
        if (!window.Events) return;

        Events.on(window.PlaybackManager || window, 'playbackstart', onPlaybackUpdate);
        Events.on(window.PlaybackManager || window, 'playbackprogress', onPlaybackUpdate);
        Events.on(window.PlaybackManager || window, 'playbackstop', onPlaybackStopped);

        // Also initial check
        if (window.PlaybackManager && PlaybackManager.currentItem) {
            const cur = PlaybackManager.currentItem();
            if (cur) onPlaybackUpdate(null, cur);
        }
    }

    async function onPlaybackUpdate(e, currentItem) {
        const item = currentItem || (window.PlaybackManager && PlaybackManager.currentItem ? PlaybackManager.currentItem() : null);
        if (!item) return;

        if (dom.msNowTitle) dom.msNowTitle.textContent = item.Name || 'Unbekannter Titel';
        const artist = item.ArtistItems && item.ArtistItems[0] ? item.ArtistItems[0].Name : (item.AlbumArtist || 'Unbekannter Künstler');
        if (dom.msNowArtist) {
            dom.msNowArtist.textContent = artist;
            dom.msNowArtist.onclick = () => {
                const artistId = (item.ArtistItems && item.ArtistItems[0]) ? item.ArtistItems[0].Id : item.AlbumArtistId;
                if (artistId) openItemDetail(artistId, 'MusicArtist');
            };
        }

        if (dom.msNowCover) {
            dom.msNowCover.src = getImageUrl(item, { width: 400, height: 400 });
        }

        // Stream Quality Pill (Hi-Res FLAC / Bitrate Detection)
        if (dom.msQualityText) {
            let quality = 'Stereo';
            if (item.MediaSources && item.MediaSources.length > 0) {
                const source = item.MediaSources[0];
                const stream = (source.MediaStreams || []).find(s => s.Type === 'Audio');
                if (stream) {
                    const codec = (stream.Codec || source.Container || 'Audio').toUpperCase();
                    const sampleRate = stream.SampleRate ? `${Math.round(stream.SampleRate / 1000)}kHz` : '';
                    const bitDepth = stream.BitDepth ? `${stream.BitDepth}bit` : '';
                    const bitRate = source.Bitrate ? `${Math.round(source.Bitrate / 1000)}kbps` : '';

                    if (codec === 'FLAC') {
                        quality = `FLAC ${sampleRate}${bitDepth ? ' / ' + bitDepth : ''}`;
                    } else if (bitRate) {
                        quality = `${codec} ${bitRate}`;
                    } else {
                        quality = `${codec} ${sampleRate}`;
                    }
                }
            }
            dom.msQualityText.textContent = quality;
        }

        // Fetch Artist Biography
        const artistId = (item.ArtistItems && item.ArtistItems[0]) ? item.ArtistItems[0].Id : null;
        if (artistId && window.ApiClient && dom.msBioCard) {
            try {
                const artistData = await ApiClient.getItem(state.userId, artistId);
                if (artistData && artistData.Overview) {
                    dom.msBioCard.textContent = artistData.Overview;
                } else if (artistData && artistData.Genres && artistData.Genres.length > 0) {
                    dom.msBioCard.textContent = `Genre: ${artistData.Genres.join(', ')}`;
                } else {
                    dom.msBioCard.textContent = `${artist} – Weitere Informationen werden geladen sobald verfügbar.`;
                }
            } catch (ignore) {}
        }
    }

    function onPlaybackStopped() {
        if (dom.msNowTitle) dom.msNowTitle.textContent = 'Keine Wiedergabe';
        if (dom.msNowArtist) dom.msNowArtist.textContent = 'Wähle einen Song aus der Mediathek';
        if (dom.msQualityText) dom.msQualityText.textContent = 'Stereo';
    }

    // Search Handling
    function setupSearch() {
        if (!dom.msSearchInput) return;

        dom.msSearchInput.addEventListener('input', (e) => {
            const query = e.target.value.trim();
            clearTimeout(state.searchDebounceTimer);
            if (!query) {
                showHome();
                return;
            }

            state.searchDebounceTimer = setTimeout(() => {
                performSearch(query);
            }, 300);
        });
    }

    async function performSearch(query) {
        if (!window.ApiClient || !state.userId) return;

        try {
            pushHistory({ view: 'search', query });

            dom.viewHome.style.display = 'none';
            dom.viewDetail.style.display = 'none';
            dom.viewSearch.style.display = 'block';

            const result = await ApiClient.getItems(state.userId, {
                SearchTerm: query,
                IncludeItemTypes: 'Audio,MusicAlbum,MusicArtist,Playlist',
                Limit: 24,
                Recursive: true
            });

            const items = result.Items || [];
            if (items.length === 0) {
                dom.msSearchResults.innerHTML = '<div style="color: var(--ms-text-muted); font-size: 0.95rem; grid-column: 1/-1;">Keine Treffer für "' + escapeHtml(query) + '" gefunden.</div>';
                return;
            }

            dom.msSearchResults.innerHTML = items.map(item => {
                const isArtist = item.Type === 'MusicArtist';
                const img = getImageUrl(item, { width: 340, height: 340 });
                const typeLabel = item.Type === 'Playlist' ? 'Playlist' : (isArtist ? 'Künstler' : (item.Type === 'Audio' ? 'Titel' : 'Album'));

                return `
                    <div class="ms-card" data-id="${item.Id}" data-type="${item.Type}">
                        <div class="ms-card-cover-wrap">
                            <img class="ms-card-cover ${isArtist ? 'round' : ''}" src="${img}" alt="${escapeHtml(item.Name)}" loading="lazy">
                            <button class="ms-play-btn-float" data-play-id="${item.Id}" title="Wiedergeben">
                                <svg width="20" height="20" viewBox="0 0 24 24" fill="currentColor"><polygon points="5 3 19 12 5 21 5 3"/></svg>
                            </button>
                        </div>
                        <div class="ms-card-title">${escapeHtml(item.Name)}</div>
                        <div class="ms-card-sub">${typeLabel} • ${escapeHtml(item.AlbumArtist || '')}</div>
                    </div>
                `;
            }).join('');

            attachShelfListeners(dom.msSearchResults);

        } catch (err) {
            console.error('[MusicSuite] Search error:', err);
        }
    }

    // Views & History Navigation
    function showHome() {
        pushHistory({ view: 'home' });
        dom.viewHome.style.display = 'block';
        dom.viewDetail.style.display = 'none';
        dom.viewSearch.style.display = 'none';
        if (dom.msSearchInput) dom.msSearchInput.value = '';
    }

    function pushHistory(entry) {
        state.history = state.history.slice(0, state.historyIndex + 1);
        state.history.push(entry);
        state.historyIndex++;
        updateNavButtons();
    }

    function updateNavButtons() {
        if (dom.btnNavBack) dom.btnNavBack.disabled = state.historyIndex <= 0;
        if (dom.btnNavForward) dom.btnNavForward.disabled = state.historyIndex >= state.history.length - 1;
    }

    function navBack() {
        if (state.historyIndex > 0) {
            state.historyIndex--;
            restoreHistoryEntry(state.history[state.historyIndex]);
            updateNavButtons();
        }
    }

    function navForward() {
        if (state.historyIndex < state.history.length - 1) {
            state.historyIndex++;
            restoreHistoryEntry(state.history[state.historyIndex]);
            updateNavButtons();
        }
    }

    function restoreHistoryEntry(entry) {
        if (!entry) return;
        if (entry.view === 'home') {
            dom.viewHome.style.display = 'block';
            dom.viewDetail.style.display = 'none';
            dom.viewSearch.style.display = 'none';
        } else if (entry.view === 'detail') {
            openItemDetail(entry.id, entry.type);
        } else if (entry.view === 'search') {
            performSearch(entry.query);
        }
    }

    // Playlist Creation Modal
    function setupPlaylistModal() {
        if (!dom.btnNewPlaylist || !dom.modalNewPlaylist) return;

        dom.btnNewPlaylist.addEventListener('click', () => {
            dom.modalNewPlaylist.style.display = 'flex';
            if (dom.inputPlaylistName) {
                dom.inputPlaylistName.value = '';
                dom.inputPlaylistName.focus();
            }
        });

        dom.btnCancelPlaylist.addEventListener('click', () => {
            dom.modalNewPlaylist.style.display = 'none';
        });

        dom.btnConfirmPlaylist.addEventListener('click', async () => {
            const name = dom.inputPlaylistName ? dom.inputPlaylistName.value.trim() : '';
            if (!name) return;

            if (window.ApiClient && ApiClient.createPlaylist) {
                try {
                    await ApiClient.createPlaylist({
                        name: name,
                        ids: []
                    });
                    dom.modalNewPlaylist.style.display = 'none';
                    // Reload playlists
                    loadDashboardData();
                } catch (err) {
                    alert('Fehler beim Erstellen der Playlist: ' + err.message);
                }
            }
        });
    }

    // Utility: HTML Escaping
    function escapeHtml(str) {
        if (!str) return '';
        return String(str)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#039;');
    }

    // Initialization
    function init() {
        initDom();
        updateGreeting();
        
        // Navigation clicks
        if (dom.navHome) dom.navHome.addEventListener('click', (e) => { e.preventDefault(); showHome(); });
        if (dom.navSearch) dom.navSearch.addEventListener('click', (e) => { e.preventDefault(); dom.msSearchInput.focus(); });
        if (dom.navExplore) dom.navExplore.addEventListener('click', (e) => { e.preventDefault(); showHome(); });

        if (dom.btnNavBack) dom.btnNavBack.addEventListener('click', navBack);
        if (dom.btnNavForward) dom.btnNavForward.addEventListener('click', navForward);

        // Filter pills in library column
        dom.pills.forEach(pill => {
            pill.addEventListener('click', () => {
                dom.pills.forEach(p => p.classList.remove('active'));
                pill.classList.add('active');
                state.currentFilter = pill.getAttribute('data-filter');
                renderLibraryList();
            });
        });

        setupSearch();
        setupPlaylistModal();
        attachPlaybackEvents();
        loadDashboardData();

        // Initial history entry
        pushHistory({ view: 'home' });
    }

    // Bootstrap when DOM ready
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }

})();
