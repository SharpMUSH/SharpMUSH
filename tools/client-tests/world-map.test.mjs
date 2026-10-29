import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';

// portal-interop.js, the World map's part: measuring the frame to fit a map in it, keeping the middle of the view
// where it was across a zoom, and bringing a room to the middle of the view.
const root = new URL('../../SharpMUSH.Client/wwwroot/', import.meta.url);

function load(styles = new Map(), reducedMotion = false) {
    const window = { matchMedia: () => ({ matches: reducedMotion }), addEventListener() {} };
    const context = vm.createContext({ window, getComputedStyle: el => styles.get(el) ?? {} });
    vm.runInContext(readFileSync(new URL('js/portal-interop.js', root), 'utf8'), context, { filename: 'js/portal-interop.js' });
    return window.SharpMUSH;
}

// A frame clientWidth x clientHeight scrolled to (left, top), holding a figure width x height.
function frame({ clientWidth, clientHeight, left = 0, top = 0 }, figure) {
    return {
        clientWidth, clientHeight, clientLeft: 0, clientTop: 0, scrollLeft: left, scrollTop: top,
        firstElementChild: { offsetWidth: figure.width, offsetHeight: figure.height },
        getBoundingClientRect: () => ({ left: 0, top: 0 }),
        scrollTo(to) { this.scrolled = to; },
        scrollIntoView(how) { this.intoView = how; },
    };
}

test('a frame has its width and the tallest it may grow, less the room kept below the drawing', () => {
    const el = frame({ clientWidth: 1200, clientHeight: 300 }, { width: 500, height: 280 });
    const map = load(new Map([[el, { maxHeight: '700px' }], [el.firstElementChild, { marginBottom: '28px' }]]));

    assert.deepEqual([...map.mapFrameSize(el)], [1200, 672]);
});

test('zooming in keeps the middle of the view where it was', () => {
    // Drawing 2000 x 1000, view 1000 x 500 at (500, 250): the middle is (1000, 500). Doubled, that is (2000, 1000).
    const el = frame({ clientWidth: 1000, clientHeight: 500, left: 500, top: 250 }, { width: 4000, height: 2000 });
    const map = load();

    map.mapRescale(el, 2);

    assert.equal(el.scrollLeft, 1500);
    assert.equal(el.scrollTop, 750);
});

test('a drawing that was narrower than its frame was centred in it, so its middle is its own', () => {
    // Drawing 600 wide in a 1000-wide frame sat 200 in; its middle (300) doubles to 600 in a 1200-wide drawing.
    const el = frame({ clientWidth: 1000, clientHeight: 500 }, { width: 1200, height: 800 });
    const map = load();

    map.mapRescale(el, 2);

    assert.equal(el.scrollLeft, 100);
});

test('a room is brought to the middle of the view, at once for a reader who asked for less motion', () => {
    const el = frame({ clientWidth: 1000, clientHeight: 500 }, { width: 3000, height: 2000 });
    let focused = null;
    const room = {
        getBoundingClientRect: () => ({ left: 1800, top: 900, width: 100, height: 100 }),
        focus(how) { focused = how; },
    };
    const map = load(new Map(), true);

    map.mapCentre(el, room, true);

    assert.equal(el.scrolled.left, 1350);
    assert.equal(el.scrolled.top, 700);
    assert.equal(el.scrolled.behavior, 'auto');
    assert.equal(focused.preventScroll, true);
});
