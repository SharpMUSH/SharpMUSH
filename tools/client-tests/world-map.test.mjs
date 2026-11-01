import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';

// portal-interop.js, the World map's part: measuring the frame to fit a map in it, keeping the middle of the view
// where it was across a zoom, and bringing a room to the middle of the view.
const root = new URL('../../SharpMUSH.Client/wwwroot/', import.meta.url);

// Listeners by event name, on a stand-in for window or an element.
function listening(target = {}) {
    target.listeners = {};
    target.addEventListener = (name, fn, options) => {
        (target.listeners[name] ??= []).push({ fn, once: options?.once });
    };
    target.removeEventListener = (name, fn) => {
        target.listeners[name] = (target.listeners[name] ?? []).filter(entry => entry.fn !== fn);
    };
    target.fire = (name, event) => {
        const entries = [...(target.listeners[name] ?? [])];
        target.listeners[name] = entries.filter(entry => !entry.once);
        for (const entry of entries) entry.fn(event);
    };
    return target;
}

function load(styles = new Map(), reducedMotion = false) {
    const frames = [];
    const window = listening({
        matchMedia: () => ({ matches: reducedMotion }),
        requestAnimationFrame: fn => frames.push(fn),
        setTimeout: () => {},
        frames,
    });
    const context = vm.createContext({ window, getComputedStyle: el => styles.get(el) ?? {} });
    vm.runInContext(readFileSync(new URL('js/portal-interop.js', root), 'utf8'), context, { filename: 'js/portal-interop.js' });
    window.SharpMUSH.window = window;
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

test('a frame has its own size for the drawing, less the room kept below it', () => {
    // The frame's height is fixed, so a fit measures it rather than the drawing it holds.
    const el = frame({ clientWidth: 1200, clientHeight: 700 }, { width: 500, height: 280 });
    const map = load(new Map([[el.firstElementChild, { marginBottom: '28px' }]]));

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

test('a drawing taller than its frame keeps the frame\'s middle still', () => {
    // A 528-tall frame with 28 kept below, scrolled to 100 over a 1000-tall drawing: its middle (264) is at 364,
    // which doubles to 728 and shows from 464.
    const el = frame({ clientWidth: 1000, clientHeight: 528, top: 100 }, { width: 2000, height: 2000 });
    const map = load(new Map([[el.firstElementChild, { marginBottom: '28px' }]]));

    map.mapRescale(el, 2);

    assert.equal(el.scrollTop, 464);
});

test('a drawing that was shorter than its frame was centred in it, so its middle is its own', () => {
    // Drawing 300 tall in a 528-tall frame with 28 kept below sat 100 down; its middle (150) doubles to 300 in a
    // 600-tall drawing, which the frame's middle (250) shows from 50.
    const el = frame({ clientWidth: 1000, clientHeight: 528 }, { width: 2000, height: 600 });
    const map = load(new Map([[el.firstElementChild, { marginBottom: '28px' }]]));

    map.mapRescale(el, 2);

    assert.equal(el.scrollTop, 50);
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

// A frame to pan and zoom, 1000 x 500 at (100, 50) on the page, over a 3000 x 2000 drawing.
function gestureFrame() {
    const el = listening(frame({ clientWidth: 1000, clientHeight: 500, left: 400, top: 300 }, { width: 3000, height: 2000 }));
    el.getBoundingClientRect = () => ({ left: 100, top: 50 });
    el.classList = { add() {}, remove() {} };
    return el;
}

function recorder() {
    const calls = [];
    return { calls, invokeMethodAsync: (name, ...args) => calls.push([name, ...args]) };
}

const event = extra => ({ preventDefault() { this.prevented = true; }, stopPropagation() {}, ...extra });

test('a pinch on a trackpad, which the browser sends as the wheel with Ctrl, zooms about the point under it', () => {
    const map = load();
    const el = gestureFrame();
    const dotNet = recorder();
    map.mapGestures(el, dotNet);

    const wheel = event({ ctrlKey: true, deltaY: -250, clientX: 300, clientY: 150 });
    el.fire('wheel', wheel);
    el.fire("wheel", event({ ctrlKey: true, deltaY: -250, clientX: 300, clientY: 150 }));
    map.window.frames.splice(0).forEach(fn => fn());

    assert.equal(wheel.prevented, true);
    assert.equal(dotNet.calls.length, 1, 'two wheel steps in one frame are one zoom');
    const [name, factor, x, y] = dotNet.calls[0];
    assert.equal(name, 'ZoomAt');
    assert.ok(Math.abs(factor - Math.exp(1)) < 1e-9);
    assert.deepEqual([x, y], [200, 100]);
});

test('the plain wheel is left to scroll the frame', () => {
    const map = load();
    const el = gestureFrame();
    const dotNet = recorder();
    map.mapGestures(el, dotNet);

    const wheel = event({ ctrlKey: false, deltaY: 120, clientX: 300, clientY: 150 });
    el.fire('wheel', wheel);

    assert.equal(wheel.prevented, undefined);
    assert.equal(map.window.frames.length, 0);
});

test('a mouse drag pans the view, and the click that ends it chooses nothing', () => {
    const map = load();
    const el = gestureFrame();
    map.mapGestures(el, recorder());

    el.fire('pointerdown', event({ pointerType: 'mouse', button: 0, clientX: 500, clientY: 300 }));
    map.window.fire('pointermove', event({ clientX: 440, clientY: 280 }));
    map.window.fire('pointerup', event({}));
    const click = event({});
    el.fire('click', click);

    assert.deepEqual([el.scrollLeft, el.scrollTop], [460, 320]);
    assert.equal(click.prevented, true);
});

test('a mouse that barely moves is a click', () => {
    const map = load();
    const el = gestureFrame();
    map.mapGestures(el, recorder());

    el.fire('pointerdown', event({ pointerType: 'mouse', button: 0, clientX: 500, clientY: 300 }));
    map.window.fire('pointermove', event({ clientX: 502, clientY: 301 }));
    map.window.fire('pointerup', event({}));

    assert.deepEqual([el.scrollLeft, el.scrollTop], [400, 300]);
    assert.equal((el.listeners.click ?? []).length, 0);
});

test('two fingers spreading zoom about their middle, and moving together pan', () => {
    const map = load();
    const el = gestureFrame();
    const dotNet = recorder();
    map.mapGestures(el, dotNet);
    const touch = (x, y) => ({ clientX: x, clientY: y });

    el.fire('touchstart', event({ touches: [touch(400, 200), touch(600, 200)] }));
    const move = event({ touches: [touch(380, 210), touch(620, 210)] });
    el.fire('touchmove', move);
    map.window.frames.splice(0).forEach(fn => fn());

    assert.equal(move.prevented, true, 'the page does not zoom or scroll under the pinch');
    assert.deepEqual([el.scrollLeft, el.scrollTop], [400, 290], 'the middle moved 10 down, so the view follows it');
    const [name, factor, x, y] = dotNet.calls[0];
    assert.equal(name, 'ZoomAt');
    assert.ok(Math.abs(factor - 1.2) < 1e-9);
    assert.deepEqual([x, y], [400, 160]);
});

test('one finger is left to scroll the frame', () => {
    const map = load();
    const el = gestureFrame();
    map.mapGestures(el, recorder());

    el.fire('touchstart', event({ touches: [{ clientX: 400, clientY: 200 }] }));
    const move = event({ touches: [{ clientX: 400, clientY: 100 }] });
    el.fire('touchmove', move);

    assert.equal(move.prevented, undefined);
});

test('zooming about a point keeps that point of the drawing under it', () => {
    // At (200, 100) in a view scrolled to (400, 300), the drawing's point is (600, 400); doubled, (1200, 800).
    const el = frame({ clientWidth: 1000, clientHeight: 500, left: 400, top: 300 }, { width: 6000, height: 4000 });
    const map = load();

    map.mapRescale(el, 2, 200, 100);

    assert.deepEqual([el.scrollLeft, el.scrollTop], [1000, 700]);
});

// A window whose timers run when told, and whose ResizeObserver can be fired by hand.
function timed() {
    const timers = new Map();
    let next = 1;
    const observers = [];
    const map = load();
    const window = map.window;
    window.setTimeout = (fn, ms) => { timers.set(next, { fn, ms }); return next++; };
    window.clearTimeout = id => timers.delete(id);
    window.ResizeObserver = class {
        constructor(fn) { this.fn = fn; this.watched = []; observers.push(this); }
        observe(el) { this.watched.push(el); }
        disconnect() { this.watched = []; this.disconnected = true; }
    };
    const run = () => {
        const due = [...timers.values()];
        timers.clear();
        for (const timer of due) timer.fn();
    };
    return { map, timers, observers, run };
}

function asking() {
    const calls = [];
    return { calls, invokeMethodAsync: (name, ...args) => calls.push([name, ...args]) };
}

test('the view is reported once scrolling settles, as the part of the drawing in the frame', () => {
    const { map, timers, run } = timed();
    const el = listening(frame({ clientWidth: 800, clientHeight: 400, left: 300, top: 200 }, { width: 4000, height: 3000 }));
    const dotNet = asking();

    map.mapViewport(el, dotNet);
    run();
    assert.deepEqual(dotNet.calls, [['ViewAt', 300, 200, 800, 400]]);

    el.scrollLeft = 500;
    el.fire('scroll');
    el.fire('scroll');
    assert.equal(timers.size, 1, 'a burst of scrolling waits for one report');
    assert.equal([...timers.values()][0].ms, 150);
    run();
    assert.deepEqual(dotNet.calls[1], ['ViewAt', 500, 200, 800, 400]);
});

test('a drawing narrower than its frame is in view from its own edge, and no wider than itself', () => {
    const { map, run } = timed();
    const el = listening(frame({ clientWidth: 1000, clientHeight: 600 }, { width: 400, height: 300 }));
    const dotNet = asking();

    map.mapViewport(el, dotNet);
    run();

    assert.deepEqual(dotNet.calls, [['ViewAt', 0, 0, 400, 300]]);
});

test('a resize of the frame or the drawing reports the view, and disposing stops it all', () => {
    const { map, timers, observers, run } = timed();
    const el = listening(frame({ clientWidth: 800, clientHeight: 400 }, { width: 4000, height: 3000 }));
    const dotNet = asking();

    const watch = map.mapViewport(el, dotNet);
    run();
    assert.deepEqual(observers[0].watched, [el, el.firstElementChild]);
    el.firstElementChild.offsetWidth = 8000;
    el.clientWidth = 900;
    observers[0].fn();
    run();
    assert.deepEqual(dotNet.calls[1], ['ViewAt', 0, 0, 900, 400]);

    el.fire('scroll');
    watch.dispose();
    assert.equal(timers.size, 0);
    assert.equal(observers[0].disconnected, true);
    assert.deepEqual(el.listeners.scroll, []);
});
