// Portal interop helpers are loaded at startup, independently of the editor assets.
(function () {
    "use strict";
    window.SharpMUSH = window.SharpMUSH || {};

    // The browser's time zone, such as Europe/Paris, or "" when it gives none. The terminal reports it to the game
    // as a telnet client reports TZ through NEW-ENVIRON.
    window.SharpMUSH.timeZone = function () {
        try {
            return Intl.DateTimeFormat().resolvedOptions().timeZone || "";
        } catch (e) {
            return "";
        }
    };

    // Puts a scrolling container (the onboarding shell between wizard steps) back at its top.
    window.SharpMUSH.scrollToTop = function (selector) {
        var el = document.querySelector(selector);
        if (el) el.scrollTop = 0;
    };

    // Moves focus to a heading (a wizard step's title once the step changes), so a keyboard or screen-reader
    // user carries on from there rather than from the top of the page. A heading takes focus only from script.
    window.SharpMUSH.focusHeading = function (selector) {
        var el = document.querySelector(selector);
        if (!el) return;
        if (!el.hasAttribute("tabindex")) el.setAttribute("tabindex", "-1");
        el.focus({ preventScroll: true });
    };

    // Brings the element with this id into view: an application row a link's #fragment names, drawn after
    // the page loaded and so too late for the browser's own jump.
    window.SharpMUSH.scrollToId = function (id) {
        var el = document.getElementById(id);
        if (el) el.scrollIntoView({ block: "start" });
    };

    // The room a World map frame has for its drawing, as [width, height]: its width, and the tallest it may grow,
    // less the space kept below the drawing for labels.
    window.SharpMUSH.mapFrameSize = function (frame) {
        var figure = frame && frame.firstElementChild;
        if (!figure) return null;
        var tallest = parseFloat(getComputedStyle(frame).maxHeight);
        var below = parseFloat(getComputedStyle(figure).marginBottom) || 0;
        return [frame.clientWidth, (isFinite(tallest) ? tallest : frame.clientHeight) - below];
    };

    // Keeps one point of a World map's view in place after its drawing grew (or shrank) by ratio: the point at
    // (x, y) in the frame, its middle when none is given. A drawing narrower than its frame was centred in it, so the
    // point is measured from where the drawing began.
    window.SharpMUSH.mapRescale = function (frame, ratio, x, y) {
        var figure = frame && frame.firstElementChild;
        if (!figure || !(ratio > 0)) return;
        var oldWidth = figure.offsetWidth / ratio;
        var oldHeight = figure.offsetHeight / ratio;
        var atX = typeof x === "number" ? x : frame.clientWidth / 2;
        var atY = typeof y === "number" ? y : Math.min(frame.clientHeight, oldHeight) / 2;
        var onX = frame.scrollLeft + atX - Math.max(0, (frame.clientWidth - oldWidth) / 2);
        var onY = frame.scrollTop + atY;
        frame.scrollLeft = onX * ratio - atX;
        frame.scrollTop = onY * ratio - atY;
    };

    // Pans and zooms a World map's frame by hand. A mouse drags the view (a click after a drag chooses nothing);
    // a pinch on a touch screen or trackpad, or the wheel with Ctrl, zooms about the point under it. One finger and
    // the plain wheel scroll the frame as the browser does. Zooms go to the page as factors, at most one a frame:
    // dotNet's ZoomAt(factor, x, y), with (x, y) the point in the frame to hold still.
    window.SharpMUSH.mapGestures = function (frame, dotNet) {
        if (!frame || !dotNet) return { dispose: function () { } };
        var pending = 1, at = null, queued = false;

        function zoom(factor, x, y) {
            pending *= factor;
            at = [x, y];
            if (queued) return;
            queued = true;
            window.requestAnimationFrame(function () {
                queued = false;
                var factor = pending;
                pending = 1;
                if (Math.abs(factor - 1) > 0.001) dotNet.invokeMethodAsync("ZoomAt", factor, at[0], at[1]);
            });
        }

        function local(clientX, clientY) {
            var box = frame.getBoundingClientRect();
            return [clientX - box.left - frame.clientLeft, clientY - box.top - frame.clientTop];
        }

        function onWheel(e) {
            if (!e.ctrlKey) return;
            e.preventDefault();
            var p = local(e.clientX, e.clientY);
            zoom(Math.exp(-e.deltaY * 0.002), p[0], p[1]);
        }

        // A mouse drag: past a few pixels it pans, and the click that ends it is swallowed.
        var drag = null;
        function onPointerDown(e) {
            if (e.pointerType !== "mouse" || e.button !== 0) return;
            drag = { x: e.clientX, y: e.clientY, left: frame.scrollLeft, top: frame.scrollTop, moved: false };
            window.addEventListener("pointermove", onPointerMove);
            window.addEventListener("pointerup", onPointerUp);
        }
        function onPointerMove(e) {
            if (!drag) return;
            var dx = e.clientX - drag.x, dy = e.clientY - drag.y;
            if (!drag.moved && Math.abs(dx) + Math.abs(dy) < 5) return;
            if (!drag.moved) frame.classList.add("map-frame--dragging");
            drag.moved = true;
            frame.scrollLeft = drag.left - dx;
            frame.scrollTop = drag.top - dy;
            e.preventDefault();
        }
        function onPointerUp() {
            window.removeEventListener("pointermove", onPointerMove);
            window.removeEventListener("pointerup", onPointerUp);
            if (drag && drag.moved) {
                frame.classList.remove("map-frame--dragging");
                frame.addEventListener("click", swallow, { capture: true, once: true });
                // A drag that ends off the frame has no click to swallow; the next real one must still count.
                window.setTimeout(function () { frame.removeEventListener("click", swallow, { capture: true }); }, 0);
            }
            drag = null;
        }
        function swallow(e) {
            e.preventDefault();
            e.stopPropagation();
        }

        // Two fingers: their spread zooms and their middle pans.
        var pinch = null;
        function measure(touches) {
            var a = touches[0], b = touches[1];
            return {
                spread: Math.hypot(a.clientX - b.clientX, a.clientY - b.clientY),
                x: (a.clientX + b.clientX) / 2,
                y: (a.clientY + b.clientY) / 2
            };
        }
        function onTouchStart(e) {
            pinch = e.touches.length === 2 ? measure(e.touches) : null;
        }
        function onTouchMove(e) {
            if (!pinch || e.touches.length !== 2) return;
            e.preventDefault();
            var now = measure(e.touches);
            frame.scrollLeft -= now.x - pinch.x;
            frame.scrollTop -= now.y - pinch.y;
            if (pinch.spread > 0 && now.spread > 0) {
                var p = local(now.x, now.y);
                zoom(now.spread / pinch.spread, p[0], p[1]);
            }
            pinch = now;
        }
        function onTouchEnd(e) {
            if (e.touches.length < 2) pinch = null;
        }

        frame.addEventListener("wheel", onWheel, { passive: false });
        frame.addEventListener("pointerdown", onPointerDown);
        frame.addEventListener("touchstart", onTouchStart, { passive: true });
        frame.addEventListener("touchmove", onTouchMove, { passive: false });
        frame.addEventListener("touchend", onTouchEnd);
        frame.addEventListener("touchcancel", onTouchEnd);
        return {
            dispose: function () {
                frame.removeEventListener("wheel", onWheel);
                frame.removeEventListener("pointerdown", onPointerDown);
                frame.removeEventListener("touchstart", onTouchStart);
                frame.removeEventListener("touchmove", onTouchMove);
                frame.removeEventListener("touchend", onTouchEnd);
                frame.removeEventListener("touchcancel", onTouchEnd);
                onPointerUp();
            }
        };
    };

    // Tells a World map what part of its drawing is in view, once the frame stops scrolling or changes size: dotNet's
    // ViewAt(x, y, width, height), in the drawing's pixels. A drawing narrower than its frame is centred in it, so
    // its view starts at its own edge.
    window.SharpMUSH.mapViewport = function (frame, dotNet) {
        if (!frame || !dotNet) return { dispose: function () { } };
        var timer = 0;

        function report() {
            timer = 0;
            var figure = frame.firstElementChild;
            if (!figure) return;
            var offset = Math.max(0, (frame.clientWidth - figure.offsetWidth) / 2);
            dotNet.invokeMethodAsync("ViewAt", Math.max(0, frame.scrollLeft - offset), frame.scrollTop,
                Math.min(frame.clientWidth, figure.offsetWidth), Math.min(frame.clientHeight, figure.offsetHeight));
        }
        function soon() {
            if (timer) window.clearTimeout(timer);
            timer = window.setTimeout(report, 150);
        }

        frame.addEventListener("scroll", soon, { passive: true });
        var observer = typeof window.ResizeObserver === "function" ? new window.ResizeObserver(soon) : null;
        if (observer) {
            observer.observe(frame);
            if (frame.firstElementChild) observer.observe(frame.firstElementChild);
        }
        soon();
        return {
            dispose: function () {
                frame.removeEventListener("scroll", soon);
                if (observer) observer.disconnect();
                if (timer) window.clearTimeout(timer);
                timer = 0;
            }
        };
    };

    // Scrolls a World map's frame so a room sits in the middle of its view, and the frame into the page's view,
    // then gives the room focus when asked. Smooth unless the reader asked for less motion.
    window.SharpMUSH.mapCentre = function (frame, room, focus) {
        if (!frame || !room) return;
        var still = window.matchMedia && window.matchMedia("(prefers-reduced-motion: reduce)").matches;
        var view = frame.getBoundingClientRect();
        var box = room.getBoundingClientRect();
        frame.scrollTo({
            left: frame.scrollLeft + (box.left + box.width / 2) - (view.left + frame.clientLeft + frame.clientWidth / 2),
            top: frame.scrollTop + (box.top + box.height / 2) - (view.top + frame.clientTop + frame.clientHeight / 2),
            behavior: still ? "auto" : "smooth"
        });
        frame.scrollIntoView({ block: "nearest", behavior: still ? "auto" : "smooth" });
        if (focus) room.focus({ preventScroll: true });
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
                // Removed from the page, the output is resized to nothing too: the observer lets go of it
                // then, or it would keep the unmounted terminal and its scrollback alive.
                if (typeof ResizeObserver !== 'undefined') {
                    var resized = new ResizeObserver(function () {
                        if (!el.isConnected) { resized.disconnect(); return; }
                        if (!el._sharpmushReading) el.scrollTop = el.scrollHeight;
                    });
                    resized.observe(el);
                }
            }
            if (el._sharpmushReading) {
                box.classList.add('sharp-terminal--new');
                return;
            }
            el.scrollTop = el.scrollHeight;
        },

        // Earlier lines are about to go in above everything: holdPlace() notes how far the view is from the
        // bottom, and restorePlace(), once they are in, puts it back there, so the lines the player was
        // reading stay where they were. A view left above the bottom is reading, as if they had scrolled.
        holdPlace: function (elementId) {
            var el = document.getElementById(elementId);
            if (el) el._sharpmushHeld = el.scrollHeight - el.scrollTop;
        },

        restorePlace: function (elementId) {
            var el = document.getElementById(elementId);
            if (!el || el._sharpmushHeld === undefined) return;
            el.scrollTop = el.scrollHeight - el._sharpmushHeld;
            delete el._sharpmushHeld;
            var reading = el.scrollHeight - el.scrollTop - el.clientHeight > 48;
            el._sharpmushReading = reading;
            var box = el.closest('.sharp-terminal-container') || el;
            box.classList.toggle('sharp-terminal--reading', reading);
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
