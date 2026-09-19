import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import { JSDOM } from 'jsdom';

const currentDirectory = path.dirname(fileURLToPath(import.meta.url));
const assetPath = path.resolve(
  currentDirectory,
  '../../plugins/MusicSuite/Web/albums-view.js'
);

async function loadLib() {
  const source = await readFile(assetPath, 'utf8');
  const dom = new JSDOM('<!doctype html><html><body></body></html>', {
    pretendToBeVisual: true,
    runScripts: 'outside-only',
    url: 'http://localhost/web/#/home'
  });
  try {
    dom.window.eval(source);
    const lib = dom.window.MusicSuiteAlbumsLib;
    assert.ok(lib, 'asset must expose window.MusicSuiteAlbumsLib');
    return { dom, lib };
  } catch (error) {
    dom.window.close();
    throw error;
  }
}

function makeAlbums(count, prefix = 'album') {
  return Array.from({ length: count }, (_, index) => ({
    Id: `00000000-0000-4000-8000-${String(index).padStart(12, '0')}`,
    Name: `${prefix} ${index}`,
    AlbumArtist: `Artist ${index % 50}`,
    ProductionYear: 1990 + (index % 35),
    HasPrimaryImage: true
  }));
}

// Values cross the jsdom realm boundary; strict deep-equal would compare
// prototypes, so tests normalize through JSON first.
function plain(value) {
  return JSON.parse(JSON.stringify(value));
}

test('paging helpers clamp to the backend bounds', async () => {
  const { dom, lib } = await loadLib();
  try {
    assert.equal(lib.clampBatchSize(50), 50);
    assert.equal(lib.clampBatchSize(0), 1);
    assert.equal(lib.clampBatchSize(10000), 200);
    assert.equal(lib.clampBatchSize(Number.NaN), 100);
    assert.equal(lib.clampDebounceMs(300), 300);
    assert.equal(lib.clampDebounceMs(1), 100);
    assert.equal(lib.clampDebounceMs(99999), 2000);
  } finally {
    dom.window.close();
  }
});

for (const count of [0, 10, 1000, 10000]) {
  test(`dedupe merges ${count} albums without duplicates and respects the DOM cap`, async () => {
    const { dom, lib } = await loadLib();
    try {
      const seen = {};
      const page = makeAlbums(count);
      // Simulate an overlapping re-fetch: every 3rd item repeats.
      const duplicates = page.filter((_, index) => index % 3 === 0);
      const fresh = lib.mergeAlbumPage(seen, [...page, ...duplicates]);
      assert.equal(fresh.length, count);
      assert.equal(Object.keys(seen).length, count);
      // Re-merging the same payload yields nothing new.
      assert.deepEqual(plain(lib.mergeAlbumPage(seen, page)), []);
      // Items without an id are skipped instead of rendered as ghosts.
      assert.deepEqual(plain(lib.mergeAlbumPage({}, [{ Name: 'ghost' }])), []);

      const capped = lib.applyDomCap(fresh, lib.DEFAULT_DOM_CAP);
      assert.ok(capped.length <= lib.DEFAULT_DOM_CAP);
      if (count > lib.DEFAULT_DOM_CAP) {
        assert.equal(capped.length, lib.DEFAULT_DOM_CAP);
        assert.equal(capped[capped.length - 1].Name, `${'album'} ${count - 1}`);
      }
    } finally {
      dom.window.close();
    }
  });
}

test('sequence guard lets only the last of rapid searches win', async () => {
  const { dom, lib } = await loadLib();
  try {
    const guard = lib.createSequenceGuard();
    const applied = [];
    const tokens = [guard.issue(), guard.issue(), guard.issue()];
    // Responses arrive out of order: first and second are stale.
    for (const [order, token] of [[1, tokens[0]], [2, tokens[2]], [0, tokens[1]]]) {
      if (guard.isCurrent(token)) applied.push(order);
    }
    assert.deepEqual(applied, [2]);
  } finally {
    dom.window.close();
  }
});

test('filter changes reset paging but keep the filters', async () => {
  const { dom, lib } = await loadLib();
  try {
    const filters = lib.createFilterState({
      searchTerm: '  miles ',
      sortBy: 'AlbumArtist',
      sortOrder: 'Descending',
      isFavorite: true,
      missingCover: true,
      genre: 'Jazz',
      year: '1959'
    });
    assert.equal(filters.searchTerm, 'miles');
    assert.equal(filters.sortBy, 'AlbumArtist');
    assert.equal(filters.sortOrder, 'Descending');

    const reset = lib.resetPaging({
      filters,
      items: makeAlbums(5),
      seen: { x: true },
      nextStart: 200,
      hasMore: false
    });
    assert.deepEqual(plain(reset.items), []);
    assert.deepEqual(plain(reset.seen), {});
    assert.equal(reset.nextStart, 0);
    assert.equal(reset.hasMore, true);
    assert.equal(reset.filters.searchTerm, 'miles');
    assert.equal(reset.filters.genre, 'Jazz');
  } finally {
    dom.window.close();
  }
});

test('query builder uses the existing AlbumQueryRequest parameter names', async () => {
  const { dom, lib } = await loadLib();
  try {
    const params = new URLSearchParams(
      lib.buildAlbumsQuery({
        startIndex: 200,
        limit: 10000,
        parentId: '6b6c96ac-3f60-4ac3-8b8a-0d0cb8acb9dd',
        searchTerm: 'miles',
        sortBy: 'ProductionYear',
        sortOrder: 'Descending',
        isFavorite: true,
        missingCover: true,
        genre: 'Jazz',
        year: '1959'
      })
    );
    assert.equal(params.get('StartIndex'), '200');
    assert.equal(params.get('Limit'), '200');
    assert.equal(params.get('ParentId'), '6b6c96ac-3f60-4ac3-8b8a-0d0cb8acb9dd');
    assert.equal(params.get('SearchTerm'), 'miles');
    assert.equal(params.get('SortBy'), 'ProductionYear');
    assert.equal(params.get('SortOrder'), 'Descending');
    assert.equal(params.get('IsFavorite'), 'true');
    assert.equal(params.get('MissingCover'), 'true');
    assert.equal(params.get('Genre'), 'Jazz');
    assert.equal(params.get('Year'), '1959');
    assert.equal(params.get('Fields'), 'Genres,DateCreated');

    const invalid = new URLSearchParams(lib.buildAlbumsQuery({ year: '99', limit: 0 }));
    assert.equal(invalid.get('Year'), null);
    assert.equal(invalid.get('Limit'), '1');
    assert.equal(invalid.get('IsFavorite'), null);
  } finally {
    dom.window.close();
  }
});

test('scroll restore round-trips through sessionStorage keys', async () => {
  const { dom, lib } = await loadLib();
  try {
    const storage = new Map();
    const fakeStorage = {
      getItem: (key) => (storage.has(key) ? storage.get(key) : null),
      setItem: (key, value) => storage.set(key, key + '=' + value)
    };
    assert.ok(lib.saveScrollPosition(fakeStorage, 'route|parent', 1234));
    assert.equal(lib.readScrollPosition(fakeStorage, 'route|parent'), 0);
    // Non-numeric stored values fail open to 0; conforming stub round-trips.
    const conforming = {
      data: {},
      getItem(key) { return this.data[key] ?? null; },
      setItem(key, value) { this.data[key] = String(value); }
    };
    assert.ok(lib.saveScrollPosition(conforming, 'route|parent', 1234));
    assert.equal(lib.readScrollPosition(conforming, 'route|parent'), 1234);
    assert.equal(lib.readScrollPosition(conforming, 'other'), 0);
    assert.equal(lib.readScrollPosition(null, 'route|parent'), 0);
    assert.equal(lib.saveScrollPosition(null, 'route|parent', 10), false);
    assert.ok(lib.scrollKey('a|b').length > 3);
  } finally {
    dom.window.close();
  }
});

test('activation stays fail-open on missing or disabled context', async () => {
  const { dom, lib } = await loadLib();
  try {
    assert.equal(lib.shouldActivate(null), false);
    assert.equal(lib.shouldActivate({}), false);
    assert.equal(lib.shouldActivate({ enabled: false, isMusicLibrary: true }), false);
    assert.equal(lib.shouldActivate({ enabled: true, isMusicLibrary: false }), false);
    assert.equal(lib.shouldActivate({ enabled: true, isMusicLibrary: true }), true);
    assert.equal(lib.shouldActivate({ Enabled: true, IsMusicLibrary: true }), true);
  } finally {
    dom.window.close();
  }
});

test('tile sizes normalize case-insensitively with an admin fallback', async () => {
  const { dom, lib } = await loadLib();
  try {
    assert.equal(lib.normalizeTileSize('large', 'Small'), 'Large');
    assert.equal(lib.normalizeTileSize('bogus', 'Small'), 'Small');
    assert.equal(lib.normalizeTileSize(undefined, undefined), 'Medium');
    assert.equal(lib.resolveTileSize({ TileSize: 'Small' }, 'Medium'), 'Small');
    assert.equal(lib.resolveTileSize({ tileSize: 'Large' }, 'Medium'), 'Large');
    assert.equal(lib.resolveTileSize({}, 'Small'), 'Small');
    assert.equal(
      lib.settingsUrl('6b6c96ac-3f60-4ac3-8b8a-0d0cb8acb9dd'),
      'Plugins/MusicSuite/Users/6b6c96ac-3f60-4ac3-8b8a-0d0cb8acb9dd/Settings'
    );
  } finally {
    dom.window.close();
  }
});

test('generated card markup is keyboard reachable, lazy and escaped', async () => {
  const { dom, lib } = await loadLib();
  try {
    const html = lib.albumCardHtml(
      {
        Id: '6b6c96ac-3f60-4ac3-8b8a-0d0cb8acb9dd',
        Name: '<script>alert(1)</script>',
        AlbumArtist: 'Miles Davis',
        ProductionYear: 1959,
        HasPrimaryImage: true
      },
      'Items/x/Images/Primary'
    );
    assert.match(html, /role="button"/);
    assert.match(html, /tabindex="0"/);
    assert.match(html, /loading="lazy"/);
    assert.match(html, /aria-label="Album/);
    assert.doesNotMatch(html, /<script>/);
    assert.match(html, /data-album-id="6b6c96ac-3f60-4ac3-8b8a-0d0cb8acb9dd"/);

    const doc = new dom.window.DOMParser().parseFromString(html, 'text/html');
    const card = doc.querySelector('.ms-album-card');
    assert.equal(card?.getAttribute('tabindex'), '0');
    assert.equal(card?.querySelector('img')?.getAttribute('loading'), 'lazy');
    assert.ok((card?.querySelector('img')?.getAttribute('alt') || '').length > 0);

    const missing = lib.albumCardHtml({ Id: 'y', Name: 'No cover', HasPrimaryImage: false }, '');
    assert.match(missing, /data-missing-cover="true"/);
    assert.doesNotMatch(missing, /<img/);
  } finally {
    dom.window.close();
  }
});
