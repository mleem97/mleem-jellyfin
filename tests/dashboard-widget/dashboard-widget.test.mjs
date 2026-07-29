import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import { JSDOM } from 'jsdom';

const currentDirectory = path.dirname(fileURLToPath(import.meta.url));
const widgetPath = path.resolve(
  currentDirectory,
  '../../plugins/HddDisplay/Web/dashboard-widget.js'
);

function delay(milliseconds) {
  return new Promise((resolve) => setTimeout(resolve, milliseconds));
}

test('dashboard widget injects once and cleans up on route exit', async () => {
  const source = await readFile(widgetPath, 'utf8');
  const dom = new JSDOM(
    `<!doctype html>
     <html>
       <body>
         <div class="dashboardPage">
           <div data-role="content">
             <section class="dashboardSection" id="paths-card">
               <h2 class="sectionTitle">Paths</h2>
             </section>
           </div>
         </div>
       </body>
     </html>`,
    {
      pretendToBeVisual: true,
      runScripts: 'outside-only',
      url: 'http://localhost/web/#/dashboard'
    }
  );

  dom.window.ApiClient = {
    getUrl: (url) => `/${url}`,
    getJSON: async () => ({
      drives: [],
      usage: { cacheHit: false },
      gpu: { isAvailable: false, diagnostic: 'test' }
    })
  };

  try {
    dom.window.eval(source);
    dom.window.document.dispatchEvent(new dom.window.Event('DOMContentLoaded'));
    await delay(30);

    const widget = dom.window.document.querySelector('#hdd-display-dashboard-widget');
    assert.ok(widget, 'widget should be injected on the dashboard route');
    assert.equal(
      widget.previousElementSibling?.id,
      'paths-card',
      'widget should be placed directly after the Paths card'
    );

    dom.window.eval(source);
    await delay(10);
    assert.equal(
      dom.window.document.querySelectorAll('#hdd-display-dashboard-widget').length,
      1,
      'loading the asset twice must not duplicate the widget'
    );

    dom.window.location.hash = '#/home';
    dom.window.dispatchEvent(new dom.window.HashChangeEvent('hashchange'));
    await delay(20);
    assert.equal(
      dom.window.document.querySelector('#hdd-display-dashboard-widget'),
      null,
      'widget should be removed after leaving the dashboard route'
    );
  } finally {
    dom.window.close();
  }
});
