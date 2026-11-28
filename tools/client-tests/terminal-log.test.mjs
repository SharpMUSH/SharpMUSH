import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';

// The saved log's write path (terminal-log.js) against a small in-memory IndexedDB: which lines a batch stores,
// and how far it moves a session's log mark in sessionStorage.
const root = new URL('../../SharpMUSH.Client/wwwroot/', import.meta.url);

function fakeIndexedDb({ failAdds = () => false } = {}) {
    const rows = [];
    const later = fn => setImmediate(fn);
    const inRange = (row, r) => row.key === r.lower[0] && row.at >= r.lower[1] && row.at <= r.upper[1];
    function cursorRequest(r, dir) {
        const req = {};
        const hits = rows.filter(row => inRange(row, r)).sort((a, b) => dir === 'prev' ? b.at - a.at : a.at - b.at);
        let i = 0;
        const step = () => later(() => {
            req.result = i < hits.length
                ? { value: hits[i], continue: () => { i++; step(); }, delete: () => rows.splice(rows.indexOf(hits[i]), 1) }
                : null;
            req.onsuccess && req.onsuccess();
        });
        step();
        return req;
    }
    const db = {
        transaction() {
            const tx = { failed: false };
            const store = {
                add(row) { if (failAdds(row)) tx.failed = true; else rows.push(row); },
                index: () => ({
                    openCursor: (r, dir) => cursorRequest(r, dir),
                    count: r => {
                        const req = {};
                        later(() => req.onsuccess && req.onsuccess({ target: { result: rows.filter(row => inRange(row, r)).length } }));
                        return req;
                    }
                })
            };
            tx.objectStore = () => store;
            later(() => tx.failed ? tx.onabort && tx.onabort() : tx.oncomplete && tx.oncomplete());
            return tx;
        }
    };
    return {
        rows,
        open() { const req = {}; later(() => { req.result = db; req.onsuccess(); }); return req; }
    };
}

function fakeStorage(items = {}) {
    const map = new Map(Object.entries(items));
    return { getItem: k => map.has(k) ? map.get(k) : null, setItem: (k, v) => map.set(k, String(v)) };
}

function boot(options) {
    const idb = fakeIndexedDb(options);
    const storage = fakeStorage({ '#logged': 's1:0' });
    const window = {
        indexedDB: idb,
        sessionStorage: storage,
        setTimeout: () => 1,
        clearTimeout: () => { },
        addEventListener: () => { }
    };
    const document = { addEventListener: () => { } };
    const IDBKeyRange = { bound: (lower, upper) => ({ lower, upper }) };
    const context = vm.createContext({ window, document, IDBKeyRange, Promise });
    vm.runInContext(readFileSync(new URL('js/terminal-log.js', root), 'utf8'), context, { filename: 'js/terminal-log.js' });
    const log = window.SharpMUSH.TerminalLog;
    const mark = seq => ({ key: '#logged', session: 's1', seq });
    const append = (at, line, m) => log.append('k', at, line, 1000, 1e12, m);
    // A read writes the queue first.
    const flush = () => log.earlier('k', Infinity, 100);
    return { flush, idb, storage, mark, append };
}

test('a numbered frame is kept and moves the mark even when the clock went back', async () => {
    const { flush, idb, storage, mark, append } = boot();
    append(1000, 'one', mark(1));
    await flush();
    append(500, 'two', mark(2));
    await flush();

    assert.deepEqual(idb.rows.map(r => r.line), ['one', 'two']);
    assert.equal(storage.getItem('#logged'), 's1:2');
});

test('an unnumbered line older than the newest kept is left out', async () => {
    const { flush, idb, mark, append } = boot();
    append(1000, 'live', mark(1));
    await flush();
    append(400, 'restored', null);
    await flush();

    assert.deepEqual(idb.rows.map(r => r.line), ['live']);
});

test('after a failed write no later write moves the mark past it', async () => {
    const { flush, idb, storage, mark, append } = boot({ failAdds: row => row.line === 'lost' });
    append(1000, 'one', mark(1));
    await flush();
    append(1100, 'lost', mark(2));
    await flush();
    append(1200, 'three', mark(3));
    await flush();

    assert.deepEqual(idb.rows.map(r => r.line), ['one', 'three']);
    assert.equal(storage.getItem('#logged'), 's1:1');
});
