// Portal interop helpers are loaded at startup, independently of the editor assets.
(function () {
    "use strict";
    window.SharpMUSH = window.SharpMUSH || {};

    // Puts a scrolling container (the onboarding shell between wizard steps) back at its top.
    window.SharpMUSH.scrollToTop = function (selector) {
        var el = document.querySelector(selector);
        if (el) el.scrollTop = 0;
    };

    // Brings the element with this id into view: an application row a link's #fragment names, drawn after
    // the page loaded and so too late for the browser's own jump.
    window.SharpMUSH.scrollToId = function (id) {
        var el = document.getElementById(id);
        if (el) el.scrollIntoView({ block: "start" });
    };

    // The terminal output a pointer went down on (a drag of its scrollbar), until it comes up anywhere.
    // One pair of window listeners for every terminal: a pair per terminal stayed on window after the
    // terminal unmounted and kept its element, with its scrollback, alive.
    var heldTerminal = null;
    var releaseTerminal = function () { heldTerminal = null; };
    window.addEventListener('pointerup', releaseTerminal, { passive: true });
    window.addEventListener('pointercancel', releaseTerminal, { passive: true });

    window.SharpMUSH.Terminal = {
        // Follows new output only while the reader is at the bottom, as a telnet client does: scrolled up to
        // reread something, the view stays where it is, the container gets sharp-terminal--reading (which
        // shows the jump button) and, once output arrives meanwhile, sharp-terminal--new.
        scrollToBottom: function (elementId) {
            var el = document.getElementById(elementId);
            if (!el) return;
            var box = el.closest('.sharp-terminal-container') || el;
            if (!el._sharpmushFollow) {
                // Only the reader leaves the bottom. A scroll the page causes (lines re-rendered, a
                // reflow while the first screen arrives) can land above it too, and latching that as
                // "reading" froze a fresh terminal at its top with the jump button showing.
                var mark = function () { el._sharpmushUserAt = Date.now(); };
                el.addEventListener('wheel', mark, { passive: true });
                el.addEventListener('touchmove', mark, { passive: true });
                el.addEventListener('keydown', mark, { passive: true });
                el.addEventListener('pointerdown', function () { heldTerminal = el; mark(); }, { passive: true });
                el._sharpmushFollow = function () {
                    var reading = el.scrollHeight - el.scrollTop - el.clientHeight > 48;
                    var byReader = heldTerminal === el || Date.now() - (el._sharpmushUserAt || 0) < 1000;
                    if (reading && !byReader && !el._sharpmushReading) {
                        el.scrollTop = el.scrollHeight;
                        return;
                    }
                    el._sharpmushReading = reading;
                    box.classList.toggle('sharp-terminal--reading', reading);
                    if (!reading) box.classList.remove('sharp-terminal--new');
                };
                el.addEventListener('scroll', el._sharpmushFollow, { passive: true });
                // Hidden (display: none, as Play's terminal is under a channel view), the output has no height,
                // so output arriving meanwhile cannot move it to the bottom; shown again, the browser puts back
                // the old scroll offset without a scroll event, a screen or more above the newest line. A
                // follower is put back at the bottom whenever its size changes, which includes being shown.
                if (typeof ResizeObserver !== 'undefined') {
                    new ResizeObserver(function () {
                        if (!el._sharpmushReading) el.scrollTop = el.scrollHeight;
                    }).observe(el);
                }
            }
            if (el._sharpmushReading) {
                box.classList.add('sharp-terminal--new');
                return;
            }
            el.scrollTop = el.scrollHeight;
        },

        // Back to the newest line, following again (the jump button, or the player sending a command).
        jumpToLatest: function (elementId) {
            var el = document.getElementById(elementId);
            if (!el) return;
            el._sharpmushReading = false;
            el.scrollTop = el.scrollHeight;
            var box = el.closest('.sharp-terminal-container') || el;
            box.classList.remove('sharp-terminal--reading', 'sharp-terminal--new');
        },

        // Registers delegated handlers on the terminal output container. Clicking — or
        // pressing Enter/Space on — a command link (<a xch_cmd="...">) runs the command via
        // .NET (RunCommandLinkAsync) instead of navigating. Command links carry
        // role="button" tabindex="0" so they are keyboard-focusable. Plain <a href> links are
        // left untouched so they open normally. Returns a disposable for cleanup.
        attachCommandLinks: function (outputId, dotNetRef) {
            var el = document.getElementById(outputId);
            if (!el) return { dispose: function () { } };

            function run(a) {
                var cmd = a.getAttribute('xch_cmd');
                if (cmd) dotNetRef.invokeMethodAsync('RunCommandLinkAsync', cmd);
            }

            function onClick(e) {
                var a = e.target.closest('a[xch_cmd]');
                if (!a || !el.contains(a)) return;
                e.preventDefault();
                run(a);
            }

            function onKeyDown(e) {
                if (e.key !== 'Enter' && e.key !== ' ' && e.key !== 'Spacebar') return;
                var a = e.target.closest('a[xch_cmd]');
                if (!a || !el.contains(a)) return;
                e.preventDefault();
                run(a);
            }

            el.addEventListener('click', onClick);
            el.addEventListener('keydown', onKeyDown);
            return {
                dispose: function () {
                    el.removeEventListener('click', onClick);
                    el.removeEventListener('keydown', onKeyDown);
                },
            };
        },
    };

    // ── Wiki editor: wrap the current textarea selection with markdown markup ──
    // Mirrors the prototype WikiEditScreen toolbar. Dispatches an 'input' event so
    // Blazor's @oninput binding picks up the new value, then restores the selection.
    window.SharpMUSH.Wiki = {
        wrap: function (id, before, after, block) {
            var ta = document.getElementById(id);
            if (!ta) return;
            var s = ta.selectionStart, e = ta.selectionEnd;
            var val = ta.value;
            var sel = val.slice(s, e) || (block ? '' : 'text');
            var pre = (block && s > 0 && val[s - 1] !== '\n') ? '\n' : '';
            ta.value = val.slice(0, s) + pre + before + sel + after + val.slice(e);
            ta.dispatchEvent(new Event('input', { bubbles: true }));
            var pos = s + pre.length + before.length;
            ta.focus();
            ta.setSelectionRange(pos, pos + sel.length);
        },
    };

    // ── Help Drawer bridge ─────────────────────────────────────────────────────
    // Called by HelpDrawer.razor via JS interop to register a link interceptor on
    // the help content container. Also exposes window.SharpMUSH.Help.show(name)
    // so the Monaco hover command can open the drawer without needing a DotNetRef.
    window.SharpMUSH.Help = {
        _dotNetRef: null,

        // Called from HelpDrawer OnAfterRenderAsync — stores the DotNetRef and
        // attaches a click delegate on document to intercept help: links.
        registerLinkInterceptor: function (dotNetRef) {
            window.SharpMUSH.Help._dotNetRef = dotNetRef;

            function onClick(e) {
                var a = e.target.closest('a[href^="help:"]');
                if (!a) return;
                e.preventDefault();
                var name = a.getAttribute('href').slice(5); // strip "help:"
                dotNetRef.invokeMethodAsync('JsNavigateTo', name);
            }

            document.addEventListener('click', onClick);

            // Return a disposable object that removes the listener on dispose
            return {
                dispose: function () {
                    document.removeEventListener('click', onClick);
                    window.SharpMUSH.Help._dotNetRef = null;
                },
            };
        },

        // Called from Monaco sharpmush.showHelp command
        show: function (name) {
            var ref = window.SharpMUSH.Help._dotNetRef;
            if (ref && name) ref.invokeMethodAsync('JsNavigateTo', name);
        },
    };

    // ── File downloads ─────────────────────────────────────────────────────────
    // Hands text generated in .NET to the browser as a download, through a Blob URL.
    // The URL is released on a delay, not straight after the click: some browsers
    // start reading the blob asynchronously and drop a download revoked under them.
    window.SharpMUSH.Files = {
        saveText: function (fileName, mimeType, text) {
            var url = URL.createObjectURL(new Blob([text], { type: mimeType }));
            var a = document.createElement('a');
            a.href = url;
            a.download = fileName;
            a.click();
            setTimeout(function () { URL.revokeObjectURL(url); }, 10000);
        },
    };

})();
