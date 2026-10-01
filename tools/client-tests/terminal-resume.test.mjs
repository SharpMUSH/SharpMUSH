import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';

// The terminal's resume point in sessionStorage (terminal-resume.js). The frame number changes on every
// frame, so it is staged in memory and written on a short timer, or when the page is hidden or unloaded;
// a new token is written at once. A stored point is offered back only to a page that was reloaded.
const root = new URL('../../SharpMUSH.Client/wwwroot/', import.meta.url);

function fakeStorage() {
    const items = new Map();
    let writes = 0;
    return {
        items,
        writes: () => writes,
        get length() { return items.size; },
        key: i => [...items.keys()][i] ?? null,
        getItem: k => items.has(k) ? items.get(k) : null,
        setItem: (k, v) => { writes++; items.set(k, String(v)); },
        removeItem: k => { items.delete(k); }
    };
}

function boot({ navigation = 'reload', storage = fakeStorage() } = {}) {
    const timers = [];
    const windowListeners = new Map();
    const documentListeners = new Map();
    const document = {
        visibilityState: 'visible',
        addEventListener: (name, handler) => documentListeners.set(name, handler)
    };
    const window = {
        sessionStorage: storage,
        performance: { getEntriesByType: type => type === 'navigation' ? [{ type: navigation }] : [] },
        setTimeout: (fn, ms) => { timers.push({ fn, ms, live: true }); return timers.length; },
        clearTimeout: id => { if (timers[id - 1]) timers[id - 1].live = false; },
        addEventListener: (name, handler) => windowListeners.set(name, handler)
    };
    const context = vm.createContext({ window, document });
    vm.runInContext(readFileSync(new URL('js/terminal-resume.js', root), 'utf8'), context, { filename: 'js/terminal-resume.js' });
    const runTimers = () => { for (const t of timers.splice(0)) if (t.live) t.fn(); };
    return { resume: window.SharpMUSH.Resume, storage, timers, runTimers, windowListeners, documentListeners, document };
}

test('the page loads it from index.html', () => {
    const html = readFileSync(new URL('index.html', root), 'utf8');
    assert.match(html, /<script src="js\/terminal-resume#\[\.\{fingerprint\}\]\.js"><\/script>/);
});

test('a staged frame number is not written until the timer fires, and many frames write once', () => {
    const { resume, storage, timers, runTimers } = boot();
    for (let seq = 1; seq <= 50; seq++) resume.stage('k', `{"token":"t","lastSeq":${seq}}`);

    assert.equal(storage.writes(), 0, 'staging must not touch sessionStorage per frame');
    assert.equal(timers.filter(t => t.live).length, 1, 'one timer for a burst of frames');
    runTimers();
    assert.equal(storage.writes(), 1);
    assert.equal(storage.getItem('k'), '{"token":"t","lastSeq":50}');
});

test('pagehide and a hidden page flush what is staged', () => {
    const { resume, storage, windowListeners, documentListeners, document } = boot();
    resume.stage('k', 'one');
    windowListeners.get('pagehide')();
    assert.equal(storage.getItem('k'), 'one');

    resume.stage('k', 'two');
    document.visibilityState = 'visible';
    documentListeners.get('visibilitychange')();
    assert.equal(storage.getItem('k'), 'one', 'becoming visible writes nothing');
    document.visibilityState = 'hidden';
    documentListeners.get('visibilitychange')();
    assert.equal(storage.getItem('k'), 'two');
});

test('a write goes through at once and replaces what was staged', () => {
    const { resume, storage, runTimers } = boot();
    resume.stage('k', 'old-token');
    assert.equal(resume.write('k', 'new-token'), true);
    assert.equal(storage.getItem('k'), 'new-token');
    runTimers();
    assert.equal(storage.getItem('k'), 'new-token', 'the stale staged value must not land after the write');
});

test('only a reloaded page is offered the stored point', () => {
    for (const navigation of ['navigate', 'back_forward', 'prerender']) {
        const storage = fakeStorage();
        storage.setItem('k', 'stored');
        const { resume } = boot({ navigation, storage });
        assert.equal(resume.resumable('k'), null, `${navigation} must start a fresh session`);
    }
    const storage = fakeStorage();
    storage.setItem('k', 'stored');
    assert.equal(boot({ storage }).resume.resumable('k'), 'stored');
});

test('remove and removeAll drop stored and staged values', () => {
    const { resume, storage, runTimers } = boot();
    storage.setItem('sharpmush.resume.play.a', '1');
    storage.setItem('sharpmush.resume.portal.a', '2');
    storage.setItem('sharpmush.account.sessionToken', 'keep');
    resume.stage('sharpmush.resume.play.b', '3');
    resume.remove('sharpmush.resume.play.a');
    assert.equal(storage.getItem('sharpmush.resume.play.a'), null);

    resume.removeAll('sharpmush.resume.');
    runTimers();
    assert.deepEqual([...storage.items.keys()], ['sharpmush.account.sessionToken']);
});

test('a browser that refuses storage is a store with nothing in it', () => {
    const refusing = {
        get length() { throw new Error('denied'); },
        key: () => { throw new Error('denied'); },
        getItem: () => { throw new Error('denied'); },
        setItem: () => { throw new Error('denied'); },
        removeItem: () => { throw new Error('denied'); }
    };
    const { resume, runTimers } = boot({ storage: refusing });
    assert.equal(resume.resumable('k'), null);
    assert.equal(resume.write('k', 'v'), false);
    resume.stage('k', 'v');
    runTimers();
    resume.remove('k');
    resume.removeAll('sharpmush.resume.');
});
