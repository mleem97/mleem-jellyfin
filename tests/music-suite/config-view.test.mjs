import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { test } from 'node:test';
import { JSDOM } from 'jsdom';

const html = await readFile(new URL('../../plugins/MusicSuite/Web/config.html', import.meta.url), 'utf8');
const delay = () => new Promise((resolve) => setTimeout(resolve, 0));

test('admin settings load and persist real server options while retaining unrelated configuration', async () => {
  const dom = new JSDOM(html, { runScripts: 'outside-only', url: 'http://localhost/web/' });
  const writes = [];
  try {
    dom.window.ApiClient = {
      getPluginConfiguration: async () => ({ Enabled: true, ForceForAllUsers: false, AllowUserCustomization: true, DefaultTileSize: 'Small', CacheTtlHours: 6 }),
      updatePluginConfiguration: async (id, value) => { writes.push({ id, value }); },
      getUrl: (path) => path,
      getJSON: async () => ({ enabled: true, libraryCount: 2, version: '1.0.0.1' })
    };
    dom.window.eval(dom.window.document.querySelector('script').textContent);
    await delay();
    const doc = dom.window.document;
    assert.equal(doc.querySelector('#musicSuiteSettings').disabled, false);
    assert.equal(doc.querySelector('#selDefaultTileSize').value, 'Small');
    doc.querySelector('#selDefaultTileSize').value = 'Large';
    doc.querySelector('#chkAllowUserCustomization').checked = false;
    doc.querySelector('#musicSuiteForm').dispatchEvent(new dom.window.Event('submit', { bubbles: true, cancelable: true }));
    await delay();
    assert.equal(writes.length, 1);
    assert.equal(writes[0].id, '6b6c96ac-3f60-4ac3-8b8a-0d0cb8acb9dd');
    assert.equal(writes[0].value.DefaultTileSize, 'Large');
    assert.equal(writes[0].value.AllowUserCustomization, false);
    assert.equal(writes[0].value.CacheTtlHours, 6);
    assert.match(doc.querySelector('#musicSuiteSaveStatus').textContent, /erfolgreich gespeichert/);
    dom.window.ApiClient.updatePluginConfiguration = async () => { throw new Error('forbidden'); };
    doc.querySelector('#musicSuiteForm').dispatchEvent(new dom.window.Event('submit', { bubbles: true, cancelable: true }));
    await delay();
    assert.match(doc.querySelector('#musicSuiteSaveStatus').textContent, /fehlgeschlagen/);
  } finally { dom.window.close(); }
});

test('configuration remains locked when administrator authorization fails', async () => {
  const dom = new JSDOM(html, { runScripts: 'outside-only' });
  try {
    dom.window.ApiClient = { getPluginConfiguration: async () => { throw new Error('403'); } };
    dom.window.eval(dom.window.document.querySelector('script').textContent);
    await delay();
    assert.equal(dom.window.document.querySelector('#musicSuiteSettings').disabled, true);
    assert.match(dom.window.document.querySelector('#musicSuiteSaveStatus').textContent, /Administratorkonto/);
  } finally { dom.window.close(); }
});
