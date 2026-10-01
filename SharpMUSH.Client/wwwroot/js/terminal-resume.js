// A terminal's resume point (its resume token and the last frame number it showed) in sessionStorage,
// so a reload within the server's grace period resumes the session instead of starting a fresh one.
// TerminalResumeStore is the C# side.
//
// The frame number moves on every frame, so stage() keeps it in memory and a short timer writes it,
// as do pagehide and the page being hidden: those are the last moments a page is sure to run. A new
// token is written at once by write(). A stored point that lags the real one is harmless: the server
// replays from it, and a reloaded page has shown none of those frames.
//
// The terminal's screen is kept the same way: appendLine() adds one rendered line to a bounded list in
// memory, and the same timer writes it. A resumed reload shows it before the frames it missed.
(function () {
    "use strict";
    window.SharpMUSH = window.SharpMUSH || {};

    var pending = {};
    // Scrollback, per key: the line JSON strings kept, their total length, and whether storage lags them.
    var buffers = {};
    var timer = null;
    var listening = false;

    function storage() {
        try { return window.sessionStorage || null; } catch (e) { return null; }
    }

    function put(key, value) {
        var store = storage();
        if (!store) return false;
        try { store.setItem(key, value); return true; } catch (e) { return false; }
    }

    function drop(key) {
        var store = storage();
        if (!store) return;
        try { store.removeItem(key); } catch (e) { /* storage refused: nothing was kept */ }
    }

    function flush() {
        if (timer !== null) {
            window.clearTimeout(timer);
            timer = null;
        }
        var staged = pending;
        pending = {};
        for (var key in staged) {
            if (Object.prototype.hasOwnProperty.call(staged, key)) put(key, staged[key]);
        }
        for (var lines in buffers) {
            if (Object.prototype.hasOwnProperty.call(buffers, lines) && buffers[lines].dirty) {
                buffers[lines].dirty = false;
                put(lines, '[' + buffers[lines].items.join(',') + ']');
            }
        }
    }

    function schedule() {
        listen();
        if (timer === null) timer = window.setTimeout(flush, window.SharpMUSH.Resume.delayMs);
    }

    // The lines a page found stored are where appending carries on from: after a resumed reload they
    // are the screen it restored, and a fresh session cleared them before its first line.
    function buffer(key) {
        if (buffers[key]) return buffers[key];
        var items = [];
        var store = storage();
        try {
            var stored = store ? JSON.parse(store.getItem(key) || '[]') : [];
            if (Array.isArray(stored)) items = stored.map(function (line) { return JSON.stringify(line); });
        } catch (e) { /* unreadable: start empty */ }
        var chars = 2;
        items.forEach(function (line) { chars += line.length + 1; });
        buffers[key] = { items: items, chars: chars, dirty: false };
        return buffers[key];
    }

    function listen() {
        if (listening) return;
        listening = true;
        window.addEventListener('pagehide', flush);
        document.addEventListener('visibilitychange', function () {
            if (document.visibilityState === 'hidden') flush();
        });
    }

    // Only a reload resumes. A tab the browser duplicated, or one opened from this page, starts with a
    // copy of this tab's sessionStorage, and resuming there would take the session from this tab.
    function reloaded() {
        try {
            var entries = window.performance && window.performance.getEntriesByType
                ? window.performance.getEntriesByType('navigation') : [];
            return entries.length > 0 && entries[0].type === 'reload';
        } catch (e) {
            return false;
        }
    }

    window.SharpMUSH.Resume = {
        delayMs: 1000,

        // The stored point for key, or null when there is none or this page was not reloaded.
        resumable: function (key) {
            if (!reloaded()) return null;
            var store = storage();
            if (!store) return null;
            try { return store.getItem(key); } catch (e) { return null; }
        },

        // Writes now, replacing anything staged for the key. False when the browser refused storage.
        write: function (key, value) {
            delete pending[key];
            return put(key, value);
        },

        // Keeps the value in memory until the timer fires or the page goes away.
        stage: function (key, value) {
            pending[key] = value;
            schedule();
        },

        // Adds one scrollback line (a JSON object) to key's list, dropping the oldest lines past
        // maxLines or once the stored array would pass maxChars, and writes it on the same timer.
        appendLine: function (key, line, maxLines, maxChars) {
            var kept = buffer(key);
            kept.items.push(line);
            kept.chars += line.length + 1;
            while (kept.items.length > 0 && (kept.items.length > maxLines || kept.chars > maxChars)) {
                kept.chars -= kept.items.shift().length + 1;
            }
            kept.dirty = true;
            schedule();
        },

        remove: function (key) {
            delete pending[key];
            delete buffers[key];
            drop(key);
        },

        // Every key starting with prefix, stored, staged or buffered.
        removeAll: function (prefix) {
            for (var staged in pending) {
                if (staged.indexOf(prefix) === 0) delete pending[staged];
            }
            for (var lines in buffers) {
                if (lines.indexOf(prefix) === 0) delete buffers[lines];
            }
            var store = storage();
            if (!store) return;
            try {
                var keys = [];
                for (var i = 0; i < store.length; i++) {
                    var key = store.key(i);
                    if (key !== null && key.indexOf(prefix) === 0) keys.push(key);
                }
                keys.forEach(drop);
            } catch (e) { /* storage refused: nothing was kept */ }
        },

        flush: flush
    };
})();
