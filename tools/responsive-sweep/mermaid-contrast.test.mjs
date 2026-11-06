import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { chromium } from 'playwright-core';

const root = new URL('../../SharpMUSH.Client/', import.meta.url);
const css = readFileSync(new URL('Components/WikiDisplay.razor.css', root), 'utf8').replaceAll('::deep', '');
const mermaid = readFileSync(new URL('wwwroot/js/mermaid.min.js', root), 'utf8');
const renderer = readFileSync(new URL('wwwroot/js/wiki-render.js', root), 'utf8');

for (const background of ['#ffffff', '#111314']) {
    test(`Mermaid labels remain readable over portal background ${background}`, async () => {
        const browser = await chromium.launch();
        try {
            const page = await browser.newPage({ viewport: { width: 320, height: 800 } });
            await page.setContent(`<style>body { margin: 0; background: ${background}; } ${css}</style>
                <div class="WikiContent"><pre class="mermaid">---
 title: Readable title
---
flowchart LR
A[Readable node] --&gt;|Readable edge| B[Another node]</pre></div>`);
            await page.addScriptTag({ content: mermaid });
            await page.evaluate(() => { window.SharpMUSH = { Assets: { ensure: async () => {} } }; });
            await page.addScriptTag({ content: renderer });
            await page.evaluate(() => window.SharpMUSH.WikiRender.render());

            const metrics = await page.locator('.mermaid').evaluate(element => {
                const rgba = value => {
                    const channels = value.match(/[\d.]+/g).map(Number);
                    return [channels[0], channels[1], channels[2], channels[3] ?? 1];
                };
                const composite = (front, back) => {
                    const alpha = front[3] + back[3] * (1 - front[3]);
                    if (!alpha) return [0, 0, 0, 0];
                    return [...front.slice(0, 3).map((value, index) =>
                        (value * front[3] + back[index] * back[3] * (1 - front[3])) / alpha), alpha];
                };
                const luminance = color => color.slice(0, 3)
                    .map(value => value / 255)
                    .map(value => value <= 0.04045 ? value / 12.92 : ((value + 0.055) / 1.055) ** 2.4)
                    .reduce((total, value, index) => total + value * [0.2126, 0.7152, 0.0722][index], 0);
                const labels = [...element.querySelectorAll('.nodeLabel p, .edgeLabel p, .flowchartTitleText')];
                const ratios = labels.map(label => {
                    const style = getComputedStyle(label);
                    const foreground = rgba(label instanceof SVGElement ? style.fill : style.color);
                    let ground = [0, 0, 0, 0];
                    // Node labels sit over a sibling SVG shape, not a CSS ancestor background.
                    // This fixture uses rectangular nodes; sample their actual computed fill.
                    const shape = label.closest('g.node')?.querySelector('rect');
                    if (shape) ground = rgba(getComputedStyle(shape).fill);
                    for (let parent = label; parent && ground[3] < 1; parent = parent.parentElement) {
                        ground = composite(ground, rgba(getComputedStyle(parent).backgroundColor));
                    }
                    const ink = composite(foreground, ground);
                    const ratio = (Math.max(luminance(ink), luminance(ground)) + 0.05)
                        / (Math.min(luminance(ink), luminance(ground)) + 0.05);
                    return { label: label.textContent, shapeFill: shape ? getComputedStyle(shape).fill : null, ratio };
                });
                return { ratios, width: document.documentElement.scrollWidth, viewport: innerWidth, svg: !!element.querySelector('svg') };
            });

            if (process.env.MERMAID_AUDIT_OUTPUT) {
                const mode = background === '#ffffff' ? 'light' : 'dark';
                await page.screenshot({ path: `${process.env.MERMAID_AUDIT_OUTPUT}/${mode}.png` });
                console.log(JSON.stringify(metrics));
            }
            assert.ok(metrics.svg, 'shipped Mermaid must produce an SVG');
            assert.equal(metrics.ratios.length, 4, 'measure title, edge and both node labels');
            assert.equal(metrics.ratios.filter(label => label.shapeFill).length, 2, 'node labels must sample actual SVG rectangles');
            for (const label of metrics.ratios) {
                assert.ok(label.ratio >= 4.5, `${label.label}: ${label.ratio}`);
            }
            assert.ok(metrics.width <= metrics.viewport, 'diagram must not overflow viewport');
        } finally {
            await browser.close();
        }
    });
}
