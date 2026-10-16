import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { chromium } from 'playwright-core';

// Radio's four-column table, rendered by MarkupString 2.21.0 at 78 cells. Frequency
// and You are nowrap; About grows. Keep the percentage colgroup: dropping it masks
// the inherited word-break bug by changing the table's sizing algorithm.
const radio = readFileSync(new URL('./fixtures/radio-table.html', import.meta.url), 'utf8');
const shell = readFileSync(new URL('../../SharpMUSH.Client/wwwroot/css/shell.css', import.meta.url), 'utf8');
// The relevant LayoutCss.Fixed rules shipped by the same renderer. The portal's
// own CSS is loaded afterward, exactly as index.html loads custom.css.
const layout = `
.ms-layout { display:block; max-width:100%; white-space:normal; }
.ms-text { white-space:pre-wrap; overflow-wrap:anywhere; }
.ms-table-wrap { max-width:100%; overflow-x:auto; }
.ms-table { border-collapse:collapse; }
.ms-table.ms-fill { width:100%; }
.ms-table th, .ms-table td { padding:0 1ch; vertical-align:top; text-align:left; }
.ms-table .ms-nowrap, .ms-table .ms-nowrap .ms-text { white-space:pre; overflow-wrap:normal; }
@media (max-width:48em) { .ms-table.ms-fill > colgroup { display:none; } }
`;

test('radio headers keep whole words while descriptions wrap and narrow tables fit', async () => {
    const browser = await chromium.launch();
    try {
        const page = await browser.newPage({ viewport: { width: 1400, height: 900 } });
        await page.setContent(`<style>
body { font:16px "DejaVu Sans Mono",monospace; margin:0; }
* { box-sizing:border-box; }
${layout}
${shell}
</style><div id="terminal" style="width:78ch;white-space:pre-wrap;word-break:break-word">${radio}</div>`);
        const category = await page.locator('th').nth(1).evaluate(th => {
            const range = document.createRange();
            range.selectNodeContents(th);
            return { text: th.textContent, lines: new Set([...range.getClientRects()].map(r => r.top)).size };
        });
        assert.equal(category.text, 'Category');
        assert.equal(category.lines, 1, 'Category must not split when About has spare width');

        // Still allow a long description to wrap at words.
        await page.locator('tbody td').nth(3).locator('.ms-text').evaluate(el => { el.textContent = 'A public frequency for everyone. '.repeat(12); });
        for (const width of [750, 390]) {
            await page.setViewportSize({ width, height: 900 });
            await page.locator('#terminal').evaluate((el, width) => { el.style.width = `${width}px`; }, width);
            const sizes = await page.locator('.ms-table-wrap').evaluate(wrap => {
                const desc = wrap.querySelector('tbody td:last-child .ms-text');
                const category = wrap.querySelector('th:nth-child(2)');
                const range = document.createRange();
                range.selectNodeContents(category);
                return { client: wrap.clientWidth, scroll: wrap.scrollWidth, descHeight: desc.getBoundingClientRect().height,
                    lines: new Set([...range.getClientRects()].map(r => r.top)).size };
            });
            assert.ok(sizes.scroll <= sizes.client + 1, `table must fit ${width}px`);
            assert.ok(sizes.descHeight > 30, 'long description still wraps');
            assert.equal(sizes.lines, 1, 'Category stays whole on narrow layouts too');
        }
    } finally {
        await browser.close();
    }
});
