import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';

const root = new URL('../../SharpMUSH.Client/wwwroot/', import.meta.url);
function boot() {
    const listeners = new Map();
    const classes = new Set();
    const box = {
        classList: {
            add: (...names) => names.forEach(n => classes.add(n)),
            remove: (...names) => names.forEach(n => classes.delete(n)),
            toggle: (name, on) => on ? classes.add(name) : classes.delete(name),
            contains: name => classes.has(name)
        }
    };
    const output = {
        scrollTop: 0, scrollHeight: 500, clientHeight: 200,
        contains: a => a.command !== undefined,
        closest: selector => selector === '.sharp-terminal-container' ? box : null,
        addEventListener: (name, handler) => listeners.set(name, handler),
        removeEventListener: name => listeners.delete(name)
    };
    const windowListeners = [];
    const context = vm.createContext({ window: { addEventListener: (name) => windowListeners.push(name) }, document: {
        getElementById: id => id === 'output' ? output : null
    }});
    const html = readFileSync(new URL('index.html', root), 'utf8');
    for (const [, reference] of html.matchAll(/<script src="(js\/[^"]+)"/g)) {
        // The build swaps the #[.{fingerprint}] marker for the file's content hash; the source file on
        // disk carries no hash at all.
        const src = reference.replace('#[.{fingerprint}]', '');
        vm.runInContext(readFileSync(new URL(src, root), 'utf8'), context, { filename: src });
    }
    return { terminal: context.window.SharpMUSH.Terminal, helpers: context.window.SharpMUSH, output, listeners, classes, windowListeners };
}

test('fresh Play page scrolls new output without loading an editor', () => {
    const { terminal, output } = boot();
    assert.ok(terminal, 'terminal helpers must be available at page startup');
    terminal.scrollToBottom('output');
    assert.equal(output.scrollTop, 500);
    output.scrollHeight = 900;
    terminal.scrollToBottom('output');
    assert.equal(output.scrollTop, 900);
});

test('scrolled up to reread, new output leaves the view in place and says so', () => {
    const { terminal, output, listeners, classes } = boot();
    terminal.scrollToBottom('output');
    assert.equal(output.scrollTop, 500);
    listeners.get('wheel')();
    output.scrollTop = 100;
    listeners.get('scroll')();
    assert.ok(classes.has('sharp-terminal--reading'), 'scrolled up is reading');
    output.scrollHeight = 900;
    terminal.scrollToBottom('output');
    assert.equal(output.scrollTop, 100, 'the reader is not dragged to the bottom');
    assert.ok(classes.has('sharp-terminal--new'), 'new output is announced');
    terminal.jumpToLatest('output');
    assert.equal(output.scrollTop, 900);
    assert.ok(!classes.has('sharp-terminal--reading') && !classes.has('sharp-terminal--new'));
    output.scrollHeight = 1200;
    terminal.scrollToBottom('output');
    assert.equal(output.scrollTop, 1200, 'back at the bottom, it follows again');
});

test('a scroll the reader did not make does not stop the terminal following', () => {
    const { terminal, output, listeners, classes } = boot();
    terminal.scrollToBottom('output');
    assert.equal(output.scrollTop, 500);
    // Lines re-rendered under the view: the browser clamps it to the top, then the content grows back.
    output.scrollTop = 0;
    output.scrollHeight = 900;
    listeners.get('scroll')();
    assert.equal(output.scrollTop, 900, 'put back at the bottom');
    assert.ok(!classes.has('sharp-terminal--reading'), 'not reading');
    output.scrollHeight = 1200;
    terminal.scrollToBottom('output');
    assert.equal(output.scrollTop, 1200, 'still following');
});

test('following a terminal adds nothing to window, so an unmounted terminal is not kept alive', () => {
    const { terminal, windowListeners } = boot();
    const before = windowListeners.length;
    terminal.scrollToBottom('output');
    assert.equal(windowListeners.length, before, 'the pointer-release listeners are shared and installed once');
});

test('fresh Play page command links support clicks, keyboard, and disposal', () => {
    const { terminal, listeners } = boot();
    assert.ok(terminal, 'terminal helpers must be available at page startup');
    const calls = [];
    const handle = terminal.attachCommandLinks('output', {
        invokeMethodAsync: (...args) => calls.push(args)
    });
    const anchor = { command: 'help newbie', getAttribute: () => 'help newbie' };
    let prevented = 0;
    const event = { target: { closest: () => anchor }, preventDefault: () => prevented++ };
    listeners.get('click')(event);
    listeners.get('keydown')({ ...event, key: 'Enter' });
    listeners.get('keydown')({ ...event, key: ' ' });
    assert.deepEqual(calls, Array.from({ length: 3 }, () => ['RunCommandLinkAsync', 'help newbie']));
    assert.equal(prevented, 3);
    listeners.get('click')({ ...event, target: { closest: () => null } });
    assert.equal(prevented, 3, 'ordinary URL links retain browser navigation');
    handle.dispose();
    assert.equal(listeners.size, 0);
});

test('help and wiki helpers are available before an editor opens', () => {
    const { helpers } = boot();
    assert.equal(typeof helpers.Help?.registerLinkInterceptor, 'function');
    assert.equal(typeof helpers.Wiki?.wrap, 'function');
});

test('a terminal disposed before the fonts load is not measured for afterwards', async () => {
    let fontsLoaded;
    const output = { style: {}, clientWidth: 600, clientHeight: 400 };
    const context = vm.createContext({
        window: {},
        document: {
            getElementById: id => id === 'output' ? output : null,
            fonts: { ready: new Promise(resolve => { fontsLoaded = resolve; }) }
        },
        ResizeObserver: class { observe() { } disconnect() { } },
        setTimeout, clearTimeout
    });
    vm.runInContext(readFileSync(new URL('js/terminalMetrics.js', root), 'utf8'), context, { filename: 'terminalMetrics.js' });
    const calls = [];
    const handle = context.window.SharpMUSH.Metrics.observe('output', { invokeMethodAsync: (...args) => calls.push(args) }, 78);

    // Mounted and gone at once (/register redirecting to /login): .NET has already let go of the reference.
    handle.dispose();
    fontsLoaded();
    await new Promise(resolve => setImmediate(resolve));

    assert.deepEqual(calls, [], 'no call through a released reference');
});
