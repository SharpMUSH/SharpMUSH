// Play's input shortcuts: a key the player bound runs its command line (Play.razor.cs binds them; the
// chords are written as KeyChord.cs writes them: Ctrl, Alt, Shift, then the key's code).
//
// A bound key works anywhere on Play except in another field, or while a dialog or a menu is open over the page,
// so a key pressed while the output or a sidebar control has focus still runs, and one pressed while writing a mail
// or editing the shortcuts does not. The browser's own action for it (F1's help, F3's find) is kept from happening.
// Held down, a key runs once: a repeat is swallowed, not sent again.
//
// A key recorder (data-key-recorder, the editor's "press a key" field) keeps the browser's action from happening
// too, for any key a chord can end in, so recording F3 does not open find; the field's own handler reads the key.
(function () {
    'use strict';
    window.SharpMUSH = window.SharpMUSH || {};

    var CODES = /^(F([1-9]|10)|Numpad([0-9]|Decimal|Add|Subtract|Multiply|Divide))$/;

    function chordOf(e) {
        return (e.ctrlKey ? 'Ctrl+' : '') + (e.altKey ? 'Alt+' : '') + (e.shiftKey ? 'Shift+' : '') + e.code;
    }

    // Another field the player is typing in: the bound keys are its keys there. Play's own input box is not one.
    function inOtherField(target) {
        if (!target || !target.closest) return false;
        if (target.closest('[data-primary-input]')) return false;
        return !!target.closest('input, textarea, select, [contenteditable=""], [contenteditable="true"]');
    }

    // A dialog, or a menu or list open over the page (Play's settings, a select), has the keys while it is open.
    function overlayOpen(doc) {
        return !!doc.querySelector('[role="dialog"][aria-modal="true"], .mud-dialog-container, .mud-popover-open');
    }

    var state = { chords: new Set(), dotNet: null, listening: false };

    function onKeyDown(e) {
        if (e.isComposing || e.metaKey || !e.code) return;
        var target = e.target;
        if (target && target.closest && target.closest('[data-key-recorder]')) {
            if (CODES.test(e.code)) e.preventDefault();
            return;
        }
        if (!state.dotNet || !state.chords.has(chordOf(e))) return;
        if (inOtherField(target) || overlayOpen(document)) return;
        e.preventDefault();
        e.stopPropagation();
        if (e.repeat) return;
        state.dotNet.invokeMethodAsync('RunShortcutAsync', chordOf(e));
    }

    window.SharpMUSH.PlayKeys = {
        // Play mounted, or its keys changed: which chords to take and who runs them.
        bind: function (dotNet, chords) {
            state.dotNet = dotNet;
            state.chords = new Set(chords || []);
            if (!state.listening) {
                // Capture: before input-focus.js and the input box's own handlers see the key.
                document.addEventListener('keydown', onKeyDown, true);
                state.listening = true;
            }
        },
        // Play gone: no key runs anything. A page that is not the one bound (an older Play disposed after a newer one
        // bound) leaves the keys alone.
        unbind: function (dotNet) {
            if (dotNet && state.dotNet && dotNet._id !== state.dotNet._id) return;
            state.dotNet = null;
            state.chords = new Set();
        },
        chordOf: chordOf,
    };
})();
