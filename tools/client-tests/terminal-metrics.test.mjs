import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';

// The /play terminal fits its font so the player's column count fills the width (terminalMetrics.js).
// The fit grows the font on a roomy screen, but only while the output keeps enough rows: a phone held
// sideways is wide and short, and growing there would leave a handful of lines.
const root = new URL('../../SharpMUSH.Client/wwwroot/', import.meta.url);

// A monospace font whose glyphs advance 0.6em and whose lines are 1.6em, so a probe's box is exact.
const ADVANCE = 0.6;
const LINE = 1.6;
const BASE_PX = 13.5;

function boot({ width, height }) {
    const output = {
        style: { fontSize: '' },
        clientWidth: width,
        clientHeight: height,
        appendChild: () => { },
        removeChild: () => { }
    };
    const context = vm.createContext({
        window: {},
        document: {
            getElementById: id => id === 'output' ? output : null,
            createElement: () => {
                const probe = { style: {}, textContent: '' };
                probe.getBoundingClientRect = () => {
                    const px = parseFloat(probe.style.fontSize);
                    const lines = probe.textContent.split('\n');
                    return {
                        width: Math.max(...lines.map(l => l.length)) * px * ADVANCE,
                        height: lines.length * px * LINE
                    };
                };
                return probe;
            }
        },
        getComputedStyle: el => ({
            fontSize: (el.style.fontSize || BASE_PX + 'px'),
            fontFamily: 'mono', lineHeight: String(LINE), letterSpacing: '0', fontFeatureSettings: 'normal',
            paddingLeft: '0', paddingRight: '0', paddingTop: '0', paddingBottom: '0'
        })
    });
    const src = 'js/terminalMetrics.js';
    vm.runInContext(readFileSync(new URL(src, root), 'utf8'), context, { filename: src });
    const metrics = context.window.SharpMUSH.Metrics;
    metrics._targets.output = 78;
    return { metrics, output };
}

const fontOf = output => parseFloat(output.style.fontSize);

test('a tall, wide output grows the font to fill 78 columns', () => {
    const { metrics, output } = boot({ width: 1000, height: 800 });
    const grid = metrics.measure('output');

    assert.equal(grid.cols, 78);
    const font = fontOf(output);
    assert.ok(font > BASE_PX + 5, `expected the fit to grow well past ${BASE_PX}px, got ${font}`);
    assert.ok(78 * font * ADVANCE <= 1000, 'the 78 columns must still fit');
});

test('a wide but short output keeps the base font and shows more rows', () => {
    // A phone sideways in focus mode: ~818 by ~250 (issue #1506 measured 17px and about 7 lines).
    const { metrics, output } = boot({ width: 818, height: 250 });
    const grid = metrics.measure('output');

    assert.equal(grid.cols, 78);
    assert.equal(fontOf(output), BASE_PX, 'the font must not grow past the base on a short output');
    assert.equal(grid.rows, Math.floor(250 / (BASE_PX * LINE)));
});

test('height caps growth part of the way, at the size that still shows the minimum rows', () => {
    const { metrics, output } = boot({ width: 1000, height: 400 });
    metrics.measure('output');

    const cap = 400 / metrics.GROW_MIN_ROWS / LINE;
    assert.ok(cap > BASE_PX && cap < 1000 / 78 / ADVANCE, 'this case sits between the base and the width fit');
    assert.ok(Math.abs(fontOf(output) - cap) < 0.01, `expected ~${cap}px, got ${fontOf(output)}`);
});

test('a short output never shrinks the font below the base; a narrow one still does', () => {
    const short = boot({ width: 818, height: 120 });
    short.metrics.measure('output');
    assert.equal(fontOf(short.output), BASE_PX);

    const narrow = boot({ width: 500, height: 120 });
    narrow.metrics.measure('output');
    assert.ok(fontOf(narrow.output) < BASE_PX, 'width still shrinks the font to fit the columns');
});
