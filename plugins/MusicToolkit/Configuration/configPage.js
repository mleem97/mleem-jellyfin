(function () {
    'use strict';

    var pluginId = '9f3e582a-281b-4b21-8c43-b295cbfa51de';
    var previewCache = [];

    function status(msg, error) {
        var n = document.querySelector('#mtkStatus');
        if (n) {
            n.textContent = msg;
            n.style.color = error ? '#ff6b6b' : '';
        }
    }

    function renderTable(rows) {
        var body = document.querySelector('#mtkTable tbody');
        body.innerHTML = '';
        rows.forEach(function (r) {
            var tr = document.createElement('tr');
            [['OldPath'], ['NewPath'], ['Status'], ['Confidence']].forEach(function (k) {
                var td = document.createElement('td');
                td.textContent = r[k[0]];
                tr.appendChild(td);
            });
            body.appendChild(tr);
        });
    }

    function load() {
        window.Dashboard.showLoadingMsg();
        window.ApiClient.getPluginConfiguration(pluginId).then(function (config) {
            document.querySelector('#quarantineDuplicates').checked = config.QuarantineDuplicates !== false;
            document.querySelector('#quarantineFolder').value = config.QuarantineFolder || '_duplicates';
            document.querySelector('#preferredCodec').value = config.PreferredCodec || 'flac';
            document.querySelector('#renamePattern').value = config.RenamePattern || '{Artist}/{Album}/{TrackNumber:02d} - {Title}';
            window.Dashboard.hideLoadingMsg();
        });
    }

    document.querySelector('#musicToolkitPage').addEventListener('pageshow', load);

    document.querySelector('#musicToolkitForm').addEventListener('submit', function (e) {
        e.preventDefault();
        window.Dashboard.showLoadingMsg();
        window.ApiClient.getPluginConfiguration(pluginId).then(function (config) {
            config.QuarantineDuplicates = document.querySelector('#quarantineDuplicates').checked;
            config.QuarantineFolder = document.querySelector('#quarantineFolder').value;
            config.PreferredCodec = document.querySelector('#preferredCodec').value;
            config.RenamePattern = document.querySelector('#renamePattern').value;
            window.ApiClient.updatePluginConfiguration(pluginId, config).then(function () {
                window.Dashboard.hideLoadingMsg();
                window.Dashboard.alert('Gespeichert.');
            });
        });
        return false;
    });

    document.querySelector('#btnDryRun').addEventListener('click', function () {
        status('Berechne Vorschau …');
        window.ApiClient.fetch({ url: window.ApiClient.getUrl('MusicToolkit/DryRun'), method: 'GET' }).then(function (r) { return r.json(); }).then(function (rows) {
            previewCache = rows || [];
            renderTable(previewCache);
            status(previewCache.length + ' Dateien analysiert.');
        }).catch(function (err) { status('DryRun fehlgeschlagen: ' + err, true); });
    });

    document.querySelector('#btnRename').addEventListener('click', function () {
        if (!previewCache.length) { status('Erst Vorschau berechnen.', true); return; }
        status('Benenne um …');
        window.ApiClient.fetch({ url: window.ApiClient.getUrl('MusicToolkit/ExecuteRename'), method: 'POST', body: JSON.stringify({ items: previewCache }) }).then(function (r) { return r.json(); }).then(function (res) {
            status('Umbenannt: ' + res.Renamed + ', übersprungen: ' + res.Skipped + (res.Errors && res.Errors.length ? ', Fehler: ' + res.Errors.join('; ') : ''));
        }).catch(function (err) { status('Rename fehlgeschlagen: ' + err, true); });
    });

    document.querySelector('#btnDedup').addEventListener('click', function () {
        status('Bereinige Duplikate …');
        window.ApiClient.fetch({ url: window.ApiClient.getUrl('MusicToolkit/Deduplicate'), method: 'POST' }).then(function (r) { return r.json(); }).then(function (groups) {
            status((groups || []).length + ' Duplikatgruppen in Quarantäne verschoben.');
        }).catch(function (err) { status('Dedup fehlgeschlagen: ' + err, true); });
    });
})();
