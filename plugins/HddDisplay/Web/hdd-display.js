// HDD Display & Storage Detail Page Logic
(function () {
    'use strict';

    function init() {
        const legendContainer = document.getElementById('storage-legend');
        const legendHeader = document.getElementById('legend-header');
        const refreshBtn = document.getElementById('btn-refresh');

        if (legendHeader && legendContainer) {
            legendHeader.addEventListener('click', function () {
                legendContainer.classList.toggle('collapsed');
            });
        }

        if (refreshBtn) {
            refreshBtn.addEventListener('click', function () {
                loadData(true);
            });
        }

        loadData(false);
    }

    function unitLabel(index) {
        return ['B', 'KB', 'MB', 'GB', 'TB', 'PB'][index] || 'PB';
    }

    function fmtBytes(bytes) {
        const value = Number(bytes || 0);
        if (value <= 0) return '0 B';
        const index = Math.min(Math.floor(Math.log(value) / Math.log(1024)), 5);
        const digits = index > 1 ? 2 : 0;
        return (value / Math.pow(1024, index)).toFixed(digits) + ' ' + unitLabel(index);
    }

    function pct(part, total) {
        const t = Number(total || 0);
        if (t <= 0) return 0;
        return Math.max(0, Math.min(100, Math.round((Number(part || 0) / t) * 100)));
    }

    function apiGet(url) {
        const client = window.ApiClient;
        if (client && client.getJSON && client.getUrl) {
            return client.getJSON(client.getUrl(url));
        }
        return fetch(url, { credentials: 'same-origin' }).then(function (res) {
            if (!res.ok) throw new Error('HTTP ' + res.status);
            return res.json();
        });
    }

    function getFsTypeClass(fsType) {
        const fs = String(fsType || '').toLowerCase();
        if (fs.includes('zfs') || fs.includes('btrfs')) {
            return { class: 'badge-fs-pool', label: '🟪 ' + (fsType || 'Pool') };
        }
        if (fs.includes('nfs') || fs.includes('cifs') || fs.includes('smb')) {
            return { class: 'badge-fs-net', label: '🟨 ' + (fsType || 'Network') };
        }
        return { class: 'badge-fs-local', label: '🟦 ' + (fsType || 'Local') };
    }

    function getStatusBadge(usedPct) {
        if (usedPct >= 90) {
            return { class: 'badge-status-crit', text: '🔴 ' + usedPct + '% Kritisch' };
        }
        if (usedPct >= 80) {
            return { class: 'badge-status-warn', text: '🟡 ' + usedPct + '% Warnung' };
        }
        return { class: 'badge-status-ok', text: '🟢 ' + usedPct + '% Normal' };
    }

    function getLibraryIcon(type) {
        switch (String(type || '').toLowerCase()) {
            case 'movies': return '🎬';
            case 'tvshows': return '📺';
            case 'music': return '🎵';
            case 'books':
            case 'audiobooks': return '📚';
            case 'transcode': return '⚡';
            default: return '📁';
        }
    }

    function loadData(forceRefresh) {
        const refreshBtn = document.getElementById('btn-refresh');
        if (refreshBtn) {
            refreshBtn.disabled = true;
            refreshBtn.textContent = 'Lädt...';
        }

        const query = forceRefresh ? '?refresh=true' : '';
        const storagePromise = apiGet('/Plugins/HddDisplay/Storage' + query);
        const systemPromise = apiGet('/Plugins/HddDisplay/SystemUsage' + query).catch(function () { return null; });

        Promise.all([storagePromise, systemPromise])
            .then(function (results) {
                const storageData = results[0];
                const systemData = results[1];

                renderOverview(storageData, systemData);
                renderDrives(storageData, systemData);
                renderTelemetry(storageData, systemData);
            })
            .catch(function (err) {
                const globalMetric = document.getElementById('global-storage-metric');
                if (globalMetric) {
                    globalMetric.textContent = 'Fehler beim Laden: ' + err.message;
                    globalMetric.style.color = '#E53935';
                }
            })
            .finally(function () {
                if (refreshBtn) {
                    refreshBtn.disabled = false;
                    refreshBtn.innerHTML = '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M21.5 2v6h-6M21.34 15.57a10 10 0 1 1-.57-8.38l5.67-5.67"/></svg> Aktualisieren';
                }
            });
    }

    function renderOverview(storageData) {
        const metricEl = document.getElementById('global-storage-metric');
        const progressEl = document.getElementById('global-progress-track');
        if (!metricEl || !progressEl) return;

        const drives = storageData.Drives || [];
        let totalBytes = 0;
        let usedBytes = 0;

        drives.forEach(function (d) {
            totalBytes += Number(d.TotalBytes || 0);
            usedBytes += Number(d.UsedBytes || 0);
        });

        const freeBytes = Math.max(0, totalBytes - usedBytes);
        const usedPercent = pct(usedBytes, totalBytes);

        metricEl.innerHTML = 'Frei: <strong>' + fmtBytes(freeBytes) + '</strong> von <strong>' + fmtBytes(totalBytes) + '</strong> (' + usedPercent + '% belegt)';

        // Calculate media totals across all drives
        const mediaTotals = { movies: 0, tvshows: 0, music: 0, books: 0, other: 0 };
        const usageEntries = (storageData.Usage && storageData.Usage.Entries) || [];

        usageEntries.forEach(function (u) {
            const t = String(u.LibraryType || '').toLowerCase();
            const b = Number(u.Bytes || 0);
            if (t === 'movies') mediaTotals.movies += b;
            else if (t === 'tvshows') mediaTotals.tvshows += b;
            else if (t === 'music') mediaTotals.music += b;
            else if (t === 'books' || t === 'audiobooks') mediaTotals.books += b;
            else mediaTotals.other += b;
        });

        // Known media sum
        const knownMediaBytes = mediaTotals.movies + mediaTotals.tvshows + mediaTotals.music + mediaTotals.books;
        const otherBytes = Math.max(0, usedBytes - knownMediaBytes);

        progressEl.innerHTML = '';
        if (totalBytes <= 0) return;

        function addSegment(bytes, cssClass, title) {
            if (bytes <= 0) return;
            const p = (bytes / totalBytes) * 100;
            const seg = document.createElement('div');
            seg.className = 'hdd-progress-segment ' + cssClass;
            seg.style.width = p + '%';
            seg.title = title + ': ' + fmtBytes(bytes) + ' (' + p.toFixed(1) + '%)';
            progressEl.appendChild(seg);
        }

        addSegment(mediaTotals.movies, 'seg-movies', 'Filme');
        addSegment(mediaTotals.tvshows, 'seg-tvshows', 'Serien');
        addSegment(mediaTotals.music, 'seg-music', 'Musik');
        addSegment(mediaTotals.books, 'seg-books', 'Bücher');
        addSegment(otherBytes, 'seg-other', 'Sonstige / System');
    }

    function renderDrives(storageData, systemData) {
        const container = document.getElementById('drive-cards-container');
        if (!container) return;
        container.innerHTML = '';

        const drives = storageData.Drives || [];
        const libraries = storageData.Libraries || [];
        const transcodePath = (systemData && systemData.TranscodingPath) || '';

        if (drives.length === 0) {
            container.innerHTML = '<div style="grid-column: 1/-1; padding: 2em; text-align: center; color: #888;">Keine Festplatten oder Medien-Mounts gefunden.</div>';
            return;
        }

        drives.forEach(function (drive) {
            const card = document.createElement('div');
            card.className = 'drive-card';

            const total = Number(drive.TotalBytes || 0);
            const free = Number(drive.FreeBytes || 0);
            const used = Math.max(0, total - free);
            const usedPercent = pct(used, total);

            // Filesystem info
            const fsInfo = getFsTypeClass(drive.FileSystemType);
            const statusInfo = getStatusBadge(usedPercent);

            // Check transcode drive
            const isTranscodeDrive = transcodePath && transcodePath.startsWith(drive.Name);

            // Header
            const header = document.createElement('div');
            header.className = 'drive-card-header';

            const mountInfo = document.createElement('div');
            mountInfo.className = 'drive-mount-info';

            const mountPath = document.createElement('div');
            mountPath.className = 'drive-mount-path';
            mountPath.textContent = drive.Name || 'Unbekannter Mount';
            mountInfo.appendChild(mountPath);

            const devLabel = document.createElement('div');
            devLabel.className = 'drive-device-label';
            devLabel.textContent = (drive.Source ? drive.Source + ' • ' : '') + (drive.Label && drive.Label !== drive.Name ? drive.Label : '');
            mountInfo.appendChild(devLabel);

            header.appendChild(mountInfo);

            const badges = document.createElement('div');
            badges.className = 'drive-badges';

            const fsBadge = document.createElement('span');
            fsBadge.className = 'badge ' + fsInfo.class;
            fsBadge.textContent = fsInfo.label;
            badges.appendChild(fsBadge);

            const statBadge = document.createElement('span');
            statBadge.className = 'badge ' + statusInfo.class;
            statBadge.textContent = statusInfo.text;
            badges.appendChild(statBadge);

            if (isTranscodeDrive) {
                const tcBadge = document.createElement('span');
                tcBadge.className = 'badge badge-role-transcode';
                tcBadge.textContent = '⚡ Transcode';
                badges.appendChild(tcBadge);
            }

            header.appendChild(badges);
            card.appendChild(header);

            // Segmented Progress Bar
            const track = document.createElement('div');
            track.className = 'hdd-progress-track';

            const driveUsage = drive.Usage || [];
            let driveMediaSum = 0;
            const driveMedia = { movies: 0, tvshows: 0, music: 0, books: 0 };

            driveUsage.forEach(function (u) {
                const t = String(u.LibraryType || '').toLowerCase();
                const b = Number(u.Bytes || 0);
                if (t === 'movies') driveMedia.movies += b;
                else if (t === 'tvshows') driveMedia.tvshows += b;
                else if (t === 'music') driveMedia.music += b;
                else if (t === 'books' || t === 'audiobooks') driveMedia.books += b;
                driveMediaSum += b;
            });

            const otherOnDrive = Math.max(0, used - driveMediaSum);

            function addDriveSeg(bytes, cssClass, label) {
                if (bytes <= 0 || total <= 0) return;
                const p = (bytes / total) * 100;
                const s = document.createElement('div');
                s.className = 'hdd-progress-segment ' + cssClass;
                s.style.width = p + '%';
                s.title = label + ': ' + fmtBytes(bytes) + ' (' + p.toFixed(1) + '%)';
                track.appendChild(s);
            }

            addDriveSeg(driveMedia.movies, 'seg-movies', 'Filme');
            addDriveSeg(driveMedia.tvshows, 'seg-tvshows', 'Serien');
            addDriveSeg(driveMedia.music, 'seg-music', 'Musik');
            addDriveSeg(driveMedia.books, 'seg-books', 'Bücher');
            addDriveSeg(otherOnDrive, 'seg-other', 'Sonstiges');

            card.appendChild(track);

            // Metrics Summary
            const metrics = document.createElement('div');
            metrics.className = 'drive-metrics-summary';
            metrics.innerHTML = '<span class="drive-metrics-free">Frei: <strong>' + fmtBytes(free) + '</strong> von ' + fmtBytes(total) + '</span>' +
                '<span class="drive-metrics-pct">' + usedPercent + '%</span>';
            card.appendChild(metrics);

            // Linked Libraries
            const linkedPaths = drive.LibraryPaths || [];
            if (linkedPaths.length > 0) {
                const libSection = document.createElement('div');
                libSection.className = 'drive-libraries';

                const libLabel = document.createElement('div');
                libLabel.className = 'drive-libraries-label';
                libLabel.textContent = 'Verknüpfte Bibliotheken';
                libSection.appendChild(libLabel);

                const libList = document.createElement('div');
                libList.className = 'drive-libraries-list';

                linkedPaths.forEach(function (path) {
                    const matchedLib = libraries.find(function (l) {
                        return (l.Paths || []).includes(path);
                    });

                    const libBadge = document.createElement('span');
                    libBadge.className = 'lib-badge';
                    const icon = getLibraryIcon(matchedLib ? matchedLib.Type : 'other');
                    const name = matchedLib ? matchedLib.Name : path;
                    libBadge.textContent = icon + ' ' + name;
                    libBadge.title = path;
                    libList.appendChild(libBadge);
                });

                libSection.appendChild(libList);
                card.appendChild(libSection);
            }

            container.appendChild(card);
        });
    }

    function renderTelemetry(storageData, systemData) {
        const gpuContent = document.getElementById('gpu-telemetry-content');
        if (gpuContent && storageData.Gpu) {
            const gpu = storageData.Gpu;
            if (gpu.Available && gpu.Devices && gpu.Devices.length > 0) {
                gpuContent.innerHTML = '';
                gpu.Devices.forEach(function (dev) {
                    const item = document.createElement('div');
                    item.className = 'telemetry-item';
                    item.innerHTML = '<span class="telemetry-label">' + (dev.Name || 'NVIDIA GPU') + '</span>' +
                        '<span class="telemetry-value">Auslastung: ' + dev.GpuUtilizationPercent + '% | VRAM: ' + dev.MemoryUsedMegabytes + ' / ' + dev.MemoryTotalMegabytes + ' MB</span>';
                    gpuContent.appendChild(item);
                });
            } else {
                const statusEl = document.getElementById('gpu-status');
                if (statusEl) statusEl.textContent = gpu.Diagnostic || 'Keine kompatible GPU gefunden';
            }
        }

        const sysContent = document.getElementById('system-paths-content');
        if (sysContent && systemData) {
            sysContent.innerHTML = '';
            const paths = [
                { label: 'Transcoding Cache', val: systemData.TranscodingPath },
                { label: 'Metadaten Cache', val: systemData.MetadataPath },
                { label: 'Log-Verzeichnis', val: systemData.LogPath },
                { label: 'Daten-Verzeichnis', val: systemData.DataPath }
            ];

            paths.forEach(function (p) {
                if (!p.val) return;
                const item = document.createElement('div');
                item.className = 'telemetry-item';
                item.innerHTML = '<span class="telemetry-label">' + p.label + '</span>' +
                    '<span class="telemetry-value" style="font-size:0.85em; word-break:break-all;">' + p.val + '</span>';
                sysContent.appendChild(item);
            });
        }
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
