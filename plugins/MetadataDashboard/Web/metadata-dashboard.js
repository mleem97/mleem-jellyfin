/**
 * MetadataDashboard Studio Client Logic
 */
(function () {
    'use strict';

    const state = {
        activeItem: null,
        currentAudit: null
    };

    const dom = {};

    function initDom() {
        // Tabs
        dom.tabBtns = document.querySelectorAll('.mdd-tab-btn');
        dom.tabContents = document.querySelectorAll('.mdd-tab-content');

        // Audit tab
        dom.btnRunAudit = document.getElementById('btnRunAudit');
        dom.txtHealthScore = document.getElementById('txtHealthScore');
        dom.barHealthFill = document.getElementById('barHealthFill');
        dom.statTotalAlbums = document.getElementById('statTotalAlbums');
        dom.statMissingCovers = document.getElementById('statMissingCovers');
        dom.statMissingMbids = document.getElementById('statMissingMbids');
        dom.statMissingSpotify = document.getElementById('statMissingSpotify');
        dom.tblAuditIssues = document.getElementById('tblAuditIssues');

        // Editor tab
        dom.inputSearchLibrary = document.getElementById('inputSearchLibrary');
        dom.btnSearchLibrary = document.getElementById('btnSearchLibrary');
        dom.editorFormWrap = document.getElementById('editorFormWrap');
        dom.editCoverPreview = document.getElementById('editCoverPreview');
        dom.btnOpenCaaModal = document.getElementById('btnOpenCaaModal');
        dom.editTitle = document.getElementById('editTitle');
        dom.editYear = document.getElementById('editYear');
        dom.editArtist = document.getElementById('editArtist');
        dom.editMbid = document.getElementById('editMbid');
        dom.editSpotify = document.getElementById('editSpotify');
        dom.editGracenote = document.getElementById('editGracenote');
        dom.btnSearchMbLive = document.getElementById('btnSearchMbLive');
        dom.btnSaveMetadata = document.getElementById('btnSaveMetadata');

        // CoverArt tab
        dom.inputDirectMbid = document.getElementById('inputDirectMbid');
        dom.btnLookupCaa = document.getElementById('btnLookupCaa');
        dom.caaResultsGrid = document.getElementById('caaResultsGrid');
        dom.providerStatusGrid = document.getElementById('providerStatusGrid');

        // Modal
        dom.modalSearch = document.getElementById('modalSearch');
        dom.modalSearchTitle = document.getElementById('modalSearchTitle');
        dom.modalSearchResults = document.getElementById('modalSearchResults');
        dom.btnModalClose = document.getElementById('btnModalClose');
    }

    // Helper: API Fetcher with Jellyfin auth headers
    function fetchApi(path, options = {}) {
        let url = path;
        const headers = options.headers || {};
        headers['Accept'] = 'application/json';

        if (window.ApiClient) {
            url = ApiClient.getUrl(path);
            if (ApiClient.accessToken && ApiClient.accessToken()) {
                headers['X-Emby-Token'] = ApiClient.accessToken();
                headers['Authorization'] = 'MediaBrowser Client="Jellyfin Web", Device="Browser", DeviceId="MetadataDashboard", Version="1.0.0", Token="' + ApiClient.accessToken() + '"';
            }
        }

        options.headers = headers;
        return fetch(url, options).then(res => {
            if (!res.ok) throw new Error('HTTP ' + res.status + ' ' + res.statusText);
            return res.json();
        });
    }

    // Tab Switching
    function setupTabs() {
        dom.tabBtns.forEach(btn => {
            btn.addEventListener('click', () => {
                const targetTab = btn.getAttribute('data-tab');
                dom.tabBtns.forEach(b => b.classList.remove('active'));
                btn.classList.add('active');

                dom.tabContents.forEach(content => {
                    if (content.id === targetTab) {
                        content.style.display = 'block';
                    } else {
                        content.style.display = 'none';
                    }
                });

                if (targetTab === 'tabCoverArt') {
                    loadProviderStatus();
                }
            });
        });
    }

    // TAB 1: RUN AUDIT
    async function runAudit() {
        if (!dom.btnRunAudit) return;
        dom.btnRunAudit.disabled = true;
        dom.btnRunAudit.innerHTML = 'Prüfe...';

        try {
            const report = await fetchApi('Plugins/MetadataDashboard/Audit');
            state.currentAudit = report;

            dom.txtHealthScore.textContent = report.healthScore + '%';
            dom.barHealthFill.style.width = report.healthScore + '%';
            dom.statTotalAlbums.textContent = report.totalAlbums;
            dom.statMissingCovers.textContent = report.missingCoversCount;
            dom.statMissingMbids.textContent = report.missingMusicBrainzCount;
            dom.statMissingSpotify.textContent = report.missingSpotifyCount;

            renderAuditTable(report.itemsWithIssues || []);
        } catch (err) {
            alert('Fehler beim Ausführen des Metadaten-Audits: ' + err.message);
        } finally {
            dom.btnRunAudit.disabled = false;
            dom.btnRunAudit.innerHTML = `
                <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M21.5 2v6h-6M21.34 15.57a10 10 0 1 1-.57-8.38l5.67-5.67"/></svg>
                Bibliothek scannen
            `;
        }
    }

    function renderAuditTable(issues) {
        if (!dom.tblAuditIssues) return;

        if (issues.length === 0) {
            dom.tblAuditIssues.innerHTML = '<tr><td colspan="5" style="text-align: center; color: var(--mdd-success); padding: 2rem;">Hervorragend! Alle geprüften Alben und Künstler besitzen vollständige Metadaten.</td></tr>';
            return;
        }

        dom.tblAuditIssues.innerHTML = issues.map(item => {
            const badges = (item.missingFields || []).map(f => `<span class="mdd-badge missing">${escapeHtml(f)}</span>`).join(' ');
            return `
                <tr>
                    <td><span class="mdd-badge ${item.type === 'Album' ? 'ok' : 'missing'}">${escapeHtml(item.type)}</span></td>
                    <td style="font-weight: 600; color: #fff;">${escapeHtml(item.name)}</td>
                    <td style="color: var(--mdd-text-sub);">${escapeHtml(item.artist)}</td>
                    <td>${badges}</td>
                    <td>
                        <button class="mdd-btn mdd-btn-secondary btn-inspect" data-id="${item.id}" style="padding: 0.35rem 0.75rem; font-size: 0.78rem;">
                            Bearbeiten ➔
                        </button>
                    </td>
                </tr>
            `;
        }).join('');

        dom.tblAuditIssues.querySelectorAll('.btn-inspect').forEach(btn => {
            btn.addEventListener('click', () => {
                const id = btn.getAttribute('data-id');
                const found = issues.find(i => i.id === id);
                if (found) loadItemIntoEditor(found);
            });
        });
    }

    // TAB 2: EDITOR
    function loadItemIntoEditor(item) {
        state.activeItem = item;
        dom.editorFormWrap.style.display = 'block';

        // Switch to Editor Tab
        const editorTabBtn = document.querySelector('.mdd-tab-btn[data-tab="tabEditor"]');
        if (editorTabBtn) editorTabBtn.click();

        dom.editTitle.value = item.name || '';
        dom.editArtist.value = item.artist || '';
        dom.editYear.value = item.year || '';
        dom.editMbid.value = item.musicBrainzId || '';
        dom.editSpotify.value = item.spotifyId || '';
        dom.editGracenote.value = item.gracenoteId || '';

        // Cover preview
        if (window.ApiClient && ApiClient.getScaledImageUrl && item.hasPrimaryImage) {
            dom.editCoverPreview.src = ApiClient.getScaledImageUrl(item.id, {
                type: 'Primary',
                maxWidth: 360,
                maxHeight: 360
            });
        } else {
            dom.editCoverPreview.src = "data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='180' height='180' viewBox='0 0 24 24' fill='%23222'%3E%3Crect width='24' height='24' rx='4'/%3E%3Cpath fill='%23666' d='M12 3v10.55c-.59-.34-1.27-.55-2-.55-2.21 0-4 1.79-4 4s1.79 4 4 4 4-1.79 4-4V7h4V3h-6z'/%3E%3C/svg%3E";
        }
    }

    async function searchLibrary() {
        const query = dom.inputSearchLibrary.value.trim();
        if (!query || !window.ApiClient) return;

        try {
            const userId = ApiClient.getCurrentUserId();
            const result = await ApiClient.getItems(userId, {
                SearchTerm: query,
                IncludeItemTypes: 'MusicAlbum,MusicArtist',
                Limit: 1
            });

            if (result.Items && result.Items.length > 0) {
                const item = result.Items[0];
                loadItemIntoEditor({
                    id: item.Id,
                    name: item.Name,
                    artist: item.AlbumArtist || item.Name,
                    type: item.Type === 'MusicAlbum' ? 'Album' : 'Künstler',
                    year: item.ProductionYear,
                    hasPrimaryImage: item.ImageTags && item.ImageTags.Primary,
                    musicBrainzId: item.ProviderIds ? (item.ProviderIds.MusicBrainzAlbum || item.ProviderIds.MusicBrainzArtist) : '',
                    spotifyId: item.ProviderIds ? item.ProviderIds.Spotify : '',
                    gracenoteId: item.ProviderIds ? item.ProviderIds.Gracenote : ''
                });
            } else {
                alert('Kein passendes Album oder Künstler in der Bibliothek gefunden.');
            }
        } catch (err) {
            alert('Fehler bei der Bibliothekssuche: ' + err.message);
        }
    }

    async function saveMetadata() {
        if (!state.activeItem || !state.activeItem.id) {
            alert('Bitte wähle zuerst ein Element aus.');
            return;
        }

        const payload = {
            itemId: state.activeItem.id,
            name: dom.editTitle.value.trim(),
            artist: dom.editArtist.value.trim(),
            year: parseInt(dom.editYear.value, 10) || null,
            musicBrainzId: dom.editMbid.value.trim(),
            spotifyId: dom.editSpotify.value.trim(),
            gracenoteId: dom.editGracenote.value.trim()
        };

        try {
            dom.btnSaveMetadata.disabled = true;
            dom.btnSaveMetadata.textContent = 'Speichere...';

            await fetchApi('Plugins/MetadataDashboard/Apply', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(payload)
            });

            alert('Metadaten erfolgreich auf ' + payload.name + ' angewendet!');
            runAudit();
        } catch (err) {
            alert('Fehler beim Speichern der Metadaten: ' + err.message);
        } finally {
            dom.btnSaveMetadata.disabled = false;
            dom.btnSaveMetadata.innerHTML = `
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M19 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11l5 5v11a2 2 0 0 1-2 2z"/><polyline points="17 21 17 13 7 13 7 21"/><polyline points="7 3 7 8 15 8"/></svg>
                Metadaten anwenden &amp; speichern
            `;
        }
    }

    // LIVE MUSICBRAINZ LOOKUP
    async function searchMusicBrainzLive() {
        const album = dom.editTitle.value.trim();
        const artist = dom.editArtist.value.trim();
        if (!album) {
            alert('Bitte gib mindestens einen Albumtitel an.');
            return;
        }

        try {
            dom.modalSearchTitle.textContent = `MusicBrainz Treffer für "${album}"`;
            dom.modalSearchResults.innerHTML = '<div style="color: #aaa; text-align: center; padding: 2rem;">Frage MusicBrainz WS/2 ab...</div>';
            dom.modalSearch.style.display = 'flex';

            const url = `Plugins/MetadataDashboard/MusicBrainz/Search?query=${encodeURIComponent(album)}&artist=${encodeURIComponent(artist)}`;
            const matches = await fetchApi(url);

            if (!matches || matches.length === 0) {
                dom.modalSearchResults.innerHTML = '<div style="color: #aaa; text-align: center; padding: 2rem;">Keine passenden MusicBrainz Releases gefunden.</div>';
                return;
            }

            dom.modalSearchResults.innerHTML = matches.map(m => `
                <div style="display: flex; justify-content: space-between; align-items: center; background: rgba(255,255,255,0.04); padding: 0.85rem; border-radius: 8px;">
                    <div>
                        <div style="font-weight: 700; color: #fff;">${escapeHtml(m.title)}</div>
                        <div style="font-size: 0.8rem; color: #aaa;">${escapeHtml(m.artist)} • ${escapeHtml(m.date || 'Jahr unbekannt')} • ${m.trackCount || 0} Tracks</div>
                        <div style="font-size: 0.72rem; color: var(--mdd-accent); margin-top: 2px;">MBID: ${m.id}</div>
                    </div>
                    <button class="mdd-btn btn-pick-mb" data-mbid="${m.id}" data-date="${m.date || ''}" style="padding: 0.4rem 0.85rem; font-size: 0.78rem;">
                        Übernehmen
                    </button>
                </div>
            `).join('');

            dom.modalSearchResults.querySelectorAll('.btn-pick-mb').forEach(b => {
                b.addEventListener('click', () => {
                    const mbid = b.getAttribute('data-mbid');
                    const date = b.getAttribute('data-date');
                    dom.editMbid.value = mbid;
                    if (date && date.length >= 4) {
                        dom.editYear.value = date.substring(0, 4);
                    }
                    dom.modalSearch.style.display = 'none';
                });
            });

        } catch (err) {
            alert('MusicBrainz Suche fehlgeschlagen: ' + err.message);
            dom.modalSearch.style.display = 'none';
        }
    }

    // TAB 3: COVERARTARCHIVE & PROVIDER STATUS
    async function lookupCaaDirect() {
        const mbid = dom.inputDirectMbid.value.trim();
        if (!mbid) {
            alert('Bitte gib eine gültige Release-MBID ein.');
            return;
        }

        try {
            dom.caaResultsGrid.innerHTML = '<div style="color: #aaa; grid-column: 1/-1;">Lade Cover von CoverArtArchive...</div>';
            const data = await fetchApi(`Plugins/MetadataDashboard/CoverArtArchive/${encodeURIComponent(mbid)}`);

            if (!data.images || data.images.length === 0) {
                dom.caaResultsGrid.innerHTML = '<div style="color: #aaa; grid-column: 1/-1;">Keine Cover im CoverArtArchive für dieses Release vorhanden.</div>';
                return;
            }

            dom.caaResultsGrid.innerHTML = data.images.map(img => {
                const thumb = img.thumbnails ? (img.thumbnails['500'] || img.thumbnails['250'] || img.image) : img.image;
                const type = (img.types || []).join(', ') || (img.front ? 'Front' : (img.back ? 'Back' : 'Art'));
                return `
                    <div style="background: rgba(255,255,255,0.03); border-radius: 8px; overflow: hidden; border: 1px solid var(--mdd-border);">
                        <img src="${thumb}" alt="${type}" style="width: 100%; aspect-ratio: 1/1; object-fit: cover;" loading="lazy">
                        <div style="padding: 0.65rem;">
                            <span class="mdd-badge ok">${escapeHtml(type)}</span>
                            <div style="margin-top: 0.5rem;">
                                <a href="${img.image}" target="_blank" style="color: var(--mdd-accent); font-size: 0.78rem; text-decoration: none;">Vollbild öffnen ➔</a>
                            </div>
                        </div>
                    </div>
                `;
            }).join('');
        } catch (err) {
            dom.caaResultsGrid.innerHTML = `<div style="color: var(--mdd-danger); grid-column: 1/-1;">Fehler: ${escapeHtml(err.message)}</div>`;
        }
    }

    async function loadProviderStatus() {
        if (!dom.providerStatusGrid) return;
        try {
            const res = await fetchApi('Plugins/MetadataDashboard/Status');
            const providers = res.providers || [];

            dom.providerStatusGrid.innerHTML = providers.map(p => `
                <div class="mdd-provider-card">
                    <div class="mdd-provider-head">
                        <div style="font-weight: 700; font-size: 1.05rem;">${escapeHtml(p.name)}</div>
                        <span class="mdd-badge ${p.active ? 'ok' : 'missing'}">${p.active ? 'Aktiv' : 'Inaktiv'}</span>
                    </div>
                    <div style="font-size: 0.85rem; color: var(--mdd-text-muted);">
                        ${escapeHtml(p.description || p.mirror || 'Provider bereit')}
                    </div>
                </div>
            `).join('');
        } catch (err) {
            dom.providerStatusGrid.innerHTML = `<div style="color: var(--mdd-danger);">Status konnte nicht geladen werden: ${escapeHtml(err.message)}</div>`;
        }
    }

    function escapeHtml(str) {
        if (!str) return '';
        return String(str)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#039;');
    }

    function init() {
        initDom();
        setupTabs();

        if (dom.btnRunAudit) dom.btnRunAudit.addEventListener('click', runAudit);
        if (dom.btnSearchLibrary) dom.btnSearchLibrary.addEventListener('click', searchLibrary);
        if (dom.btnSaveMetadata) dom.btnSaveMetadata.addEventListener('click', saveMetadata);
        if (dom.btnSearchMbLive) dom.btnSearchMbLive.addEventListener('click', searchMusicBrainzLive);

        if (dom.btnLookupCaa) dom.btnLookupCaa.addEventListener('click', lookupCaaDirect);
        if (dom.btnOpenCaaModal) {
            dom.btnOpenCaaModal.addEventListener('click', () => {
                const mbid = dom.editMbid.value.trim();
                if (mbid) {
                    dom.inputDirectMbid.value = mbid;
                    const caaTabBtn = document.querySelector('.mdd-tab-btn[data-tab="tabCoverArt"]');
                    if (caaTabBtn) caaTabBtn.click();
                    lookupCaaDirect();
                } else {
                    alert('Bitte trage zuerst eine MusicBrainz Release-ID (MBID) ein.');
                }
            });
        }

        if (dom.btnModalClose) {
            dom.btnModalClose.addEventListener('click', () => {
                dom.modalSearch.style.display = 'none';
            });
        }

        // Run initial audit on load
        runAudit();
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }

})();
