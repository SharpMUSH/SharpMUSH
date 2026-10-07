import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';

// input-focus.js: the page's main text field (data-primary-input) is reached by arriving, typing and clicking,
// without taking a key or a click from a control, a field, a dialog or a page shortcut.
const root = new URL('../../SharpMUSH.Client/wwwroot/', import.meta.url);

// A stand-in element: `is` lists the simple selectors it matches; closest() walks the parents with that list,
// which is enough for the comma-separated simple selectors the script uses.
class El {
    constructor(is = [], parent = null, attrs = {}) {
        this.is = is;
        this.parent = parent;
        this.attrs = { ...attrs };
        this.value = '';
        this.disabled = false;
        this.readOnly = false;
        this.isConnected = true;
        this.shown = true;
        this.selection = null;
    }
    matches(selector) {
        return selector.split(',').map(s => s.trim()).some(s => this.is.includes(s)
            || (s.startsWith('[') && s.endsWith(']') && !s.includes('=') && Object.hasOwn(this.attrs, s.slice(1, -1))));
    }
    closest(selector) {
        for (let el = this; el; el = el.parent) if (el.matches(selector)) return el;
        return null;
    }
    contains(other) {
        for (let el = other; el; el = el.parent) if (el === this) return true;
        return false;
    }
    hasAttribute(name) { return Object.hasOwn(this.attrs, name); }
    getAttribute(name) { return Object.hasOwn(this.attrs, name) ? this.attrs[name] : null; }
    setAttribute(name, value) { this.attrs[name] = String(value); }
    getClientRects() { return this.shown ? [{}] : []; }
    setSelectionRange(start, end) { this.selection = [start, end]; }
}

function boot({ coarse = false } = {}) {
    const listeners = { window: {}, document: {} };
    const elements = [];
    const timers = [];
    const document = {
        body: null,
        documentElement: null,
        activeElement: null,
        addEventListener: (name, handler, options) => {
            const capture = options === true || (options && options.capture);
            listeners.document[capture ? `${name}:capture` : name] = handler;
        },
        querySelectorAll: selector => elements.filter(e => e.matches(selector)),
        querySelector: selector => elements.find(e => e.matches(selector)) || null,
        getElementById: id => elements.find(e => e.attrs.id === id) || null,
    };
    const window = {
        addEventListener: (name, handler) => { listeners.window[name] = handler; },
        matchMedia: () => ({ matches: coarse }),
        getSelection: () => ({ isCollapsed: !window.selected }),
        selected: false,
    };
    const add = (is, parent, attrs) => {
        const el = new El(is, parent, attrs);
        el.focus = () => {
            document.activeElement = el;
            listeners.document.focusin?.({ target: el });
        };
        elements.push(el);
        return el;
    };
    document.body = add(['body']);
    document.documentElement = add(['html']);
    document.activeElement = document.body;
    const main = add(['main'], document.body, { id: 'main-content' });
    const context = vm.createContext({ window, document, setTimeout: fn => timers.push(fn) });
    vm.runInContext(readFileSync(new URL('js/input-focus.js', root), 'utf8'), context, { filename: 'js/input-focus.js' });
    const flush = () => { while (timers.length) timers.shift()(); };
    return { api: window.sharpmushInputFocus, listeners, document, window, main, add, flush };
}

function keydown(target, init = {}) {
    let prevented = false;
    return {
        event: {
            key: 'h', ctrlKey: false, metaKey: false, altKey: false, shiftKey: false, repeat: false, isComposing: false,
            defaultPrevented: false, target, preventDefault: () => { prevented = true; }, ...init,
        },
        prevented: () => prevented,
    };
}

function click(target, init = {}) {
    return {
        target, button: 0, detail: 1, ctrlKey: false, metaKey: false, shiftKey: false, altKey: false,
        defaultPrevented: false, preventDefault() { this.defaultPrevented = true; }, ...init,
    };
}

test('a printable key on the page moves focus to the field and is not prevented', () => {
    const { listeners, document, main, add } = boot();
    const field = add(['textarea'], main, { 'data-primary-input': 'end' });
    field.value = 'draft';
    const heading = add(['h1'], main);

    for (const target of [document.body, heading]) {
        document.activeElement = target;
        const press = keydown(target, { key: 'H', shiftKey: true });
        listeners.window.keydown(press.event);
        assert.equal(document.activeElement, field);
        assert.equal(press.prevented(), false, 'the browser delivers the character to the field');
    }
    assert.deepEqual(field.selection, [5, 5], 'first focus puts the caret after the draft');
});

test('controls, fields, dialogs and claimed keys keep their keys', () => {
    const { listeners, document, main, add } = boot();
    const field = add(['textarea'], main, { 'data-primary-input': 'end' });
    const button = add(['button'], main);
    const other = add(['input'], main);
    const chip = add(['[role="radio"]'], add(['[role="radiogroup"]'], main));
    const editor = add(['[contenteditable]:not([contenteditable="false"])'], main);

    for (const target of [button, other, chip, editor]) {
        document.activeElement = target;
        listeners.window.keydown(keydown(target).event);
        assert.equal(document.activeElement, target);
    }

    document.activeElement = document.body;
    for (const init of [
        { defaultPrevented: true }, { ctrlKey: true, key: 'c' }, { metaKey: true }, { altKey: true },
        { key: ' ' }, { key: 'Enter' }, { key: 'ArrowUp' }, { key: 'Dead' }, { key: 'Process' },
        { isComposing: true }, { repeat: true },
    ]) {
        listeners.window.keydown(keydown(document.body, init).event);
        assert.equal(document.activeElement, document.body, JSON.stringify(init));
    }

    const modal = add(['[aria-modal="true"]'], document.body);
    listeners.window.keydown(keydown(document.body).event);
    assert.equal(document.activeElement, document.body, 'not under a dialog');
    modal.is = [];
    listeners.window.keydown(keydown(document.body).event);
    assert.equal(document.activeElement, field);
});

test('the field used last wins, and a hidden, inert or disabled one is passed over', () => {
    const { api, document, main, add } = boot();
    const story = add(['div'], main);
    const composer = add(['textarea'], story, { 'data-primary-input': 'end' });
    const terminal = add(['input'], add(['[inert]'], main, { inert: '' }), { 'data-primary-input': 'end' });
    const line = add(['input'], main, { 'data-primary-input': 'end' });

    assert.equal(api.current(), composer, 'the first usable one');
    line.focus();
    assert.equal(api.current(), line, 'the one last used');
    line.shown = false;
    assert.equal(api.current(), composer);
    composer.disabled = true;
    assert.equal(api.current(), null, 'the inert terminal is not offered');
    assert.ok(terminal);
    assert.equal(document.activeElement, line);
});

test('a field keeps its caret once used, and "start" keeps the top', () => {
    const { listeners, document, main, add } = boot();
    const field = add(['textarea'], main, { 'data-primary-input': 'start' });
    field.value = 'existing page';
    listeners.window.keydown(keydown(document.body).event);
    assert.equal(field.selection, null, 'the wiki source starts at the top');

    const chat = add(['input'], main, { 'data-primary-input': 'end' });
    chat.value = 'hello';
    chat.focus();
    chat.selection = [2, 2];
    document.activeElement = document.body;
    field.shown = false;
    listeners.window.keydown(keydown(document.body).event);
    assert.equal(document.activeElement, chat);
    assert.deepEqual(chat.selection, [2, 2], 'where the reader left it');
});

test('a plain mouse click in the output focuses the field; selecting, links and touch do not', () => {
    const { listeners, document, window, main, add } = boot();
    const field = add(['textarea'], main, { 'data-primary-input': 'end' });
    const output = add(['div'], main, { 'data-input-region': '' });
    const line = add(['div'], output);
    const link = add(['a[href]'], output);
    const outside = add(['div'], main);

    const pointer = type => listeners.document['pointerdown:capture']({ pointerType: type });

    pointer('mouse');
    for (const [target, init] of [[link, {}], [outside, {}], [line, { detail: 2 }], [line, { shiftKey: true }],
        [line, { button: 1 }], [line, { defaultPrevented: true }]]) {
        listeners.document.click(click(target, init));
        assert.equal(document.activeElement, document.body);
    }
    window.selected = true;
    listeners.document.click(click(line));
    assert.equal(document.activeElement, document.body, 'a drag that selected text keeps its selection');
    window.selected = false;

    pointer('touch');
    listeners.document.click(click(line));
    assert.equal(document.activeElement, document.body, 'a tap does not open the keyboard');

    pointer('mouse');
    listeners.document.click(click(line));
    assert.equal(document.activeElement, field);
});

test('arriving focuses the field after the router, unless the reader is busy or on a touch screen', () => {
    {
        const { api, document, main, add, flush } = boot();
        const field = add(['textarea'], main, { 'data-primary-input': 'end' });
        const nav = add(['a[href]'], document.body);
        document.activeElement = nav;
        api.arrive(field);
        assert.equal(document.activeElement, nav, 'deferred a turn');
        flush();
        assert.equal(document.activeElement, field, 'the site navigation hands focus over');
    }
    {
        const { api, document, main, add, flush } = boot();
        const field = add(['textarea'], main, { 'data-primary-input': 'end' });
        const tab = add(['[role="tab"]'], main);
        document.activeElement = tab;
        api.arrive(field);
        flush();
        assert.equal(document.activeElement, tab, "the page's own control keeps it");
        const search = add(['input'], document.body);
        document.activeElement = search;
        api.arrive(field);
        flush();
        assert.equal(document.activeElement, search, 'a field anywhere keeps it');
    }
    {
        const { api, document, main, add, flush } = boot({ coarse: true });
        const field = add(['textarea'], main, { 'data-primary-input': 'end', id: 'mc-body' });
        api.arrive('mc-body');
        flush();
        assert.equal(document.activeElement, document.body, 'no keyboard pops up over the page');
        assert.ok(field);
    }
});

test('mark makes a library-rendered field the main one', () => {
    const { api, main, add } = boot();
    const body = add(['textarea'], main, { id: 'mc-body' });
    api.mark('mc-body', 'start');
    assert.equal(body.getAttribute('data-primary-input'), 'start');
    assert.equal(api.current(), body);
});

test('the skip links focus the text box, or the page when it has none', () => {
    const { listeners, document, main, add } = boot();
    const toInput = add(['a[data-skip]'], document.body, { 'data-skip': 'input' });
    const toMain = add(['a[data-skip]'], document.body, { 'data-skip': 'main' });

    const first = click(toInput, { detail: 0 });
    listeners.document['click:capture'](first);
    assert.equal(first.defaultPrevented, true, 'not a navigation');
    assert.equal(document.activeElement, main, 'no text box: the page');

    const field = add(['textarea'], main, { 'data-primary-input': 'end' });
    listeners.document['click:capture'](click(toInput, { detail: 0 }));
    assert.equal(document.activeElement, field);

    listeners.document['click:capture'](click(toMain, { detail: 0 }));
    assert.equal(document.activeElement, main);
});
