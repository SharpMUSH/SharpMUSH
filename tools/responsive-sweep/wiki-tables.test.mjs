import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { chromium } from 'playwright-core';

const fixture = readFileSync(new URL('./fixtures/wiki-longsword.html', import.meta.url), 'utf8');
const shell = readFileSync(new URL('../../SharpMUSH.Client/wwwroot/css/shell.css', import.meta.url), 'utf8');
const render = readFileSync(new URL('../../SharpMUSH.Client/wwwroot/js/wiki-render.js', import.meta.url), 'utf8');

test('rendered wiki equipment tables reflow, retain semantics and scroll by keyboard', async () => {
    const browser = await chromium.launch({ executablePath: process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE });
    try {
        const page = await browser.newPage({ viewport: { width: 320, height: 900 } });
        const article = `<style>${shell}
body { margin:0; font:15px Arial,sans-serif; }
main { padding:20px 28px; max-width:640px; margin:auto; }
</style><main class="WikiContent"><div class="mud-card-content">${fixture}</div></main>`;
        await page.setContent(article);
        const original = await page.locator('table').evaluate(table => table.outerHTML);
        await page.evaluate(() => document.querySelectorAll('.wiki-table-scroll').forEach(w => w.replaceWith(...w.childNodes)));
        assert.ok(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth), 'fixture reproduces the original viewport overflow');
        await page.setContent(article);
        await page.addScriptTag({ content: render });
        await page.evaluate(async () => {
            window.SharpMUSH.WikiRender.init();
            await window.SharpMUSH.WikiRender.render();
            await window.SharpMUSH.WikiRender.render();
        });
        assert.equal(await page.locator('.wiki-table-scroll').count(), 1, 'repeated renders do not nest wrappers');
        assert.equal(await page.locator('table').evaluate(table => table.outerHTML), original);
        assert.equal(await page.getByRole('table').count(), 1);
        assert.equal(await page.getByRole('columnheader').count(), 6);
        assert.equal(await page.evaluate(() => document.documentElement.scrollWidth), 320);
        await page.keyboard.press('Tab');
        assert.equal(await page.evaluate(() => document.activeElement.className), 'wiki-table-scroll');
        await page.keyboard.press('ArrowRight');
        await page.waitForFunction(() => document.querySelector('.wiki-table-scroll').scrollLeft > 0);
        await page.setViewportSize({ width: 1440, height: 900 });
        assert.equal(await page.evaluate(() => document.documentElement.scrollWidth), 1440);
        const dimensions = await page.locator('.wiki-table-scroll').evaluate(container => ({ client: container.clientWidth, scroll: container.scrollWidth }));
        assert.ok(dimensions.scroll <= dimensions.client + 1, 'desktop table needs no horizontal scrolling');
    } finally {
        await browser.close();
    }
});

// Exercise the component's source rules without Blazor's generated scope selector; the
// actual scoped component lifecycle is a separate .NET concern.
const readmeCss = readFileSync(new URL('../../SharpMUSH.Client/Components/Admin/PackageReadmeDialog.razor.css', import.meta.url), 'utf8').replaceAll('::deep', '');

test('package README table wrappers own keyboard scrolling and a visible focus indicator', async () => {
    const browser = await chromium.launch({ executablePath: process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE });
    try {
        const page = await browser.newPage({ viewport: { width: 320, height: 900 } });
        await page.setContent(`<style>${shell}
${readmeCss}
body { margin:0; font:15px Arial,sans-serif; --accent:#008866; }
main { padding:20px 28px; }
/* Force a wide README table independently of this article's wrapping text. */
.package-readme table { min-width:640px; }
</style><main><div class="package-readme">${fixture}</div></main>`);
        const wrapper = page.locator('.wiki-table-scroll');
        assert.equal(await wrapper.evaluate(element => getComputedStyle(element).overflowX), 'auto');
        assert.equal(await page.evaluate(() => document.documentElement.scrollWidth), 320);
        assert.equal(await page.getByRole('table').count(), 1);
        assert.equal(await page.getByRole('columnheader').count(), 6);
        await page.keyboard.press('Tab');
        assert.equal(await page.evaluate(() => document.activeElement.className), 'wiki-table-scroll');
        assert.equal(await wrapper.evaluate(element => getComputedStyle(element).outlineStyle), 'solid');
        await page.keyboard.press('ArrowRight');
        await page.waitForFunction(() => document.querySelector('.wiki-table-scroll').scrollLeft > 0);
        assert.equal(await page.locator('table').evaluate(element => getComputedStyle(element).display), 'table');
    } finally {
        await browser.close();
    }
});
