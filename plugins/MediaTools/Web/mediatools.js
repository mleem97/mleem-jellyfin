/**
 * MediaTools Admin Dashboard Logic
 */
(function () {
    'use strict';

    const BASE_URL = '/Plugins/MediaTools';

    // State
    let renamerResults = [];
    let splitResults = [];
    let containerResults = [];
    let dedupResults = [];
    let queueTimer = null;

    // Helper: Jellyfin API fetch with auth token support
    async function apiFetch(endpoint, options = {}) {
        const headers = {
            'Accept': 'application/json',
            ...(options.headers || {})
        };

        if (window.ApiClient && window.ApiClient.accessToken) {
            headers['X-Emby-Token'] = window.ApiClient.accessToken();
        }

        const response = await fetch(`${BASE_URL}/${endpoint}`, {
            ...options,
            headers
        });

        if (!response.ok) {
            const errText = await response.text();
            throw new Error(errText || `HTTP ${response.status}`);
        }

        const contentType = response.headers.get('content-type');
        if (contentType && contentType.includes('application/json')) {
            return await response.json();
        }
        return await response.text();
    }

    function formatBytes(bytes, decimals = 2) {
        if (!bytes || bytes === 0) return '0 B';
        const k = 1024;
        const dm = decimals < 0 ? 0 : decimals;
        const sizes = ['B', 'KB', 'MB', 'GB', 'TB'];
        const i = Math.floor(Math.log(bytes) / Math.log(k));
        return parseFloat((bytes / Math.pow(k, i)).toFixed(dm)) + ' ' + sizes[i];
    }

    function escapeHtml(str) {
        if (!str) return '';
        return String(str)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;');
    }

    // Tab Navigation
    function initTabs() {
        const tabBtns = document.querySelectorAll('.mt-tab-btn');
        tabBtns.forEach(btn => {
            btn.addEventListener('click', () => {
                const targetTabId = btn.getAttribute('data-tab');
                switchTab(targetTabId);
            });
        });
    }

    function switchTab(tabId) {
        document.querySelectorAll('.mt-tab-btn').forEach(b => {
            b.classList.toggle('active', b.getAttribute('data-tab') === tabId);
        });
        document.querySelectorAll('.mt-tab-pane').forEach(p => {
            p.classList.toggle('active', p.id === tabId);
        });

        if (tabId === 'tab-queue') {
            loadJobs();
        } else if (tabId === 'tab-settings') {
            loadSettings();
        }
    }

    // System Status
    async function loadStatus() {
        try {
            const status = await apiFetch('Status');
            const ffmpegEl = document.getElementById('mt-stat-ffmpeg');
            const ffprobeEl = document.getElementById('mt-stat-ffprobe');
            const activeJobsEl = document.getElementById('mt-stat-active-jobs');
            const completedJobsEl = document.getElementById('mt-stat-completed-jobs');

            if (status.ffmpegAvailable) {
                ffmpegEl.innerHTML = `<span class="mt-badge mt-badge-ok">Installed</span> <span class="mt-mono" style="font-size:0.75rem; color:var(--mt-text-muted);">${escapeHtml(status.ffmpegPath)}</span>`;
            } else {
                ffmpegEl.innerHTML = `<span class="mt-badge mt-badge-danger">Not Found</span>`;
            }

            if (status.ffprobeAvailable) {
                ffprobeEl.innerHTML = `<span class="mt-badge mt-badge-ok">Installed</span> <span class="mt-mono" style="font-size:0.75rem; color:var(--mt-text-muted);">${escapeHtml(status.ffprobePath)}</span>`;
            } else {
                ffprobeEl.innerHTML = `<span class="mt-badge mt-badge-danger">Not Found</span>`;
            }

            activeJobsEl.textContent = status.activeJobsCount;
            completedJobsEl.textContent = status.completedJobsCount;
        } catch (err) {
            console.error('Failed to load status:', err);
        }
    }

    // --- Tab 1: Renamer ---
    async function previewRenames() {
        const type = document.getElementById('renamer-filter-type').value;
        const limit = document.getElementById('renamer-limit').value || 100;
        const container = document.getElementById('renamer-results-container');
        const previewBtn = document.getElementById('btn-renamer-preview');
        const executeBtn = document.getElementById('btn-renamer-execute');

        previewBtn.disabled = true;
        previewBtn.textContent = 'Scanning...';
        container.innerHTML = `<div class="mt-empty-state">Scanning library and computing standardized paths...</div>`;

        try {
            const query = new URLSearchParams();
            if (type) query.set('itemTypes', type);
            query.set('limit', limit);

            renamerResults = await apiFetch(`Renamer/Preview?${query.toString()}`);
            renderRenamerTable();
        } catch (err) {
            container.innerHTML = `<div class="mt-empty-state" style="color:var(--mt-danger);">Error: ${escapeHtml(err.message)}</div>`;
        } finally {
            previewBtn.disabled = false;
            previewBtn.textContent = 'Scan & Preview Renames';
        }
    }

    function renderRenamerTable() {
        const container = document.getElementById('renamer-results-container');
        const executeBtn = document.getElementById('btn-renamer-execute');

        if (!renamerResults || renamerResults.length === 0) {
            container.innerHTML = `<div class="mt-empty-state">No rename suggestions found. All analyzed items already match the naming pattern!</div>`;
            executeBtn.disabled = true;
            document.getElementById('renamer-selected-count').textContent = '0';
            return;
        }

        let html = `
            <div class="mt-table-wrapper">
                <table class="mt-table">
                    <thead>
                        <tr>
                            <th style="width: 40px;"><input type="checkbox" id="renamer-check-all" checked></th>
                            <th>Item Name</th>
                            <th>Type</th>
                            <th>Current Path &rarr; Proposed Path</th>
                            <th>Reason</th>
                        </tr>
                    </thead>
                    <tbody>
        `;

        renamerResults.forEach((item, idx) => {
            html += `
                <tr>
                    <td><input type="checkbox" class="renamer-row-check" data-id="${item.itemId}" checked></td>
                    <td><strong>${escapeHtml(item.itemName)}</strong></td>
                    <td><span class="mt-badge mt-badge-info">${escapeHtml(item.mediaType)}</span></td>
                    <td class="mt-mono">
                        <div class="mt-diff-del">${escapeHtml(item.currentFilename)}</div>
                        <div class="mt-diff-add">&darr; ${escapeHtml(item.proposedFilename)}</div>
                    </td>
                    <td><span class="mt-badge mt-badge-warn">${escapeHtml(item.reason)}</span></td>
                </tr>
            `;
        });

        html += `</tbody></table></div>`;
        container.innerHTML = html;

        // Event bindings
        const checkAll = document.getElementById('renamer-check-all');
        const rowChecks = document.querySelectorAll('.renamer-row-check');

        const updateSelectedCount = () => {
            const count = document.querySelectorAll('.renamer-row-check:checked').length;
            document.getElementById('renamer-selected-count').textContent = count;
            executeBtn.disabled = count === 0;
        };

        checkAll.addEventListener('change', () => {
            rowChecks.forEach(c => c.checked = checkAll.checked);
            updateSelectedCount();
        });

        rowChecks.forEach(c => c.addEventListener('change', updateSelectedCount));
        updateSelectedCount();
    }

    async function executeRenames() {
        const selected = Array.from(document.querySelectorAll('.renamer-row-check:checked')).map(c => c.getAttribute('data-id'));
        if (selected.length === 0) return;

        if (!confirm(`Are you sure you want to execute safe rename on ${selected.length} item(s)? Original database IDs and watch history will be preserved.`)) {
            return;
        }

        const executeBtn = document.getElementById('btn-renamer-execute');
        executeBtn.disabled = true;
        executeBtn.textContent = 'Executing...';

        try {
            const result = await apiFetch('Renamer/Execute', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ itemIds: selected })
            });

            alert(`Renamed: ${result.succeeded} successful, ${result.failed} failed.`);
            previewRenames();
        } catch (err) {
            alert('Rename execution failed: ' + err.message);
        } finally {
            executeBtn.textContent = `Execute Selected Renames (${selected.length})`;
            executeBtn.disabled = false;
        }
    }

    // --- Tab 2: Split Movies ---
    async function scanSplitMovies() {
        const container = document.getElementById('split-results-container');
        const scanBtn = document.getElementById('btn-split-scan');

        scanBtn.disabled = true;
        scanBtn.textContent = 'Scanning...';
        container.innerHTML = `<div class="mt-empty-state">Scanning for CD1/CD2 and multi-part movie files...</div>`;

        try {
            splitResults = await apiFetch('SplitMovies/Scan');
            renderSplitTable();
        } catch (err) {
            container.innerHTML = `<div class="mt-empty-state" style="color:var(--mt-danger);">Error: ${escapeHtml(err.message)}</div>`;
        } finally {
            scanBtn.disabled = false;
            scanBtn.textContent = 'Scan for Split Movies';
        }
    }

    function renderSplitTable() {
        const container = document.getElementById('split-results-container');
        if (!splitResults || splitResults.length === 0) {
            container.innerHTML = `<div class="mt-empty-state">No multi-part split movies discovered in your library.</div>`;
            return;
        }

        let html = `
            <div class="mt-table-wrapper">
                <table class="mt-table">
                    <thead>
                        <tr>
                            <th>Movie Title</th>
                            <th>Parts Detected</th>
                            <th>Smart Concat</th>
                            <th>Target Output</th>
                            <th>Action</th>
                        </tr>
                    </thead>
                    <tbody>
        `;

        splitResults.forEach(grp => {
            const partsList = grp.parts.map(p => `Part ${p.partIndex}: ${escapeHtml(p.filename)} (${formatBytes(p.fileSizeBytes)})`).join('<br>');
            const concatBadge = grp.canSmartConcat
                ? `<span class="mt-badge mt-badge-ok">Lossless (-c copy)</span>`
                : `<span class="mt-badge mt-badge-warn">Requires Re-encode</span>`;

            html += `
                <tr>
                    <td><strong>${escapeHtml(grp.title)}</strong> ${grp.year ? `(${grp.year})` : ''}</td>
                    <td class="mt-mono">${partsList}</td>
                    <td>${concatBadge}</td>
                    <td class="mt-mono">${escapeHtml(grp.targetOutputFilename)}</td>
                    <td>
                        <button class="mt-btn mt-btn-primary btn-merge-movie" data-key="${escapeHtml(grp.groupKey)}">
                            Merge Parts
                        </button>
                    </td>
                </tr>
            `;
        });

        html += `</tbody></table></div>`;
        container.innerHTML = html;

        document.querySelectorAll('.btn-merge-movie').forEach(btn => {
            btn.addEventListener('click', async () => {
                const key = btn.getAttribute('data-key');
                btn.disabled = true;
                btn.textContent = 'Enqueuing...';
                try {
                    await apiFetch('SplitMovies/Merge', {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ groupKey: key, forceReencode: false })
                    });
                    switchTab('tab-queue');
                } catch (err) {
                    alert('Merge enqueue failed: ' + err.message);
                    btn.disabled = false;
                    btn.textContent = 'Merge Parts';
                }
            });
        });
    }

    // --- Tab 3: Containers ---
    async function scanContainers() {
        const container = document.getElementById('containers-results-container');
        const scanBtn = document.getElementById('btn-containers-scan');

        scanBtn.disabled = true;
        scanBtn.textContent = 'Scanning...';
        container.innerHTML = `<div class="mt-empty-state">Scanning library for non-MKV video files...</div>`;

        try {
            containerResults = await apiFetch('Containers/Scan');
            renderContainersTable();
        } catch (err) {
            container.innerHTML = `<div class="mt-empty-state" style="color:var(--mt-danger);">Error: ${escapeHtml(err.message)}</div>`;
        } finally {
            scanBtn.disabled = false;
            scanBtn.textContent = 'Scan Non-MKV Media';
        }
    }

    function renderContainersTable() {
        const container = document.getElementById('containers-results-container');
        const convertBtn = document.getElementById('btn-containers-convert');

        if (!containerResults || containerResults.length === 0) {
            container.innerHTML = `<div class="mt-empty-state">No non-MKV video files found. All videos are already standardized Matroska (.mkv)!</div>`;
            convertBtn.disabled = true;
            document.getElementById('containers-selected-count').textContent = '0';
            return;
        }

        let html = `
            <div class="mt-table-wrapper">
                <table class="mt-table">
                    <thead>
                        <tr>
                            <th style="width: 40px;"><input type="checkbox" id="containers-check-all" checked></th>
                            <th>Title</th>
                            <th>Container</th>
                            <th>Size</th>
                            <th>Codecs (V / A)</th>
                            <th>External Subtitles</th>
                        </tr>
                    </thead>
                    <tbody>
        `;

        containerResults.forEach(item => {
            const subsText = item.externalSubtitlesCount > 0
                ? `<span class="mt-badge mt-badge-ok">${item.externalSubtitlesCount} found (${item.subtitleLanguages.join(', ')})</span>`
                : `<span class="mt-badge mt-badge-warn">None</span>`;

            html += `
                <tr>
                    <td><input type="checkbox" class="containers-row-check" data-id="${item.itemId}" checked></td>
                    <td><strong>${escapeHtml(item.title)}</strong><div class="mt-mono mt-diff-del" style="font-size:0.75rem;">${escapeHtml(item.currentPath)}</div></td>
                    <td><span class="mt-badge mt-badge-info">${escapeHtml(item.currentContainer)}</span></td>
                    <td>${formatBytes(item.fileSizeBytes)}</td>
                    <td>${escapeHtml(item.videoCodec)} / ${escapeHtml(item.audioCodec)}</td>
                    <td>${subsText}</td>
                </tr>
            `;
        });

        html += `</tbody></table></div>`;
        container.innerHTML = html;

        const checkAll = document.getElementById('containers-check-all');
        const rowChecks = document.querySelectorAll('.containers-row-check');

        const updateSelected = () => {
            const count = document.querySelectorAll('.containers-row-check:checked').length;
            document.getElementById('containers-selected-count').textContent = count;
            convertBtn.disabled = count === 0;
        };

        checkAll.addEventListener('change', () => {
            rowChecks.forEach(c => c.checked = checkAll.checked);
            updateSelected();
        });

        rowChecks.forEach(c => c.addEventListener('change', updateSelected));
        updateSelected();
    }

    async function convertContainers() {
        const selected = Array.from(document.querySelectorAll('.containers-row-check:checked')).map(c => c.getAttribute('data-id'));
        if (selected.length === 0) return;

        const remuxOnly = document.getElementById('containers-remux-only').checked;
        const includeSubs = document.getElementById('containers-include-subs').checked;

        try {
            await apiFetch('Containers/Convert', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    itemIds: selected,
                    remuxOnly: remuxOnly,
                    includeExternalSubtitles: includeSubs
                })
            });
            switchTab('tab-queue');
        } catch (err) {
            alert('Failed to enqueue conversion: ' + err.message);
        }
    }

    // --- Tab 4: Deduplicator ---
    async function scanDuplicates() {
        const type = document.getElementById('dedup-filter-type').value;
        const minSizeMb = document.getElementById('dedup-min-size').value || 1;
        const container = document.getElementById('dedup-results-container');
        const scanBtn = document.getElementById('btn-dedup-scan');

        scanBtn.disabled = true;
        scanBtn.textContent = 'Scanning & Hashing...';
        container.innerHTML = `<div class="mt-empty-state">Computing tag-skipping MD5 content hashes across library...</div>`;

        try {
            const query = new URLSearchParams();
            if (type) query.set('mediaType', type);
            query.set('minSizeBytes', parseInt(minSizeMb, 10) * 1024 * 1024);

            dedupResults = await apiFetch(`Deduplicator/Scan?${query.toString()}`);
            renderDedupTable();
        } catch (err) {
            container.innerHTML = `<div class="mt-empty-state" style="color:var(--mt-danger);">Error: ${escapeHtml(err.message)}</div>`;
        } finally {
            scanBtn.disabled = false;
            scanBtn.textContent = 'Scan for Duplicates';
        }
    }

    function renderDedupTable() {
        const container = document.getElementById('dedup-results-container');
        const quarantineBtn = document.getElementById('btn-dedup-quarantine');

        if (!dedupResults || dedupResults.length === 0) {
            container.innerHTML = `<div class="mt-empty-state">No duplicate media streams found. Library is 100% deduplicated!</div>`;
            quarantineBtn.disabled = true;
            document.getElementById('dedup-selected-count').textContent = '0';
            return;
        }

        let html = `
            <div class="mt-table-wrapper">
                <table class="mt-table">
                    <thead>
                        <tr>
                            <th style="width: 40px;">Select</th>
                            <th>Media Title &amp; Stream MD5</th>
                            <th>File Path</th>
                            <th>Size</th>
                            <th>Quality / Role</th>
                        </tr>
                    </thead>
                    <tbody>
        `;

        dedupResults.forEach(grp => {
            grp.files.forEach(f => {
                const isMaster = f.isMaster;
                const roleBadge = isMaster
                    ? `<span class="mt-badge mt-badge-ok">⭐ Master (Keep)</span>`
                    : `<span class="mt-badge mt-badge-warn">Duplicate Candidate</span>`;

                html += `
                    <tr>
                        <td>
                            ${isMaster ? '' : `<input type="checkbox" class="dedup-row-check" data-path="${escapeHtml(f.path)}" checked>`}
                        </td>
                        <td>
                            <strong>${escapeHtml(grp.mediaTitle)}</strong>
                            <div class="mt-mono" style="font-size:0.75rem; color:var(--mt-text-muted);">${escapeHtml(grp.streamHash)}</div>
                        </td>
                        <td class="mt-mono">${escapeHtml(f.path)}</td>
                        <td>${formatBytes(f.fileSizeBytes)}</td>
                        <td>${roleBadge}</td>
                    </tr>
                `;
            });
        });

        html += `</tbody></table></div>`;
        container.innerHTML = html;

        const rowChecks = document.querySelectorAll('.dedup-row-check');
        const updateSelected = () => {
            const count = document.querySelectorAll('.dedup-row-check:checked').length;
            document.getElementById('dedup-selected-count').textContent = count;
            quarantineBtn.disabled = count === 0;
        };

        rowChecks.forEach(c => c.addEventListener('change', updateSelected));
        updateSelected();
    }

    async function quarantineDuplicates() {
        const selected = Array.from(document.querySelectorAll('.dedup-row-check:checked')).map(c => c.getAttribute('data-path'));
        if (selected.length === 0) return;

        if (!confirm(`Safely move ${selected.length} duplicate file(s) to quarantine folder?`)) {
            return;
        }

        try {
            const res = await apiFetch('Deduplicator/Quarantine', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ filePathsToQuarantine: selected })
            });

            alert(`Quarantined ${res.quarantinedCount} file(s) into '${res.quarantineFolder}' folder.`);
            scanDuplicates();
        } catch (err) {
            alert('Quarantine failed: ' + err.message);
        }
    }

    // --- Tab 5: Live Queue ---
    async function loadJobs() {
        try {
            const jobs = await apiFetch('Jobs');
            renderJobsTable(jobs);
            loadStatus();
        } catch (err) {
            console.error('Failed to load jobs:', err);
        }
    }

    function renderJobsTable(jobs) {
        const container = document.getElementById('queue-results-container');
        if (!jobs || jobs.length === 0) {
            container.innerHTML = `<div class="mt-empty-state">No jobs in queue or history.</div>`;
            return;
        }

        let html = `
            <div class="mt-table-wrapper">
                <table class="mt-table">
                    <thead>
                        <tr>
                            <th>Type</th>
                            <th>Title</th>
                            <th>Status</th>
                            <th>Progress</th>
                            <th>Speed</th>
                            <th>Action</th>
                        </tr>
                    </thead>
                    <tbody>
        `;

        jobs.forEach(j => {
            let statusBadge = `<span class="mt-badge mt-badge-info">${escapeHtml(j.status)}</span>`;
            if (j.status === 'Completed') statusBadge = `<span class="mt-badge mt-badge-ok">Completed</span>`;
            else if (j.status === 'Running') statusBadge = `<span class="mt-badge mt-badge-info">Running</span>`;
            else if (j.status === 'Failed') statusBadge = `<span class="mt-badge mt-badge-danger">Failed: ${escapeHtml(j.errorMessage)}</span>`;
            else if (j.status === 'Cancelled') statusBadge = `<span class="mt-badge mt-badge-warn">Cancelled</span>`;

            const canCancel = j.status === 'Queued' || j.status === 'Running';

            html += `
                <tr>
                    <td><span class="mt-badge mt-badge-info">${escapeHtml(j.type)}</span></td>
                    <td><strong>${escapeHtml(j.title)}</strong></td>
                    <td>${statusBadge}</td>
                    <td style="min-width: 160px;">
                        <div>${j.percentComplete}%</div>
                        <div class="mt-progress-track">
                            <div class="mt-progress-bar" style="width: ${j.percentComplete}%;"></div>
                        </div>
                    </td>
                    <td>${escapeHtml(j.speed || '-')}</td>
                    <td>
                        ${canCancel ? `<button class="mt-btn mt-btn-danger btn-cancel-job" data-id="${j.jobId}" style="padding:0.3rem 0.6rem; font-size:0.75rem;">Cancel</button>` : '-'}
                    </td>
                </tr>
            `;
        });

        html += `</tbody></table></div>`;
        container.innerHTML = html;

        document.querySelectorAll('.btn-cancel-job').forEach(btn => {
            btn.addEventListener('click', async () => {
                const id = btn.getAttribute('data-id');
                btn.disabled = true;
                try {
                    await apiFetch(`Jobs/${id}/Cancel`, { method: 'POST' });
                    loadJobs();
                } catch (err) {
                    alert('Cancel failed: ' + err.message);
                }
            });
        });
    }

    // --- Tab 6: Settings ---
    async function loadSettings() {
        try {
            const cfg = await apiFetch('Configuration');
            document.getElementById('cfg-ffmpeg-path').value = cfg.fFmpegPathOverride || '';
            document.getElementById('cfg-ffprobe-path').value = cfg.fFprobePathOverride || '';
            document.getElementById('cfg-quarantine-folder').value = cfg.quarantineFolderName || '_duplicates';
            document.getElementById('cfg-movie-pattern').value = cfg.movieRenamePattern || '';
            document.getElementById('cfg-episode-pattern').value = cfg.episodeRenamePattern || '';
            document.getElementById('cfg-audio-pattern').value = cfg.audioRenamePattern || '';
        } catch (err) {
            console.error('Failed to load settings:', err);
        }
    }

    async function saveSettings(e) {
        e.preventDefault();
        const statusEl = document.getElementById('cfg-save-status');
        statusEl.textContent = 'Saving...';
        statusEl.style.color = 'var(--mt-primary)';

        const payload = {
            fFmpegPathOverride: document.getElementById('cfg-ffmpeg-path').value.trim(),
            fFprobePathOverride: document.getElementById('cfg-ffprobe-path').value.trim(),
            quarantineFolderName: document.getElementById('cfg-quarantine-folder').value.trim() || '_duplicates',
            movieRenamePattern: document.getElementById('cfg-movie-pattern').value.trim(),
            episodeRenamePattern: document.getElementById('cfg-episode-pattern').value.trim(),
            audioRenamePattern: document.getElementById('cfg-audio-pattern').value.trim(),
            enabled: true
        };

        try {
            await apiFetch('Configuration', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(payload)
            });
            statusEl.textContent = 'Saved successfully!';
            statusEl.style.color = 'var(--mt-success)';
            loadStatus();
            setTimeout(() => { statusEl.textContent = ''; }, 3000);
        } catch (err) {
            statusEl.textContent = 'Save failed: ' + err.message;
            statusEl.style.color = 'var(--mt-danger)';
        }
    }

    // Initialize Event Handlers
    function init() {
        initTabs();

        document.getElementById('mt-btn-refresh-status').addEventListener('click', loadStatus);
        document.getElementById('btn-renamer-preview').addEventListener('click', previewRenames);
        document.getElementById('btn-renamer-execute').addEventListener('click', executeRenames);
        document.getElementById('btn-split-scan').addEventListener('click', scanSplitMovies);
        document.getElementById('btn-containers-scan').addEventListener('click', scanContainers);
        document.getElementById('btn-containers-convert').addEventListener('click', convertContainers);
        document.getElementById('btn-dedup-scan').addEventListener('click', scanDuplicates);
        document.getElementById('btn-dedup-quarantine').addEventListener('click', quarantineDuplicates);
        document.getElementById('btn-queue-refresh').addEventListener('click', loadJobs);
        document.getElementById('form-settings').addEventListener('submit', saveSettings);

        // Auto-refresh timer for Live Queue
        queueTimer = setInterval(() => {
            const queueTab = document.getElementById('tab-queue');
            const autoRefresh = document.getElementById('queue-auto-refresh');
            if (queueTab && queueTab.classList.contains('active') && autoRefresh && autoRefresh.checked) {
                loadJobs();
            }
        }, 2000);

        loadStatus();
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
