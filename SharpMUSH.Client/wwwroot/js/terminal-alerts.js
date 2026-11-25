// Play's highlight alerts: the sounds a rule plays and the browser notifications it raises. TerminalAlerts is
// the C# side.
//
// The sounds are made here with Web Audio, so there are no files to fetch or license. A burst of matching lines
// (a replay after a reconnect, a busy channel) plays one sound, not one for each line: a sound inside
// minGapMs of the last is skipped. A notification is raised only while the tab is not in front, and each
// rule's replaces the one before it (the rule's id is its tag), so a burst leaves one.
(function () {
    "use strict";
    window.SharpMUSH = window.SharpMUSH || {};

    var context = null;
    var lastSound = -Infinity;

    function audio() {
        if (context) return context;
        var Ctor = window.AudioContext || window.webkitAudioContext;
        if (!Ctor) return null;
        try { context = new Ctor(); } catch (e) { context = null; }
        return context;
    }

    // One soft note: a sine that rises quickly and dies away, so it never clicks.
    function note(ctx, frequency, at, length, peak) {
        var osc = ctx.createOscillator();
        var gain = ctx.createGain();
        osc.type = "sine";
        osc.frequency.setValueAtTime(frequency, at);
        gain.gain.setValueAtTime(0.0001, at);
        gain.gain.exponentialRampToValueAtTime(peak, at + 0.015);
        gain.gain.exponentialRampToValueAtTime(0.0001, at + length);
        osc.connect(gain).connect(ctx.destination);
        osc.start(at);
        osc.stop(at + length + 0.02);
    }

    var sounds = {
        Chime: function (ctx, t) { note(ctx, 659.25, t, 0.35, 0.18); note(ctx, 987.77, t + 0.12, 0.5, 0.15); },
        Ping: function (ctx, t) { note(ctx, 1318.5, t, 0.25, 0.14); },
        Knock: function (ctx, t) { note(ctx, 196, t, 0.12, 0.3); note(ctx, 196, t + 0.16, 0.12, 0.3); }
    };

    function supported() { return typeof window.Notification === "function"; }

    function inFront() {
        return document.visibilityState === "visible" && (typeof document.hasFocus !== "function" || document.hasFocus());
    }

    window.SharpMUSH.Alerts = {
        minGapMs: 1000,

        // Plays the named sound. False when it was skipped (too soon after the last) or the browser has no
        // Web Audio, or will not play before the page has been clicked.
        play: function (name) {
            var make = sounds[name];
            var ctx = make ? audio() : null;
            if (!ctx) return false;
            var now = Date.now();
            if (now - lastSound < window.SharpMUSH.Alerts.minGapMs) return false;
            lastSound = now;
            if (ctx.state === "suspended") { try { ctx.resume(); } catch (e) { /* needs a click first */ } }
            make(ctx, ctx.currentTime + 0.01);
            return true;
        },

        // "granted", "denied", "default", or "unsupported".
        permission: function () {
            return supported() ? window.Notification.permission : "unsupported";
        },

        // Asks the browser for permission to notify. Must run from a click. Answers as permission() does.
        requestPermission: async function () {
            if (!supported()) return "unsupported";
            if (window.Notification.permission !== "default") return window.Notification.permission;
            try { return await window.Notification.requestPermission(); } catch (e) { return window.Notification.permission; }
        },

        // Raises a notification while the tab is not in front. Clicking it brings the tab back. False when
        // nothing was shown.
        notify: function (title, body, tag) {
            if (!supported() || window.Notification.permission !== "granted" || inFront()) return false;
            try {
                var shown = new window.Notification(title, { body: body, tag: tag, renotify: false });
                shown.onclick = function () { window.focus(); shown.close(); };
                return true;
            } catch (e) {
                // Some phones notify only through a service worker; there it is left out.
                return false;
            }
        },

        // For the rule editor's preview: plays a sound however recently the last one played.
        preview: function (name) {
            lastSound = -Infinity;
            return window.SharpMUSH.Alerts.play(name);
        }
    };
})();
