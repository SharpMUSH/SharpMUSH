// The play terminal's saved log ("Save log in this browser" in Play's settings, on until turned off), in IndexedDB, one
// list of lines per account and character. TerminalLog is the C# side, and decides what a line holds.
//
// append() queues a line and a short timer writes the queue in one transaction, as do pagehide and the page being
// hidden, so a reload keeps the lines it showed last. An unnumbered line older than the
// newest one kept for its key is left out: a refused resume shows the earlier screen again, and those
// lines are kept already. A numbered server frame is kept whatever its time. Every few hundred lines the key is pruned to its age and line limits.
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

    var listening = false;
    // Set once a write fails: from then on no mark moves in this page, since a later write that is stored must not
    // pass over the frames that were not. A reload then logs the server's replay again, as it did before marks.
    var holdMarks = false;

    // After a batch is stored: each resume slot's mark ("<session>:<frame>", TerminalResumeStore.LoggedKeyFor) moves to
    // the newest frame stored for it, but only while it still names that session. A slot cleared for a fresh session,
    // or forgotten at sign-out, is left as it is.
    function moveMarks(batch) {
        if (holdMarks) return;
        var store;
        try { store = window.sessionStorage; } catch (e) { return; }
        if (!store) return;
        batch.forEach(function (item) {
            var mark = item.mark;
            if (!mark) return;
            try {
                var current = (store.getItem(mark.key) || '').split(':');
                if (current.length !== 2 || current[0] !== mark.session || Number(current[1]) >= mark.seq) return;
                store.setItem(mark.key, mark.session + ':' + mark.seq);
            } catch (e) { /* storage refused: a reload may log those frames again */ }
        });
    }

    function listen() {
        if (listening) return;
        listening = true;
        window.addEventListener('pagehide', function () { if (queue.length > 0) flush(); });
        document.addEventListener('visibilitychange', function () {
            if (document.visibilityState === 'hidden' && queue.length > 0) flush();
        });
    }

    // Writes what is queued, after any write already under way: a read that flushes first then sees every line
    // queued before it, even ones a timer's flush took from the queue and has not finished writing.
    var writing = Promise.resolve();

    function flush() {
        if (timer !== null) window.clearTimeout(timer);
        timer = null;
        var batch = queue;
        queue = [];
        writing = writing.then(function () { return write(batch); });
        return writing;
    }

    function write(batch) {
        if (batch.length === 0) return Promise.resolve();
        var added = [];
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
                    // The time guard is for unnumbered lines (a restored screen shown again). A server frame that
                    // claimed its number is new whatever the clock says, and its mark will pass it.
                    if (!item.mark && item.at < state.newest) return;
                    if (!(item.at < state.newest)) state.newest = item.at;
                    state.since++;
                    store.add({ key: item.key, at: item.at, line: item.line });
                    added.push(item);
                });
                return done(tx);
            }).then(function () {
                moveMarks(added);
                var prunes = [];
                batch.forEach(function (item) {
                    var state = keys[item.key];
                    if (state.since < window.SharpMUSH.TerminalLog.pruneEvery) return;
                    state.since = 0;
                    prunes.push(prune(db, item.key, item.maxLines, item.maxAgeMs));
                });
                return Promise.all(prunes);
            });
        }).catch(function () {
            // Storage refused or full: those lines are not kept, and no mark moves past them.
            holdMarks = true;
        });
    }

    window.SharpMUSH.TerminalLog = {
        delayMs: 250,
        pruneEvery: 200,

        // Queues one line (TerminalScrollback's JSON for it) to keep under key, at its time in ms.
        // mark, when given, is {key, session, seq}: once the line is stored, the session's mark in sessionStorage moves to seq.
        append: function (key, at, line, maxLines, maxAgeMs, mark) {
            listen();
            queue.push({ key: key, at: at, line: line, maxLines: maxLines, maxAgeMs: maxAgeMs, mark: mark || null });
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

        // Every kept line from fromAt to toAt (both included), oldest first, as one JSON array, for an export;
        // null when the store is out of reach. Lines still queued are written first.
        between: function (key, fromAt, toAt) {
            if (timer !== null) window.clearTimeout(timer);
            return flush().then(open).then(function (db) {
                var index = db.transaction(STORE).objectStore(STORE).index('byKey');
                var lines = [];
                return new Promise(function (resolve, reject) {
                    var req = index.openCursor(range(key, fromAt, toAt), 'next');
                    req.onsuccess = function () {
                        var cursor = req.result;
                        if (!cursor) { resolve('[' + lines.join(',') + ']'); return; }
                        lines.push(cursor.value.line);
                        cursor.continue();
                    };
                    req.onerror = function () { reject(req.error); };
                });
            }).catch(function () { return null; });
        },

        // What an exported HTML log needs to look as the terminal does: the markup stylesheet, the values the portal's
        // theme gives the named custom properties and the markup's own now, and the portal's origin, for pictures the
        // game sent by path. One JSON object; the stylesheet is empty when it cannot be fetched.
        exportStyle: function (names) {
            var vars = {};
            var root = window.getComputedStyle(document.documentElement);
            names.forEach(function (name) { vars[name] = root.getPropertyValue(name).trim(); });
            // The terminal's own pane: its text colour, and the first background behind it that is not transparent.
            var pane = document.querySelector('.sharp-terminal-output');
            if (pane) {
                // The markup's own colours (ansi(), layout borders, titles), as the theme sets them for the terminal.
                var paneStyle = window.getComputedStyle(pane);
                for (var i = 0; i < paneStyle.length; i++) {
                    if (paneStyle[i].indexOf('--ms-') === 0) vars[paneStyle[i]] = paneStyle.getPropertyValue(paneStyle[i]).trim();
                }
                vars['--log-fg'] = window.getComputedStyle(pane).color;
                for (var el = pane; el; el = el.parentElement) {
                    var bg = window.getComputedStyle(el).backgroundColor;
                    if (bg && bg !== 'transparent' && !/rgba\(.*,\s*0\)$/.test(bg)) { vars['--log-bg'] = bg; break; }
                }
            }
            return window.fetch('data/markup.css')
                .then(function (r) { return r.ok ? r.text() : ''; })
                .catch(function () { return ''; })
                .then(function (css) { return JSON.stringify({ css: css, vars: vars, origin: window.location.origin }); });
        },

        // Forgets every kept line, for every character: the player turned the setting off.
        clearAll: function () {
            queue = [];
            if (timer !== null) { window.clearTimeout(timer); timer = null; }
            // After any write already under way, failed or not, so none lands once the store is empty.
            writing = writing.catch(function () { }).then(function () {
                keys = {};
                return open().then(function (db) {
                    var tx = db.transaction(STORE, 'readwrite');
                    tx.objectStore(STORE).clear();
                    return done(tx);
                });
            }).catch(function () { /* nothing was kept */ });
            return writing;
        }
    };
})();
