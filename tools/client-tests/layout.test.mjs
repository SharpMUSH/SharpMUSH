import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';

// The command palette's ⌘K / Ctrl+K hook (layout.js). It must not fire while the reader is typing:
// the terminal input and every text field keep their keys.
const root = new URL('../../SharpMUSH.Client/wwwroot/', import.meta.url);

function boot() {
    const listeners = new Map();
    const context = vm.createContext({
        window: { matchMedia: () => ({ matches: false }) },
        document: { addEventListener: (name, handler) => listeners.set(name, handler) },
        HTMLElement: class {}
    });
    vm.runInContext(readFileSync(new URL('js/layout.js', root), 'utf8'), context, { filename: 'js/layout.js' });
    return { layout: context.window.sharpmushLayout, listeners };
}

function key(target, init) {
    let prevented = false;
    return {
        event: { key: 'k', ctrlKey: false, metaKey: false, target, preventDefault: () => { prevented = true; }, ...init },
        prevented: () => prevented
    };
}

const body = { tagName: 'BODY', isContentEditable: false };
const input = { tagName: 'INPUT', isContentEditable: false };
const editor = { tagName: 'DIV', isContentEditable: true };

test('Ctrl+K and Cmd+K open the palette from the page', () => {
    const { layout, listeners } = boot();
    const calls = [];
    layout.registerPaletteHotkey({ invokeMethodAsync: name => calls.push(name) });

    const ctrl = key(body, { ctrlKey: true });
    listeners.get('keydown')(ctrl.event);
    const meta = key(body, { metaKey: true, key: 'K' });
    listeners.get('keydown')(meta.event);

    assert.deepEqual(calls, ['OpenPaletteFromHotkey', 'OpenPaletteFromHotkey']);
    assert.ok(ctrl.prevented() && meta.prevented(), 'the browser must not act on the chord too');
});

test('a text field or an editor keeps its own K', () => {
    const { layout, listeners } = boot();
    const calls = [];
    layout.registerPaletteHotkey({ invokeMethodAsync: name => calls.push(name) });

    for (const target of [input, editor]) {
        const press = key(target, { ctrlKey: true });
        listeners.get('keydown')(press.event);
        assert.equal(press.prevented(), false);
    }
    listeners.get('keydown')(key(body, {}).event);
    assert.deepEqual(calls, [], 'no chord, or typing: nothing opens');
});

test('registering twice keeps one listener', () => {
    const { layout, listeners } = boot();
    const first = [];
    const second = [];
    layout.registerPaletteHotkey({ invokeMethodAsync: name => first.push(name) });
    layout.registerPaletteHotkey({ invokeMethodAsync: name => second.push(name) });
    listeners.get('keydown')(key(body, { ctrlKey: true }).event);
    assert.equal(first.length + second.length, 1);
    assert.equal(second.length, 1, 'the latest registration answers');
});

test('AltGr+K and Ctrl+Shift+K belong to the keyboard layout and the browser', () => {
    const { layout, listeners } = boot();
    const calls = [];
    layout.registerPaletteHotkey({ invokeMethodAsync: name => { calls.push(name); return Promise.resolve(); } });
    for (const init of [{ ctrlKey: true, altKey: true }, { ctrlKey: true, shiftKey: true, key: 'K' }]) {
        const press = key(body, init);
        listeners.get('keydown')(press.event);
        assert.equal(press.prevented(), false);
    }
    assert.deepEqual(calls, []);
});

test('after unregistering, the chord does nothing and is not swallowed', () => {
    const { layout, listeners } = boot();
    const calls = [];
    layout.registerPaletteHotkey({ invokeMethodAsync: name => { calls.push(name); return Promise.resolve(); } });
    layout.unregisterPaletteHotkey();
    const press = key(body, { ctrlKey: true });
    listeners.get('keydown')(press.event);
    assert.deepEqual(calls, []);
    assert.equal(press.prevented(), false);
});

test('a disposed .NET object does not surface as an unhandled rejection', async () => {
    const { layout, listeners } = boot();
    let caught = false;
    const rejected = { then: () => rejected, catch: handler => { caught = true; handler(new Error('no tracked object')); return rejected; } };
    layout.registerPaletteHotkey({ invokeMethodAsync: () => rejected });
    listeners.get('keydown')(key(body, { ctrlKey: true }).event);
    assert.ok(caught, 'the invoke must carry a catch');
});
