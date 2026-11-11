import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { chromium } from 'playwright-core';

const read = path => readFileSync(new URL(`../../SharpMUSH.Client/${path}`, import.meta.url), 'utf8');
const tokens = read('wwwroot/css/tokens.css');

test('mobile bottom navigation contains its labels at 200 percent text size', async () => {
    const browser = await chromium.launch();
    try {
        const page = await browser.newPage({ viewport: { width: 320, height: 900 } });
        await page.setContent(`<style>${tokens} html {font-size:32px;} * {box-sizing:border-box;} .mud-icon-root {width:1em;height:1em;flex-shrink:0;fill:currentColor;}
            ${read('wwwroot/css/shell.css')}</style>
            <div class="phosphor-shell phosphor-shell--bottomnav"><nav class="phosphor-bottomnav">
            ${['Home', 'Play', 'Wiki', 'Characters', 'Menu'].map(name => `<button class="phosphor-bottomtab"><svg class="mud-icon-root phosphor-bottomtab-icon" viewBox="0 0 24 24"><path d="M0 0h24v24H0z" /></svg><span class="phosphor-bottomtab-label">${name}</span></button>`).join('')}
            </nav></div>`);
        const bounds = await page.locator('.phosphor-bottomnav').evaluate(nav => ({
            bar: nav.getBoundingClientRect().toJSON(),
            labels: [...nav.querySelectorAll('.phosphor-bottomtab-label')].map(el => { const range = document.createRange(); range.selectNodeContents(el); return range.getBoundingClientRect().toJSON(); })
        }));
        assert.ok(bounds.labels.every(label => label.top >= bounds.bar.top && label.bottom <= bounds.bar.bottom), 'enlarged labels remain inside the visible navigation');
        if (process.env.SHELL_AUDIT_OUTPUT) await page.screenshot({ path: `${process.env.SHELL_AUDIT_OUTPUT}/navigation-200.png` });
    } finally {
        await browser.close();
    }
});

test('theme radio groups use one tab stop and arrow keys select and wrap', async () => {
    const browser = await chromium.launch();
    try {
        const page = await browser.newPage();
        await page.setContent(`<style>${tokens}</style><div id="settings"><div role="radiogroup">
            <button role="radio" aria-checked="true">Phosphor</button>
            <button role="radio" aria-checked="false" disabled>Unavailable</button>
            <button role="radio" aria-checked="false">Daylight</button>
            </div><button id="after">After</button></div>`);
        await page.addScriptTag({ content: read('wwwroot/js/layout.js') });
        await page.evaluate(() => {
            const root = document.querySelector('#settings');
            root.addEventListener('click', event => {
                if (event.target.getAttribute('role') !== 'radio') return;
                for (const radio of root.querySelectorAll('[role=radio]')) radio.setAttribute('aria-checked', String(radio === event.target));
                window.sharpmushLayout.radioKeys(root);
            });
            window.sharpmushLayout.radioKeys(root);
        });
        await page.keyboard.press('Tab');
        assert.equal(await page.evaluate(() => document.activeElement.textContent), 'Phosphor');
        await page.keyboard.press('ArrowRight');
        assert.equal(await page.evaluate(() => document.activeElement.textContent), 'Daylight');
        assert.equal(await page.getByRole('radio', { name: 'Daylight' }).getAttribute('aria-checked'), 'true');
        await page.keyboard.press('ArrowRight');
        assert.equal(await page.evaluate(() => document.activeElement.textContent), 'Phosphor');
        await page.keyboard.press('ArrowLeft');
        assert.equal(await page.evaluate(() => document.activeElement.textContent), 'Daylight');
        await page.keyboard.press('Tab');
        assert.equal(await page.evaluate(() => document.activeElement.id), 'after');
    } finally {
        await browser.close();
    }
});

test('phone and widget schema previews reflow independently of the desktop page', async () => {
    const browser = await chromium.launch();
    try {
        const page = await browser.newPage({ viewport: { width: 1440, height: 900 } });
        // Remove Blazor's source-only deep combinator, retaining the actual responsive rules.
        const css = [read('Pages/AppBuilder.razor.css'), read('Components/Schema/SchemaViewRenderer.razor.css')]
            .join('\n').replaceAll('::deep', '');
        const grid = `<div class="schema-view"><div class="schema-grid schema-grid--cols" style="--schema-cols:3">
            <div class="schema-cell" style="--schema-span:2">Character name</div>
            <div class="schema-cell">Description</div><div class="schema-cell">Status</div></div></div>`;
        await page.setContent(`<style>${tokens} body { margin:0; } * { box-sizing:border-box; }
            .desktop-page { container:page / inline-size; width:1200px; } ${css}</style>
            <main class="desktop-page"><div class="bld-preview-frame">${grid}</div></main>`);
        const frame = page.locator('.bld-preview-frame');
        const columns = () => page.locator('.schema-grid').evaluate(el => getComputedStyle(el).gridTemplateColumns.split(' ').length);
        assert.equal(await columns(), 3, 'desktop preview keeps declared columns');
        for (const mode of ['phone', 'widget']) {
            await frame.evaluate((el, mode) => { el.className = `bld-preview-frame bld-preview-frame--${mode}`; }, mode);
            assert.equal(await columns(), 1, `${mode} preview must query its own width instead of the desktop page`);
            assert.equal(await page.locator('.schema-cell').first().evaluate(el => getComputedStyle(el).gridColumn), 'auto', 'column spans reset in compact preview');
        }
    } finally {
        await browser.close();
    }
});

test('boot error stays hidden until failure and its dismissal is keyboard operable', async () => {
    const browser = await chromium.launch();
    try {
        const page = await browser.newPage({ viewport: { width: 320, height: 568 } });
        const error = read('wwwroot/index.html').match(/<div id="blazor-error-ui"[\s\S]*?<\/div>/)[0];
        await page.setContent(`<style>${tokens}${read('wwwroot/css/globals.css')}</style>${error}`);
        assert.equal(await page.locator('#blazor-error-ui').isVisible(), false);
        // Blazor's existing hook reveals the bar and attaches dismissal to .dismiss.
        await page.evaluate(() => {
            const bar = document.querySelector('#blazor-error-ui');
            bar.style.display = 'block';
            bar.querySelector('.dismiss').addEventListener('click', () => { bar.style.display = 'none'; });
        });
        await page.keyboard.press('Tab');
        assert.equal(await page.locator('.reload').evaluate(el => el === document.activeElement), true);
        await page.keyboard.press('Tab');
        assert.equal(await page.getByRole('button', { name: 'Dismiss error' }).evaluate(el => el === document.activeElement), true);
        const target = await page.locator('.dismiss').boundingBox();
        assert.ok(target.width >= 44 && target.height >= 44);
        await page.keyboard.press('Enter');
        assert.equal(await page.locator('#blazor-error-ui').isVisible(), false);
    } finally {
        await browser.close();
    }
});

test('shared card header wraps long titles and controls at narrow widths with enlarged text', async () => {
    const browser = await chromium.launch();
    try {
        const page = await browser.newPage({ viewport: { width: 320, height: 568 } });
        await page.setContent(`<style>${tokens}
            html { font-size: 32px; } body { margin: 16px; font-family: var(--font-ui); } * { box-sizing: border-box; }
            button { font: inherit; } ${read('Components/Kit/KitCard.razor.css')}
            </style><section class="kit-card"><header class="kit-card-head">
            <div class="kit-card-titles"><div class="kit-card-title">Averylongunbrokencommunitytitle</div>
            <div class="kit-card-sub">A longer translated description of this collection</div></div>
            <div class="kit-card-controls"><button>Previous</button><button>Next</button></div>
            </header></section>`);
        const extent = await page.evaluate(() => ({ scroll: document.documentElement.scrollWidth, width: innerWidth }));
        assert.ok(extent.scroll <= extent.width, 'shared card must not cause horizontal page overflow');
        assert.equal(await page.locator('.kit-card-title').evaluate(el => getComputedStyle(el).fontSize), '30px', 'title uses the real rem-based text-base token at 200 percent');
        assert.equal(await page.locator('.kit-card-sub').evaluate(el => getComputedStyle(el).fontSize), '26px', 'subtitle uses the real rem-based text-sm token at 200 percent');
        if (process.env.SHELL_AUDIT_OUTPUT) await page.screenshot({ path: `${process.env.SHELL_AUDIT_OUTPUT}/card-200.png` });
        const controls = await page.locator('button').all();
        for (const button of controls) assert.ok(await button.isVisible());
    } finally {
        await browser.close();
    }
});
