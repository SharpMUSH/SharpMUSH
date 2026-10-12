// Offline browser proof: intercepted HTTPS requests only; no listener, server or game connection.
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { createRequire } from 'node:module';
import { resolve } from 'node:path';
const require = createRequire(import.meta.url);
const { chromium } = require(process.env.PLAYWRIGHT_MODULE || 'playwright');
const root = resolve(import.meta.dirname, '../..');
const packageRoot = resolve(root, 'examples/packages/builder-graph');
const browser = await chromium.launch({ headless: true });
try {
    const page = await browser.newPage({ viewport: { width: 1300, height: 1000 } });
    const errors = [];
    let finishDelayedImage;
    const delayedImage = new Promise(resolve => { finishDelayedImage = resolve; });
    page.on('pageerror', error => errors.push(error.message));
    await page.route('**/*', async route => {
        const path = new URL(route.request().url()).pathname;
        if (path === '/replacement' || path === '/pre-first-replacement') return route.fulfill({ contentType: 'text/html', body: '<!doctype html><title>Replacement</title><div id="ready">Replacement document</div><script>window.received=[];addEventListener("message",e=>received.push(e.data))</script>' + (path === '/replacement' ? '<img src="/delayed-image">' : '') });
        if (path === '/delayed-image') { await delayedImage; return route.fulfill({ contentType: 'image/svg+xml', body: '<svg xmlns="http://www.w3.org/2000/svg" width="1" height="1"/>' }); }
        if (path === '/host') return route.fulfill({ contentType: 'text/html', body: '<!doctype html><title>Independent graph instances</title><style>body{margin:0;background:#101b28}iframe{width:100%;height:490px;border:0}</style><iframe id="east" title="East" sandbox="allow-scripts"></iframe><iframe id="west" title="West" sandbox="allow-scripts"></iframe>' });
        const file = path === '/hosted-app.mjs' ? resolve(root, 'SharpMUSH.Client/wwwroot/js/hosted-app.mjs') : resolve(packageRoot, path.slice(path.lastIndexOf('/') + 1));
        const type = file.endsWith('.html') ? 'text/html' : file.endsWith('.css') ? 'text/css' : 'text/javascript';
        return route.fulfill({ contentType: type, body: await readFile(file) });
    });
    await page.goto('https://builder-test.invalid/host');
    await page.evaluate(async () => {
        const { mount } = await import('/hosted-app.mjs');
        const html = await (await fetch('/assets/index.html')).text();
        const eastGraph = {
            rooms: [{ dbref: '#10', name: 'Gate' }, { dbref: '#20', name: 'Gate' }, { dbref: '#30', name: 'Disconnected' }],
            exits: [
                { dbref: '#11', name: 'one way', source: '#10', destination: '#20', status: 'linked' },
                { dbref: '#12', name: 'parallel', source: '#10', destination: '#20', status: 'linked' },
                { dbref: '#13', name: 'loop', source: '#20', destination: '#20', status: 'linked' },
                { dbref: '#14', name: 'generated', source: '#10', destination: null, status: 'dynamic' }
            ], truncated: false
        };
        const westGraph = { rooms: [{ dbref: '#40', name: 'West only' }], exits: [], truncated: false };
        window.readCounts = { east: 0, west: 0 };
        window.mounts = [mount('east', { instanceId: 'east', configVersion: 1, config: { title: 'East workshop', rooms: [10, 20, 30] }, entryUrl: new URL('/assets/index.html', location.href).href, html }, { invokeMethodAsync: async () => { window.readCounts.east++; if (window.holdResponse) await new Promise(resolve => { window.finishResponse = resolve; }); return eastGraph; } }),
            mount('west', { instanceId: 'west', configVersion: 1, config: { title: 'West workshop', rooms: [40] }, entryUrl: new URL('/assets/index.html', location.href).href, html }, { invokeMethodAsync: async () => { window.readCounts.west++; return westGraph; } })];
    });
    const east = page.frameLocator('#east'), west = page.frameLocator('#west');
    await east.locator('.node').first().waitFor();
    await west.locator('.node').first().waitFor();
    assert.equal(await east.locator('#title').textContent(), 'East workshop');
    assert.equal(await west.locator('#title').textContent(), 'West workshop');
    assert.equal(await east.locator('.node').count(), 3);
    assert.equal(await east.locator('.edge').count(), 3);
    assert.equal(await west.locator('.node').count(), 1);
    await east.locator('.node[data-id="#10"]').click();
    assert.match(await east.locator('#exits').textContent(), /generated.*dynamic/);
    assert.deepEqual(errors, []);
    await page.screenshot({ path: '/tmp/builder-host-proof.png', fullPage: true });
    // Commit a replacement document while its load event is held back, then finish an old read.
    // WindowProxy-based replies would reach the replacement even before navigation's load event.
    await page.evaluate(() => { window.holdResponse = true; });
    await east.locator('#refresh').click();
    await page.waitForFunction(() => window.finishResponse);
    await page.evaluate(() => { document.getElementById('east').removeAttribute('srcdoc'); document.getElementById('east').src = '/replacement'; });
    await east.locator('#ready').waitFor();
    await page.evaluate(() => window.finishResponse());
    await page.waitForTimeout(50);
    assert.equal(await east.locator('#ready').evaluate(() => window.received.length), 0);
    finishDelayedImage();
    await page.waitForTimeout(50);
    assert.equal(await east.locator('#ready').evaluate(() => window.received.length), 0);
    assert.equal(await page.evaluate(() => window.readCounts.east), 2);
    await page.evaluate(async () => {
        const { mount } = await import('/hosted-app.mjs');
        const frame = document.createElement('iframe'); frame.id = 'preload'; frame.sandbox = 'allow-scripts'; document.body.append(frame);
        window.preloadReads = 0;
        window.preloadMount = mount('preload', { instanceId: 'preload', config: { rooms: [10] }, configVersion: 1,
            entryUrl: new URL('/assets/index.html', location.href).href,
            html: '<!doctype html><html><head><script>location.href="https://builder-test.invalid/pre-first-replacement"</script></head><body>Redirecting before load</body></html>' },
            { invokeMethodAsync: async () => { window.preloadReads++; return {}; } });
    });
    const preload = page.frameLocator('#preload');
    await preload.locator('#ready').waitFor();
    await page.waitForTimeout(50);
    assert.equal(await preload.locator('#ready').evaluate(() => window.received.length), 0);
    assert.equal(await page.evaluate(() => window.preloadReads), 0);
    console.log('PASS: two opaque-origin frames, independent configs/graphs, directed parallel links, self-loop, disconnected room and unresolved dynamic exit; no browser errors; replacement documents receive no config or pending reply, including a redirect before first load.');
} finally { await browser.close(); }
