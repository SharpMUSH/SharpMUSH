// Portal interop helpers are loaded at startup, independently of the editor assets.
(function () {
    "use strict";
    window.SharpMUSH = window.SharpMUSH || {};

    window.SharpMUSH.Terminal = {
        scrollToBottom: function (elementId) {
            var el = document.getElementById(elementId);
            if (el) el.scrollTop = el.scrollHeight;
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

})();
