// The longer terminal log a player turned on in Play's settings ("Keep longer logs"), in IndexedDB, one
// list of lines per account and character. TerminalLog is the C# side, and decides what a line holds.
//
// append() queues a line and a short timer writes the queue in one transaction. A line older than the
// newest one kept for its key is left out: a refused resume shows the earlier screen again, and those
// lines are kept already. Every few hundred lines the key is pruned to its age and line limits.
//
// Nothing here outlives clearAll(), which empties the store when the player turns the setting off.
(function () {
    "use strict";
    window.SharpMUSH = window.SharpMUSH || {};

    var NAME = 'sharpmush-terminal-log';
    var STORE = 'lines';
    var opening = null;
    var queue = [];
    var timer = null;
    // Per key: the newest time kept (null until read from the store), and lines written since the last prune.
    var keys = {};

    function request(req) {
        return new Promise(function (resolve, reject) {
            req.onsuccess = function () { resolve(req.result); };
            req.onerror = function () { reject(req.error); };
        });
    }

    function done(tx) {
        return new Promise(function (resolve, reject) {
            tx.oncomplete = function () { resolve(); };
            tx.onerror = tx.onabort = function () { reject(tx.error); };
        });
    }

    function open() {
        if (opening) return opening;
        opening = new Promise(function (resolve, reject) {
            var idb;
            try { idb = window.indexedDB; } catch (e) { idb = null; }
            if (!idb) { reject(new Error('IndexedDB is not available')); return; }
            var req = idb.open(NAME, 1);
            req.onupgradeneeded = function () {
                var store = req.result.createObjectStore(STORE, { keyPath: 'id', autoIncrement: true });
                store.createIndex('byKey', ['key', 'at']);
            };
            req.onsuccess = function () {
                // Another tab deleting or upgrading the database: let go, and open again next time.
                req.result.onversionchange = function () { req.result.close(); opening = null; };
                resolve(req.result);
            };
            req.onerror = function () { reject(req.error); };
        });
        opening.catch(function () { opening = null; });
        return opening;
    }

    function range(key, from, to) {
        return IDBKeyRange.bound([key, from], [key, to]);
    }

    // The newest time kept for key, from the store.
    function newest(db, key) {
        var index = db.transaction(STORE).objectStore(STORE).index('byKey');
        return request(index.openCursor(range(key, -Infinity, Infinity), 'prev'))
            .then(function (cursor) { return cursor ? cursor.value.at : -Infinity; });
    }

    // Drops what is older than maxAgeMs, then the oldest lines past maxLines.
    function prune(db, key, maxLines, maxAgeMs) {
        var tx = db.transaction(STORE, 'readwrite');
        var index = tx.objectStore(STORE).index('byKey');
        var cutoff = Date.now() - maxAgeMs;
        var old = index.openCursor(range(key, -Infinity, cutoff), 'next');
        old.onsuccess = function () {
            var cursor = old.result;
            if (cursor) { cursor.delete(); cursor.continue(); return; }
            index.count(range(key, -Infinity, Infinity)).onsuccess = function (counted) {
                var extra = counted.target.result - maxLines;
                if (extra <= 0) return;
                var oldest = index.openCursor(range(key, -Infinity, Infinity), 'next');
                oldest.onsuccess = function () {
                    var c = oldest.result;
                    if (!c || extra <= 0) return;
                    c.delete();
                    extra--;
                    c.continue();
                };
            };
        };
        return done(tx);
    }

    function flush() {
        timer = null;
        var batch = queue;
        queue = [];
        if (batch.length === 0) return Promise.resolve();
        return open().then(function (db) {
            var unread = [];
            batch.forEach(function (item) {
                if (!keys[item.key]) keys[item.key] = { newest: null, since: 0 };
                if (keys[item.key].newest === null && unread.indexOf(item.key) < 0) unread.push(item.key);
            });
            return Promise.all(unread.map(function (key) {
                return newest(db, key).then(function (at) { if (keys[key].newest === null) keys[key].newest = at; });
            })).then(function () {
                var tx = db.transaction(STORE, 'readwrite');
                var store = tx.objectStore(STORE);
                batch.forEach(function (item) {
                    var state = keys[item.key];
                    if (item.at < state.newest) return;
                    state.newest = item.at;
                    state.since++;
                    store.add({ key: item.key, at: item.at, line: item.line });
                });
                return done(tx);
            }).then(function () {
                var prunes = [];
                batch.forEach(function (item) {
                    var state = keys[item.key];
                    if (state.since < window.SharpMUSH.TerminalLog.pruneEvery) return;
                    state.since = 0;
                    prunes.push(prune(db, item.key, item.maxLines, item.maxAgeMs));
                });
                return Promise.all(prunes);
            });
        }).catch(function () { /* storage refused or full: those lines are not kept */ });
    }

    window.SharpMUSH.TerminalLog = {
        delayMs: 250,
        pruneEvery: 200,

        // Queues one line (TerminalScrollback's JSON for it) to keep under key, at its time in ms.
        append: function (key, at, line, maxLines, maxAgeMs) {
            queue.push({ key: key, at: at, line: line, maxLines: maxLines, maxAgeMs: maxAgeMs });
            if (timer === null) timer = window.setTimeout(flush, window.SharpMUSH.TerminalLog.delayMs);
        },

        // Up to count kept lines at or before beforeAt, oldest first, as one JSON array; "[]" when there
        // are none or the store is out of reach. Lines still queued are written first.
        earlier: function (key, beforeAt, count) {
            if (timer !== null) window.clearTimeout(timer);
            return flush().then(open).then(function (db) {
                var index = db.transaction(STORE).objectStore(STORE).index('byKey');
                var lines = [];
                return new Promise(function (resolve, reject) {
                    var req = index.openCursor(range(key, -Infinity, beforeAt), 'prev');
                    req.onsuccess = function () {
                        var cursor = req.result;
                        if (!cursor || lines.length >= count) { resolve('[' + lines.reverse().join(',') + ']'); return; }
                        lines.push(cursor.value.line);
                        cursor.continue();
                    };
                    req.onerror = function () { reject(req.error); };
                });
            }).catch(function () { return '[]'; });
        },

        // Forgets every kept line, for every character: the player turned the setting off.
        clearAll: function () {
            queue = [];
            if (timer !== null) { window.clearTimeout(timer); timer = null; }
            keys = {};
            return open().then(function (db) {
                var tx = db.transaction(STORE, 'readwrite');
                tx.objectStore(STORE).clear();
                return done(tx);
            }).catch(function () { /* nothing was kept */ });
        }
    };
})();
