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
    return { layout: context.window.sharpmushLayout, listeners, document: context.document };
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

// Play's Story view: a click on a pose's mention opens the character sheet; a new-tab click keeps the link.
function storyElement() {
    const listeners = new Map();
    return {
        listeners,
        addEventListener: (name, handler) => listeners.set(name, handler),
        removeEventListener: (name, handler) => { if (listeners.get(name) === handler) listeners.delete(name); },
        contains: () => true
    };
}

function mentionClick(init) {
    let prevented = false;
    const link = { getAttribute: name => (name === 'data-name' ? 'Tomas Reyes' : null) };
    return {
        event: {
            button: 0, ctrlKey: false, metaKey: false, shiftKey: false, altKey: false,
            target: { closest: selector => (selector.startsWith('a.mention') ? link : null) },
            preventDefault: () => { prevented = true; }, ...init
        },
        prevented: () => prevented
    };
}

test('a plain click on a mention opens the character, a new-tab click follows the link', () => {
    const { layout } = boot();
    const element = storyElement();
    const calls = [];
    layout.delegateMentions(element, { invokeMethodAsync: (name, arg) => { calls.push([name, arg]); return Promise.resolve(); } });

    const plain = mentionClick({});
    element.listeners.get('click')(plain.event);
    const newTab = mentionClick({ ctrlKey: true });
    element.listeners.get('click')(newTab.event);
    const middle = mentionClick({ button: 1 });
    element.listeners.get('click')(middle.event);

    assert.deepEqual(calls, [['OpenMention', 'Tomas Reyes']]);
    assert.ok(plain.prevented());
    assert.equal(newTab.prevented() || middle.prevented(), false);
});

// Play's exit keys (README §5.6): the keycap's letter takes the exit, except while the reader is typing
// or a dialog (the character sheet, the palette) is open.
function bootWithDialog(open) {
    const listeners = new Map();
    const context = vm.createContext({
        window: { matchMedia: () => ({ matches: false }) },
        document: {
            addEventListener: (name, handler) => listeners.set(name, handler),
            querySelector: selector => (open && selector.includes('aria-modal') ? {} : null)
        },
        HTMLElement: class {}
    });
    vm.runInContext(readFileSync(new URL('js/layout.js', root), 'utf8'), context, { filename: 'js/layout.js' });
    return { layout: context.window.sharpmushLayout, listeners };
}

function press(k, target = body, init = {}) {
    let prevented = false;
    return {
        event: { key: k, ctrlKey: false, metaKey: false, altKey: false, shiftKey: false, repeat: false, target,
            preventDefault: () => { prevented = true; }, ...init },
        prevented: () => prevented
    };
}

test('an exit key goes, in either case, and is not typed anywhere else', () => {
    const { layout, listeners } = bootWithDialog(false);
    const calls = [];
    layout.registerExitKeys({ invokeMethodAsync: (name, arg) => { calls.push([name, arg]); return Promise.resolve(); } }, ['n', 'w']);
    const n = press('n');
    listeners.get('keydown')(n.event);
    listeners.get('keydown')(press('W').event);
    assert.deepEqual(calls, [['GoByKey', 'n'], ['GoByKey', 'w']]);
    assert.ok(n.prevented());
});

test('exit keys are ignored while typing, with a modifier, held down, unlisted, or under a dialog', () => {
    const { layout, listeners } = bootWithDialog(false);
    const calls = [];
    layout.registerExitKeys({ invokeMethodAsync: name => { calls.push(name); return Promise.resolve(); } }, ['n']);
    for (const event of [press('n', input), press('n', editor), press('n', { tagName: 'TEXTAREA' }), press('n', { tagName: 'SELECT' }),
        press('n', body, { ctrlKey: true }), press('n', body, { altKey: true }), press('n', body, { metaKey: true }),
        press('n', body, { repeat: true }), press('x')]) {
        listeners.get('keydown')(event.event);
        assert.equal(event.prevented(), false);
    }
    assert.deepEqual(calls, []);

    const dialog = bootWithDialog(true);
    const underDialog = [];
    dialog.layout.registerExitKeys({ invokeMethodAsync: name => { underDialog.push(name); return Promise.resolve(); } }, ['n']);
    dialog.listeners.get('keydown')(press('n').event);
    assert.deepEqual(underDialog, []);
});

test('re-registering replaces the keys, and unregistering stops them', () => {
    const { layout, listeners } = bootWithDialog(false);
    const calls = [];
    const ref = { invokeMethodAsync: (name, arg) => { calls.push(arg); return Promise.resolve(); } };
    layout.registerExitKeys(ref, ['n']);
    layout.registerExitKeys(ref, ['s']);
    listeners.get('keydown')(press('n').event);
    listeners.get('keydown')(press('s').event);
    layout.unregisterExitKeys();
    listeners.get('keydown')(press('s').event);
    assert.deepEqual(calls, ['s']);
});

// Play's composer: Enter sends, Shift+Enter (and IME composition) keep the textarea's own behaviour.
test('Enter sends from the composer, Shift+Enter and composition keep the newline', () => {
    const { layout } = boot();
    const element = storyElement();
    const calls = [];
    layout.composerEnter(element, { invokeMethodAsync: name => { calls.push(name); return Promise.resolve(); } });
    const enter = press('Enter');
    element.listeners.get('keydown')(enter.event);
    const shift = press('Enter', body, { shiftKey: true });
    element.listeners.get('keydown')(shift.event);
    const composing = press('Enter', body, { isComposing: true });
    element.listeners.get('keydown')(composing.event);
    const other = press('a');
    element.listeners.get('keydown')(other.event);
    assert.deepEqual(calls, ['SendFromEnter']);
    assert.ok(enter.prevented());
    assert.equal(shift.prevented() || composing.prevented() || other.prevented(), false);
});

// Safari reports the Enter that commits an IME candidate after compositionend: isComposing is false,
// and keyCode 229 is the only mark that the key belongs to the composition.
test('the Enter that commits an IME candidate in Safari (keyCode 229) does not send', () => {
    const { layout } = boot();
    const element = storyElement();
    const calls = [];
    layout.composerEnter(element, { invokeMethodAsync: name => { calls.push(name); return Promise.resolve(); } });
    const commit = press('Enter', body, { isComposing: false, keyCode: 229 });
    element.listeners.get('keydown')(commit.event);
    assert.deepEqual(calls, []);
    assert.equal(commit.prevented(), false);
});

test('exit keys ignore Shift, so a capital letter is typed where it belongs', () => {
    const { layout, listeners } = bootWithDialog(false);
    const calls = [];
    layout.registerExitKeys({ invokeMethodAsync: name => { calls.push(name); return Promise.resolve(); } }, ['n']);
    const shifted = press('N', body, { shiftKey: true });
    listeners.get('keydown')(shifted.event);
    assert.deepEqual(calls, []);
    assert.equal(shifted.prevented(), false);
});

test('a click outside a mention does nothing, and undelegating removes the listener', () => {
    const { layout } = boot();
    const element = storyElement();
    const calls = [];
    layout.delegateMentions(element, { invokeMethodAsync: name => { calls.push(name); return Promise.resolve(); } });
    element.listeners.get('click')({ button: 0, target: { closest: () => null }, preventDefault: () => assert.fail('not a mention') });
    layout.undelegateMentions(element);
    assert.equal(element.listeners.has('click'), false);
    assert.deepEqual(calls, []);
});

test('the compact-screen watch reports the screen now and on every turn, and stops when unwatched', () => {
    const queries = [];
    const context = vm.createContext({
        window: {
            matchMedia: media => {
                const listeners = new Set();
                const query = {
                    media,
                    matches: true,
                    addEventListener: (name, handler) => { assert.equal(name, 'change'); listeners.add(handler); },
                    removeEventListener: (name, handler) => listeners.delete(handler),
                    fire: matches => listeners.forEach(handler => handler({ matches })),
                    listeners
                };
                queries.push(query);
                return query;
            }
        },
        document: { addEventListener: () => { } },
        HTMLElement: class {}
    });
    vm.runInContext(readFileSync(new URL('js/layout.js', root), 'utf8'), context, { filename: 'js/layout.js' });
    const layout = context.window.sharpmushLayout;
    const calls = [];
    const ref = { invokeMethodAsync: (name, value) => { calls.push([name, value]); return Promise.resolve(); } };

    assert.equal(layout.watchCompactScreen(ref), true);
    assert.equal(queries[0].media, '(orientation: landscape) and (max-height: 32rem), (max-width: 760px)');
    queries[0].fire(false);
    assert.deepEqual(calls, [['OnCompactScreenChanged', false]]);

    // Watching again replaces the first watch rather than adding a second listener.
    layout.watchCompactScreen(ref);
    assert.equal(queries[0].listeners.size, 0);
    layout.unwatchCompactScreen();
    assert.equal(queries[1].listeners.size, 0);
    queries[1].fire(true);
    assert.equal(calls.length, 1);
});

test('the short-screen watch is the landscape half of the compact one, and is a watch of its own', () => {
    const queries = [];
    const context = vm.createContext({
        window: {
            matchMedia: media => {
                const listeners = new Set();
                const query = {
                    media,
                    matches: false,
                    addEventListener: (name, handler) => listeners.add(handler),
                    removeEventListener: (name, handler) => listeners.delete(handler),
                    fire: matches => listeners.forEach(handler => handler({ matches })),
                    listeners
                };
                queries.push(query);
                return query;
            }
        },
        document: { addEventListener: () => { } },
        HTMLElement: class {}
    });
    vm.runInContext(readFileSync(new URL('js/layout.js', root), 'utf8'), context, { filename: 'js/layout.js' });
    const layout = context.window.sharpmushLayout;
    const calls = [];
    const ref = { invokeMethodAsync: (name, value) => { calls.push([name, value]); return Promise.resolve(); } };

    layout.watchCompactScreen(ref);
    assert.equal(layout.watchShortScreen(ref), false);
    const [compact, short] = queries;
    assert.equal(short.media, '(orientation: landscape) and (max-height: 32rem)');
    assert.ok(compact.media.startsWith(short.media + ','), 'the short screen is the compact screen held sideways');

    short.fire(true);
    compact.fire(true);
    assert.deepEqual(calls, [['OnShortScreenChanged', true], ['OnCompactScreenChanged', true]]);

    // Unwatching one leaves the other listening.
    layout.unwatchShortScreen();
    assert.equal(short.listeners.size, 0);
    assert.equal(compact.listeners.size, 1);
    layout.unwatchCompactScreen();
    assert.equal(compact.listeners.size, 0);
});

test('nested modals hand focus back in turn: the inner to the outer, the outer to what opened it', () => {
    const { layout, document } = boot();
    const focused = [];
    const control = name => ({ isConnected: true, focus() { focused.push(name); } });

    document.activeElement = control('row');
    layout.rememberFocus();
    document.activeElement = control('sheet');
    layout.rememberFocus();

    layout.restoreFocus();
    layout.restoreFocus();
    assert.deepEqual(focused, ['sheet', 'row']);
});

test('a control gone from the page gets no focus back', () => {
    const { layout, document } = boot();
    let focused = false;
    document.activeElement = { isConnected: false, focus() { focused = true; } };
    layout.rememberFocus();
    layout.restoreFocus();
    assert.equal(focused, false);
});
