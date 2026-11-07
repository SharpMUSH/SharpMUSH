import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { chromium } from 'playwright-core';
const tokens = readFileSync(new URL('../../SharpMUSH.Client/wwwroot/css/tokens.css', import.meta.url), 'utf8');
const css = readFileSync(new URL('../../SharpMUSH.Client/Components/Map/MapCanvas.razor.css', import.meta.url), 'utf8');

test('stored terrain labels stay inside small hex cells and retain accessible names', async () => {
    const browser = await chromium.launch();
    try {
        const page = await browser.newPage();
        await page.setContent(`<style>${tokens} *{box-sizing:border-box} :root{--map-zoom:.3} ${css}</style>
            <button class="map-room map-room--place" style="width:30.6px;height:26.5px" aria-label="Harbour lookout" title="Harbour lookout"><span class="map-room-icon" aria-hidden="true">♣</span><span class="map-room-name">Harbour lookout</span></button>`);
        const bounds = await page.locator('button').evaluate(button => {
            const label = button.querySelector('.map-room-name');
            const b = button.getBoundingClientRect(), l = label.getBoundingClientRect();
            const style = getComputedStyle(button);
            return { contained: l.left >= b.left + parseFloat(style.paddingLeft)
                    && l.right <= b.right - parseFloat(style.paddingRight)
                    && l.top >= b.top + parseFloat(style.paddingTop)
                    && l.bottom <= b.bottom - parseFloat(style.paddingBottom),
                ellipsis: getComputedStyle(label).textOverflow, whiteSpace: getComputedStyle(label).whiteSpace };
        });
        assert.equal(bounds.contained, true, 'the label must not clip at either side or vertically');
        assert.equal(bounds.ellipsis, 'ellipsis');
        assert.equal(await page.locator('.map-room-icon').isVisible(), false, 'the icon must not squeeze out the only text line');
        assert.equal(bounds.whiteSpace, 'nowrap');
        assert.equal(await page.getByRole('button', { name: 'Harbour lookout' }).getAttribute('title'), 'Harbour lookout');
        await page.locator('button').evaluate(button => { button.style.width = '100px'; button.style.height = '90px'; });
        assert.equal(await page.locator('.map-room-name').evaluate(e => getComputedStyle(e).whiteSpace), 'normal', 'larger cells keep multiline labels');
    } finally { await browser.close(); }
});
