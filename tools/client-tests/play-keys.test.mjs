import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';

// play-keys.js: a key the player bound runs its command anywhere on Play, except in another field or an open dialog.
const source = readFileSync(new URL('../../SharpMUSH.Client/wwwroot/js/play-keys.js', import.meta.url), 'utf8');

function el(selectors = []) {
    return { closest: s => s.split(',').map(x => x.trim()).some(x => selectors.includes(x)) ? {} : null };
}

function boot({ dialog = false } = {}) {
    let handler = null;
    const document = {
        addEventListener: (name, h, capture) => { if (name === 'keydown' && capture) handler = h; },
        querySelector: () => (dialog ? {} : null),
    };
    const window = {};
    vm.runInNewContext(source, { window, document, Set });
    const calls = [];
    const dotNet = { invokeMethodAsync: (name, chord) => calls.push([name, chord]) };
    function press(code, { target = el(), ctrl = false, alt = false, shift = false, meta = false, repeat = false } = {}) {
        const e = {
            code, ctrlKey: ctrl, altKey: alt, shiftKey: shift, metaKey: meta, repeat, isComposing: false, target,
            prevented: false, stopped: false,
            preventDefault() { this.prevented = true; },
            stopPropagation() { this.stopped = true; },
        };
        handler(e);
        return e;
    }
    return { api: window.SharpMUSH.PlayKeys, dotNet, calls, press };
}

test('a bound key runs its chord and the browser keeps its own action', () => {
    const { api, dotNet, calls, press } = boot();
    api.bind(dotNet, ['F2', 'Shift+Numpad8']);
    const e = press('F2');
    assert.deepEqual(calls, [['RunShortcutAsync', 'F2']]);
    assert.ok(e.prevented && e.stopped);
    press('Numpad8', { shift: true });
    assert.deepEqual(calls[1], ['RunShortcutAsync', 'Shift+Numpad8']);
});

test('an unbound key, or a bound one with other modifiers, is left alone', () => {
    const { api, dotNet, calls, press } = boot();
    api.bind(dotNet, ['F2']);
    assert.equal(press('F3').prevented, false);
    assert.equal(press('F2', { ctrl: true }).prevented, false);
    assert.equal(press('F2', { meta: true }).prevented, false);
    assert.deepEqual(calls, []);
});

test('Play\'s own input box runs the key, another field and an open dialog do not', () => {
    const { api, dotNet, calls, press } = boot();
    api.bind(dotNet, ['F2']);
    press('F2', { target: el(['[data-primary-input]', 'textarea']) });
    assert.equal(calls.length, 1);
    const other = press('F2', { target: el(['input']) });
    assert.equal(other.prevented, false);
    assert.equal(calls.length, 1);

    const withDialog = boot({ dialog: true });
    withDialog.api.bind(withDialog.dotNet, ['F2']);
    withDialog.press('F2');
    assert.deepEqual(withDialog.calls, []);
});

test('a held key runs once', () => {
    const { api, dotNet, calls, press } = boot();
    api.bind(dotNet, ['F2']);
    press('F2');
    const held = press('F2', { repeat: true });
    assert.equal(calls.length, 1);
    assert.ok(held.prevented, 'the repeat still does not reach the browser');
});

test('a key recorder keeps the browser\'s action for any bindable key and runs nothing', () => {
    const { api, dotNet, calls, press } = boot();
    api.bind(dotNet, ['F3']);
    const recorded = press('F3', { target: el(['[data-key-recorder]']) });
    assert.ok(recorded.prevented);
    assert.deepEqual(calls, []);
    assert.equal(press('KeyA', { target: el(['[data-key-recorder]']) }).prevented, false);
});

test('unbound, no key runs anything', () => {
    const { api, dotNet, calls, press } = boot();
    api.bind(dotNet, ['F2']);
    api.unbind();
    assert.equal(press('F2').prevented, false);
    assert.deepEqual(calls, []);
});

test('an open menu over the page has the keys, and an older page unbinding leaves a newer one bound', () => {
    const { api, dotNet, calls, press } = boot({ dialog: true });
    api.bind(dotNet, ['F2']);
    press('F2');
    assert.deepEqual(calls, []);

    const fresh = boot();
    const older = { _id: 1, invokeMethodAsync: () => {} };
    const newer = { _id: 2, invokeMethodAsync: (name, chord) => fresh.calls.push([name, chord]) };
    fresh.api.bind(older, ['F2']);
    fresh.api.bind(newer, ['F2']);
    fresh.api.unbind(older);
    fresh.press('F2');
    assert.deepEqual(fresh.calls, [['RunShortcutAsync', 'F2']]);
});
