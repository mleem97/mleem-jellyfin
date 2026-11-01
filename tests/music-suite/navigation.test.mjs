import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { test } from 'node:test';
import { JSDOM } from 'jsdom';

const source = await readFile(new URL('../../plugins/MusicSuite/Web/music-redirect.js', import.meta.url), 'utf8');
const delay = () => new Promise((resolve) => setTimeout(resolve, 30));

async function harness(status, options = {}) {
  const dom = new JSDOM('<!doctype html><html><head></head><body><nav class="sidebarLinks"></nav><div data-role="page" class="musicPage"><a data-type="music" href="#/music.html">Music</a></div></body></html>', {
    runScripts: 'outside-only', url: 'http://localhost/jellyfin/web/' + (options.hash || '#/home')
  });
  dom.window.ApiClient = {
    getUrl: (path) => '/jellyfin/' + path,
    getCurrentUserId: () => 'user',
    getJSON: async () => { if (options.failStatus) throw new Error('offline'); return status; }
  };
  dom.window.fetch = async () => ({ ok: !options.failAssets });
  dom.window.eval(source);
  dom.window.document.dispatchEvent(new dom.window.Event('DOMContentLoaded'));
  await delay();
  return dom;
}

test('disabled plugin keeps native pages and removes custom navigation', async () => {
  const dom = await harness({ enabled: false, forceForAllUsers: true }, { hash: '#/music.html' });
  try {
    assert.equal(dom.window.location.hash, '#/music.html');
    assert.equal(dom.window.document.querySelector('.musicsuite-nav-item'), null);
    assert.notEqual(dom.window.getComputedStyle(dom.window.document.querySelector('.musicPage')).display, 'none');
    assert.equal(dom.window.document.querySelector('link[data-musicsuite-theme]'), null);
  } finally { dom.window.close(); }
});

test('default policy keeps native navigation and exposes Spotify as an optional view', async () => {
  const dom = await harness({ enabled: true, forceForAllUsers: false }, { hash: '#/music.html' });
  try {
    assert.equal(dom.window.location.hash, '#/music.html');
    assert.ok(dom.window.document.querySelector('.musicsuite-nav-item'));
    assert.equal(dom.window.document.querySelector('link[data-musicsuite-theme]'), null);
  } finally { dom.window.close(); }
});

test('explicit automatic dashboard choice preserves album routes and survives disablement', async () => {
  const status = { enabled: true, forceForAllUsers: true };
  const dom = await harness(status, { hash: '#/music/albums?topParentId=123' });
  try {
    assert.match(dom.window.location.hash, /music\/albums/);
    dom.window.location.hash = '#/music.html';
    await delay();
    assert.equal(dom.window.location.hash, '#!/configurationpage?name=spotify-dashboard');
    assert.equal(dom.window.document.querySelector('link[data-musicsuite-theme]').getAttribute('href'), '/jellyfin/Plugins/MusicSuite/Assets/spotify-theme.css');
    status.enabled = false;
    await dom.window.MusicSuiteNavigation.refresh();
    await delay();
    assert.equal(dom.window.location.hash, '#/music.html');
    assert.equal(dom.window.document.querySelector('.musicsuite-nav-item'), null);
  } finally { dom.window.close(); }
});

for (const failure of ['failAssets', 'failStatus']) {
  test(`${failure} retains native route even when automatic dashboard is enabled`, async () => {
    const dom = await harness({ enabled: true, forceForAllUsers: true }, { hash: '#/music.html', [failure]: true });
    try {
      assert.equal(dom.window.location.hash, '#/music.html');
      assert.notEqual(dom.window.getComputedStyle(dom.window.document.querySelector('.musicPage')).display, 'none');
    } finally { dom.window.close(); }
  });
}

test('Spotify stylesheet does not hide native music pages', async () => {
  const css = await readFile(new URL('../../plugins/MusicSuite/Web/spotify-theme.css', import.meta.url), 'utf8');
  const dom = new JSDOM('<!doctype html><html><head></head><body><div data-role="page" class="musicPage">Native albums</div></body></html>');
  try {
    const style = dom.window.document.createElement('style');
    style.textContent = css;
    dom.window.document.head.appendChild(style);
    assert.notEqual(dom.window.getComputedStyle(dom.window.document.querySelector('.musicPage')).display, 'none');
  } finally { dom.window.close(); }
});
