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
const PARENT_ID = '6b6c96ac-3f60-4ac3-8b8a-0d0cb8acb9dd';
const USER_ID = '11111111-1111-4111-8111-111111111111';

function delay(milliseconds) {
  return new Promise((resolve) => setTimeout(resolve, milliseconds));
}

function makeItem(index, name) {
  return {
    Id: `00000000-0000-4000-8000-${String(index).padStart(12, '0')}`,
    Name: name ?? `Album ${index}`,
    AlbumArtist: `Artist ${index % 7}`,
    ProductionYear: 1970 + (index % 50),
    HasPrimaryImage: index % 4 !== 3
  };
}

// Minimal fake backend speaking the real MusicSuite routes.
function createServer(options = {}) {
  const total = options.total ?? 120;
  const batchOverlap = options.batchOverlap ?? 0;
  const state = {
    calls: [],
    ajaxCalls: [],
    failFirstPage: options.failFirstPage ?? false,
    failLaterPages: options.failLaterPages ?? false,
    canCustomize: options.canCustomize ?? true,
    settingsTileSize: options.settingsTileSize ?? 'Medium',
    searchResults: options.searchResults ?? null
  };

  async function handle(url) {
    state.calls.push(url);
    if (url.includes('Albums/Context')) {
      return {
        enabled: true,
        isMusicLibrary: true,
        batchSize: 50,
        searchDebounceMs: 100
      };
    }
    if (url.includes('/Settings')) {
      return {
        CanCustomize: state.canCustomize,
        Settings: { TileSize: state.settingsTileSize }
      };
    }
    if (url.includes('Plugins/MusicSuite/Albums')) {
      const query = url.split('?')[1] ?? '';
      const params = new URLSearchParams(query);
      // The frontend must page with the backend's StartIndex/Limit names.
      assert.ok(params.has('StartIndex'), 'StartIndex must be sent');
      assert.ok(params.has('Limit'), 'Limit must be sent');
      const start = Number(params.get('StartIndex'));
      const limit = Number(params.get('Limit'));
      if (state.failFirstPage && start === 0) throw new Error('backend down');
      if (state.failLaterPages && start > 0) throw new Error('page failed');
      const search = params.get('SearchTerm') ?? '';
      if (state.searchResults && search in state.searchResults) {
        const items = state.searchResults[search];
        return {
          Items: items,
          NextStartIndex: null,
          TotalRecordCount: items.length,
          HasMore: false
        };
      }
      const effectiveStart = Math.max(0, start - (start > 0 ? batchOverlap : 0));
      const items = [];
      for (let i = effectiveStart; i < Math.min(effectiveStart + limit, total); i += 1) {
        items.push(makeItem(i));
      }
      const next = start + limit;
      return {
        Items: items,
        NextStartIndex: next < total ? next : null,
        TotalRecordCount: total,
        HasMore: next < total
      };
    }
    throw new Error(`unexpected url ${url}`);
  }

  return { state, handle };
}

async function createHarness(server, hash = '#/home') {
  const source = await readFile(assetPath, 'utf8');
  const dom = new JSDOM('<!doctype html><html><head></head><body></body></html>', {
    pretendToBeVisual: true,
    runScripts: 'outside-only',
    url: `http://localhost/web/${hash}`
  });
  const observers = [];
  dom.window.IntersectionObserver = class {
    constructor(callback) {
      this.callback = callback;
      observers.push(this);
    }

    observe() {}

    disconnect() {}
  };
  const scrolled = [];
  dom.window.scrollTo = (x, y) => scrolled.push([x, y]);
  dom.window.ApiClient = {
    getUrl: (url) => `/${url}`,
    getJSON: (url) => server.handle(url),
    getCurrentUserId: () => USER_ID,
    ajax: (ajaxOptions) => {
      server.state.ajaxCalls.push(ajaxOptions);
      if (ajaxOptions.type === 'DELETE') return Promise.resolve({});
      return Promise.resolve(null);
    }
  };
  dom.window.eval(source);
  assert.ok(dom.window.MusicSuiteAlbumsRenderer, 'renderer must be exposed');
  assert.ok(dom.window.MusicSuiteAlbumsLib, 'lib must be exposed');
  return { dom, observers, scrolled };
}

function mountArgs(server, routeKey = 'test-route|parent') {
  return {
    context: { enabled: true, isMusicLibrary: true, batchSize: 50, searchDebounceMs: 100 },
    helpers: {
      apiGet: (url) => server.handle(url),
      parentId: PARENT_ID,
      routeKey
    }
  };
}

function intersect(harness) {
  for (const observer of harness.observers) {
    observer.callback([{ isIntersecting: true }]);
  }
}

test('mount renders toolbar, lazy grid and status without visible pagination', async () => {
  const server = createServer({ total: 120 });
  const harness = await createHarness(server);
  try {
    const { context, helpers } = mountArgs(server);
    const container = harness.dom.window.document.createElement('section');
    harness.dom.window.document.body.appendChild(container);
    const mounted = await harness.dom.window.MusicSuiteAlbumsRenderer.mount(
      container, context, helpers
    );
    assert.equal(mounted, true);
    assert.ok(container.querySelector('.ms-albums-toolbar[role="search"]'));
    assert.ok(container.querySelector('.ms-albums-grid[role="list"]'));
    assert.ok(container.querySelector('.ms-albums-sentinel'));
    assert.ok(container.querySelector('.ms-albums-status[role="status"]'));
    const cards = container.querySelectorAll('.ms-album-card');
    assert.equal(cards.length, 50);
    for (const card of cards) {
      assert.equal(card.getAttribute('role'), 'button');
      assert.equal(card.getAttribute('tabindex'), '0');
    }
    const images = container.querySelectorAll('.ms-album-card img');
    assert.ok(images.length > 0);
    for (const image of images) {
      assert.equal(image.getAttribute('loading'), 'lazy');
    }
    // No visible pagination controls: endless scroll only.
    assert.equal(container.querySelector('.ms-albums-pages'), null);
    assert.equal(container.querySelector('[data-page]'), null);
  } finally {
    harness.dom.window.close();
  }
});

test('endless scroll appends pages, guards in-flight requests and dedupes', async () => {
  const server = createServer({ total: 120, batchOverlap: 5 });
  const harness = await createHarness(server);
  try {
    const { context, helpers } = mountArgs(server);
    const container = harness.dom.window.document.createElement('section');
    harness.dom.window.document.body.appendChild(container);
    await harness.dom.window.MusicSuiteAlbumsRenderer.mount(container, context, helpers);
    assert.equal(container.querySelectorAll('.ms-album-card').length, 50);

    // Fire the sentinel twice while the request is in flight: one request only.
    const pending = server.state.calls.length;
    intersect(harness);
    intersect(harness);
    await delay(30);
    const secondPageCalls = server.state.calls
      .slice(pending)
      .filter((url) => url.includes('StartIndex=50'));
    assert.equal(secondPageCalls.length, 1);
    await delay(30);
    // 50 + 50 new (5 overlapping ids are deduped).
    assert.equal(container.querySelectorAll('.ms-album-card').length, 95);

    intersect(harness);
    await delay(30);
    // Page 3 covers items 95..119 (25 new): 95 + 25 = 120 unique cards.
    assert.equal(container.querySelectorAll('.ms-album-card').length, 120);
  } finally {
    harness.dom.window.close();
  }
});

test('stale responses lose: only the last sort change is rendered', async () => {
  const server = createServer({ total: 60 });
  const harness = await createHarness(server);
  const pending = [];
  try {
    const { context } = mountArgs(server);
    const helpers = {
      apiGet: (url) => new Promise((resolve, reject) => pending.push({ url, resolve, reject })),
      parentId: PARENT_ID,
      routeKey: 'stale|parent'
    };
    const container = harness.dom.window.document.createElement('section');
    harness.dom.window.document.body.appendChild(container);
    const mounting = harness.dom.window.MusicSuiteAlbumsRenderer.mount(container, context, helpers);
    await delay(10);
    // Request 0 is the settings lookup, request 1 the first album page.
    assert.equal(pending.length, 2);

    // Settings resolve first (admin defaults stay active).
    pending[0].resolve({ CanCustomize: true, Settings: { TileSize: 'Medium' } });
    // First page arrives: grid fills.
    pending[1].resolve({ Items: [makeItem(1, 'First')], NextStartIndex: 1, HasMore: true });
    await mounting;
    assert.equal(container.querySelectorAll('.ms-album-card').length, 1);

    // Two rapid sort changes; the first response is held back.
    const order = container.querySelector('.ms-albums-order');
    order.value = 'Ascending';
    order.dispatchEvent(new harness.dom.window.Event('change', { bubbles: true }));
    await delay(10);
    order.value = 'Descending';
    order.dispatchEvent(new harness.dom.window.Event('change', { bubbles: true }));
    await delay(10);
    assert.equal(pending.length, 4);

    // Resolve out of order: newest first, then the stale one.
    pending[3].resolve({ Items: [makeItem(2, 'Newest')], NextStartIndex: null, HasMore: false });
    await delay(10);
    pending[2].resolve({ Items: [makeItem(3, 'Stale')], NextStartIndex: null, HasMore: false });
    await delay(20);
    const titles = Array.from(container.querySelectorAll('.ms-album-title'))
      .map((node) => node.textContent);
    assert.deepEqual(titles, ['Newest']);
  } finally {
    harness.dom.window.close();
  }
});

test('search is debounced and resets paging', async () => {
  const server = createServer({
    total: 60,
    searchResults: { 'miles davis': [makeItem(7, 'Kind of Blue')] }
  });
  const harness = await createHarness(server);
  try {
    const { context, helpers } = mountArgs(server);
    const container = harness.dom.window.document.createElement('section');
    harness.dom.window.document.body.appendChild(container);
    await harness.dom.window.MusicSuiteAlbumsRenderer.mount(container, context, helpers);
    const callsBefore = server.state.calls.length;

    const search = container.querySelector('.ms-albums-search');
    search.value = 'miles';
    search.dispatchEvent(new harness.dom.window.Event('input', { bubbles: true }));
    await delay(30);
    search.value = 'miles davis';
    search.dispatchEvent(new harness.dom.window.Event('input', { bubbles: true }));
    await delay(250);

    const searches = server.state.calls
      .slice(callsBefore)
      .filter((url) => url.includes('SearchTerm='));
    assert.equal(searches.length, 1);
    assert.ok(searches[0].includes('SearchTerm=miles+davis'));
    assert.ok(searches[0].includes('StartIndex=0'));
    const titles = Array.from(container.querySelectorAll('.ms-album-title'))
      .map((node) => node.textContent);
    assert.deepEqual(titles, ['Kind of Blue']);
  } finally {
    harness.dom.window.close();
  }
});

test('page errors show a retry button; first-page errors fail open', async () => {
  const server = createServer({ total: 120, failLaterPages: true });
  const harness = await createHarness(server);
  try {
    const { context, helpers } = mountArgs(server);
    const container = harness.dom.window.document.createElement('section');
    harness.dom.window.document.body.appendChild(container);
    await harness.dom.window.MusicSuiteAlbumsRenderer.mount(container, context, helpers);
    assert.equal(container.querySelectorAll('.ms-album-card').length, 50);

    intersect(harness);
    await delay(30);
    const retry = container.querySelector('.ms-albums-retry');
    assert.ok(retry, 'later pages must offer a retry button');
    // Loaded items stay visible; nothing is replaced by an error page.
    assert.equal(container.querySelectorAll('.ms-album-card').length, 50);

    server.state.failLaterPages = false;
    retry.dispatchEvent(new harness.dom.window.Event('click', { bubbles: true }));
    await delay(30);
    assert.equal(container.querySelector('.ms-albums-retry'), null);
    assert.ok(container.querySelectorAll('.ms-album-card').length > 50);
  } finally {
    harness.dom.window.close();
  }

  const failing = createServer({ failFirstPage: true });
  const failingHarness = await createHarness(failing);
  try {
    const { context, helpers } = mountArgs(failing);
    const container = failingHarness.dom.window.document.createElement('section');
    failingHarness.dom.window.document.body.appendChild(container);
    const mounted = await failingHarness.dom.window.MusicSuiteAlbumsRenderer.mount(
      container, context, helpers
    );
    assert.equal(mounted, false);
  } finally {
    failingHarness.dom.window.close();
  }
});

test('tile sizes apply instantly and persist through the settings API', async () => {
  const server = createServer({});
  const harness = await createHarness(server);
  try {
    const { context, helpers } = mountArgs(server);
    const container = harness.dom.window.document.createElement('section');
    harness.dom.window.document.body.appendChild(container);
    await harness.dom.window.MusicSuiteAlbumsRenderer.mount(container, context, helpers);
    const grid = container.querySelector('.ms-albums-grid');
    assert.equal(grid.getAttribute('data-tile'), 'Medium');

    const large = container.querySelector('[data-tile="Large"]');
    large.dispatchEvent(new harness.dom.window.Event('click', { bubbles: true }));
    await delay(10);
    assert.equal(grid.getAttribute('data-tile'), 'Large');
    assert.equal(large.getAttribute('aria-pressed'), 'true');
    const puts = server.state.ajaxCalls.filter((call) => call.type === 'PUT');
    assert.equal(puts.length, 1);
    assert.ok(puts[0].url.includes(`/Users/${USER_ID}/Settings`));
    assert.ok(String(puts[0].data).includes('Large'));

    const reset = container.querySelector('.ms-albums-reset');
    reset.dispatchEvent(new harness.dom.window.Event('click', { bubbles: true }));
    await delay(10);
    assert.ok(server.state.ajaxCalls.some((call) => call.type === 'DELETE'));
  } finally {
    harness.dom.window.close();
  }
});

test('disabled customization locks the layout UI but keeps admin defaults', async () => {
  const server = createServer({ canCustomize: false, settingsTileSize: 'Small' });
  const harness = await createHarness(server);
  try {
    const { context, helpers } = mountArgs(server);
    const container = harness.dom.window.document.createElement('section');
    harness.dom.window.document.body.appendChild(container);
    await harness.dom.window.MusicSuiteAlbumsRenderer.mount(container, context, helpers);
    // Admin default is applied immediately on load.
    assert.equal(container.querySelector('.ms-albums-grid').getAttribute('data-tile'), 'Small');
    const tileButtons = Array.from(container.querySelectorAll('.ms-albums-tiles [data-tile]'));
    assert.ok(tileButtons.length > 0);
    for (const button of tileButtons) {
      assert.equal(button.disabled, true);
    }
    assert.equal(container.querySelector('.ms-albums-reset').disabled, true);
    assert.equal(
      server.state.ajaxCalls.filter((call) => call.type === 'PUT').length,
      0
    );
  } finally {
    harness.dom.window.close();
  }
});

test('scroll position restores from sessionStorage on mount', async () => {
  const server = createServer({ total: 60 });
  const harness = await createHarness(server);
  try {
    const lib = harness.dom.window.MusicSuiteAlbumsLib;
    harness.dom.window.sessionStorage.setItem(lib.scrollKey('restore|parent'), '321');
    const { context } = mountArgs(server, 'restore|parent');
    const helpers = {
      apiGet: (url) => server.handle(url),
      parentId: PARENT_ID,
      routeKey: 'restore|parent'
    };
    const container = harness.dom.window.document.createElement('section');
    harness.dom.window.document.body.appendChild(container);
    await harness.dom.window.MusicSuiteAlbumsRenderer.mount(container, context, helpers);
    await delay(50);
    assert.ok(
      harness.scrolled.some(([, y]) => y === 321),
      'saved scroll position must be restored'
    );
  } finally {
    harness.dom.window.close();
  }
});

test('cards are keyboard operable and open the album details', async () => {
  const server = createServer({ total: 60 });
  const harness = await createHarness(server);
  try {
    const { context, helpers } = mountArgs(server);
    const container = harness.dom.window.document.createElement('section');
    harness.dom.window.document.body.appendChild(container);
    await harness.dom.window.MusicSuiteAlbumsRenderer.mount(container, context, helpers);
    const card = container.querySelector('.ms-album-card');
    assert.ok(card);
    card.dispatchEvent(
      new harness.dom.window.KeyboardEvent('keydown', { key: 'Enter', bubbles: true })
    );
    assert.match(harness.dom.window.location.hash, /#\/details\?id=/);
  } finally {
    harness.dom.window.close();
  }
});

test('failed context validation keeps the native view (fail-open)', async () => {
  const source = await readFile(assetPath, 'utf8');
  const dom = new JSDOM(
    `<!doctype html>
     <html>
       <body>
         <div data-role="page" class="page">
           <div data-role="content">
             <div class="itemsContainer"><span>native albums</span></div>
           </div>
         </div>
       </body>
     </html>`,
    {
      pretendToBeVisual: true,
      runScripts: 'outside-only',
      url: `http://localhost/web/#/music/albums?topParentId=${PARENT_ID}`
    }
  );
  dom.window.ApiClient = {
    getUrl: (url) => `/${url}`,
    getJSON: async (url) => {
      if (String(url).includes('Albums/Context')) throw new Error('backend down');
      return {};
    }
  };
  try {
    dom.window.eval(source);
    dom.window.document.dispatchEvent(new dom.window.Event('DOMContentLoaded'));
    await delay(150);
    assert.equal(
      dom.window.document.querySelector('#better-music-display-albums'),
      null,
      'no container may be injected when validation fails'
    );
    const native = dom.window.document.querySelector('.itemsContainer');
    assert.ok(native, 'native view must remain');
    assert.notEqual(native.style.display, 'none');
    assert.equal(native.getAttribute('aria-hidden'), null);
  } finally {
    dom.window.close();
  }
});
