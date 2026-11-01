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

    // The space kept below a World map's drawing for the labels of the rooms along its bottom edge.
    function below(figure) {
        return parseFloat(getComputedStyle(figure).marginBottom) || 0;
    }

    // The room a World map frame has for its drawing, as [width, height]: its size, less the space kept below the
    // drawing for labels.
    window.SharpMUSH.mapFrameSize = function (frame) {
        var figure = frame && frame.firstElementChild;
        if (!figure) return null;
        return [frame.clientWidth, frame.clientHeight - below(figure)];
    };

    // Keeps one point of a World map's view in place after its drawing grew (or shrank) by ratio: the point at
    // (x, y) in the frame, its middle when none is given. A drawing smaller than its frame was centred in it, so the
    // point is measured from where the drawing began.
    window.SharpMUSH.mapRescale = function (frame, ratio, x, y) {
        var figure = frame && frame.firstElementChild;
        if (!figure || !(ratio > 0)) return;
        var oldWidth = figure.offsetWidth / ratio;
        var oldHeight = figure.offsetHeight / ratio;
        var tall = frame.clientHeight - below(figure);
        var atX = typeof x === "number" ? x : frame.clientWidth / 2;
        // The middle of the view: of the drawing while it was centred, else of the frame.
        var atY = typeof y === "number" ? y : oldHeight < tall ? tall / 2 : frame.clientHeight / 2;
        var onX = frame.scrollLeft + atX - Math.max(0, (frame.clientWidth - oldWidth) / 2);
        var onY = frame.scrollTop + atY - Math.max(0, (tall - oldHeight) / 2);
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
            // A room or corner a builder may drag moves that, not the view.
            if (e.target && e.target.closest && e.target.closest("[data-map-drag], [data-map-handle]")) return;
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

    // The World map's rooms by keyboard and, while editing, by drag. For every reader an arrow key on a room goes to the
    // nearest room that way (falling back to scrolling when there is none). While editing, on the chosen room a builder
    // may move ([data-map-drag], aria-pressed), arrows move it instead, through dotNet's Nudged(id, dx, dy, big,
    // resize): Shift for a bigger step, Ctrl/Cmd+Shift to move its east or south edge. A drag of such a room, or of a
    // corner of the chosen one ([data-map-handle]), follows the pointer, snapped to the figure's data-snap pixels and
    // kept inside its data-picture box, and ends in Dragged(id, dx, dy) or Stretched(id, corner, dx, dy) in the
    // figure's pixels. Escape puts it back. Alt is left alone: Alt+arrow is the browser's Back and Forward.
    window.SharpMUSH.mapEdit = function (frame, dotNet) {
        if (!frame || !dotNet) return { dispose: function () { } };

        var arrows = { ArrowLeft: [-1, 0], ArrowRight: [1, 0], ArrowUp: [0, -1], ArrowDown: [0, 1] };

        function centre(el) {
            var box = el.getBoundingClientRect();
            return [box.left + box.width / 2, box.top + box.height / 2];
        }

        // The room nearest from `from` in direction (dx, dy): ahead of it, scored by distance along the way plus twice
        // the distance off it, so a room straight ahead wins over a nearer one off to the side.
        function nearest(from, dx, dy) {
            var at = centre(from), best = null, score = Infinity;
            frame.querySelectorAll(".map-room").forEach(function (el) {
                if (el === from || el.offsetParent === null) return;
                var c = centre(el), ax = c[0] - at[0], ay = c[1] - at[1];
                var along = ax * dx + ay * dy;
                if (along <= 1) return;
                var off = Math.abs(ax * dy - ay * dx);
                var s = along + 2 * off;
                if (s < score) { score = s; best = el; }
            });
            return best;
        }

        // Focus follows the room after the page draws it again, which may give it a new element.
        function refocus(id) {
            window.requestAnimationFrame(function () {
                var el = frame.querySelector('[data-room-id="' + CSS.escape(id) + '"]');
                if (!el) return;
                if (document.activeElement !== el) el.focus({ preventScroll: true });
                el.scrollIntoView({ block: "nearest", inline: "nearest" });
            });
        }

        function onKeyDown(e) {
            var step = arrows[e.key];
            if (!step || e.altKey) return;
            var room = e.target.closest && e.target.closest(".map-room");
            if (!room || !frame.contains(room)) return;
            var id = room.getAttribute("data-room-id");
            if (id && room.hasAttribute("data-map-drag") && room.getAttribute("aria-pressed") === "true") {
                e.preventDefault();
                var resize = !!((e.ctrlKey || e.metaKey) && e.shiftKey);
                if (!resize && (e.ctrlKey || e.metaKey)) return;
                dotNet.invokeMethodAsync("Nudged", id, step[0], step[1], !!e.shiftKey && !resize, resize)
                    .then(function () { refocus(id); }, function () { });
                return;
            }
            if (e.ctrlKey || e.metaKey || e.shiftKey) return;
            var next = nearest(room, step[0], step[1]);
            if (!next) return;
            e.preventDefault();
            next.focus();
        }

        // A drag in progress: the element moving, what it started as, and how far the pointer has gone.
        var drag = null;

        function figureOf(el) { return el.closest(".map-figure"); }

        function numbers(text) {
            return (text || "").split(",").map(Number);
        }

        function onPointerDown(e) {
            // A second finger is a pinch: the map's own gestures take it, and the room goes back where it was.
            if (drag) { if (e.pointerId !== drag.pointer) abandon(); return; }
            if (e.button !== 0) return;
            var handle = e.target.closest && e.target.closest("[data-map-handle]");
            var room = !handle && e.target.closest && e.target.closest("[data-map-drag]");
            if ((!handle && !room) || !frame.contains(e.target)) return;
            var figure = figureOf(handle || room);
            var id = handle ? handle.getAttribute("data-room") : room.getAttribute("data-room-id");
            var el = frame.querySelector('[data-room-id="' + CSS.escape(id) + '"]');
            if (!el || !figure) return;
            var box = { left: el.offsetLeft, top: el.offsetTop, width: el.offsetWidth, height: el.offsetHeight };
            drag = {
                id: id, el: el, handle: handle, corner: handle ? handle.getAttribute("data-map-handle") : null,
                point: !handle && el.getAttribute("data-map-drag") === "point",
                figure: figure, x: e.clientX, y: e.clientY, dx: 0, dy: 0, box: box, moved: false,
                style: el.getAttribute("style"), pointer: e.pointerId,
                snap: Number(figure.getAttribute("data-snap")) || 0,
                picture: figure.getAttribute("data-picture") ? numbers(figure.getAttribute("data-picture")) : null,
                // A room's button is drawn in from its box by data-inset pixels: the box, not the button, meets the edge.
                inset: Number(figure.getAttribute("data-inset")) || 0,
                origin: numbers(figure.getAttribute("data-origin"))
            };
            if (handle) e.preventDefault();
            window.addEventListener("pointermove", onPointerMove);
            window.addEventListener("pointerup", onPointerUp);
            window.addEventListener("pointercancel", cancel);
            window.addEventListener("keydown", onEscape, true);
        }

        function snap(value) {
            return drag.snap > 0 ? Math.round(value / drag.snap) * drag.snap : value;
        }

        // The box the drag has made, in the figure's pixels: moved whole, or with the dragged corner moved.
        function shaped(dx, dy) {
            var b = drag.box, left = b.left, top = b.top, right = b.left + b.width, bottom = b.top + b.height;
            var p = drag.picture, i = drag.point ? 0 : drag.inset;
            if (p) p = [p[0] + i, p[1] + i, p[2] - 2 * i, p[3] - 2 * i];
            if (!drag.corner) {
                dx = snap(dx); dy = snap(dy);
                if (p) {
                    // A box stays inside the picture; a point (a room with no size) keeps its middle inside.
                    var halfW = drag.point ? b.width / 2 : 0, halfH = drag.point ? b.height / 2 : 0;
                    dx = Math.min(Math.max(dx, p[0] - halfW - left), p[0] + p[2] + halfW - right);
                    dy = Math.min(Math.max(dy, p[1] - halfH - top), p[1] + p[3] + halfH - bottom);
                }
                return { left: left + dx, top: top + dy, width: b.width, height: b.height, dx: dx, dy: dy };
            }
            var c = drag.corner, min = Math.max(8, drag.snap);
            if (c.indexOf("w") >= 0) left = Math.min(left + snap(dx), right - min);
            else right = Math.max(right + snap(dx), left + min);
            if (c.indexOf("n") >= 0) top = Math.min(top + snap(dy), bottom - min);
            else bottom = Math.max(bottom + snap(dy), top + min);
            if (p) {
                left = Math.max(left, p[0]); top = Math.max(top, p[1]);
                right = Math.min(right, p[0] + p[2]); bottom = Math.min(bottom, p[1] + p[3]);
            }
            return {
                left: left, top: top, width: right - left, height: bottom - top,
                dx: c.indexOf("w") >= 0 ? left - b.left : right - (b.left + b.width),
                dy: c.indexOf("n") >= 0 ? top - b.top : bottom - (b.top + b.height)
            };
        }

        // What the readout under the box says: on a picture, the point it would be centred on or the size it is being
        // given, in the picture's pixels; on a square map, a size in cells. A move on a grid or hex map shows where it
        // lands by its snapped place, so says nothing.
        function readout(box) {
            if (!drag.picture) {
                var cell = drag.box.width > 0 && drag.snap > 0 ? drag.snap : 0;
                return drag.corner && cell ? Math.round((box.width + 2 * drag.inset) / cell) + " × " + Math.round((box.height + 2 * drag.inset) / cell) : "";
            }
            var o = drag.origin, zoom = o[2] || 1;
            var x = Math.round(o[0] + (box.left + box.width / 2) / zoom);
            var y = Math.round(o[1] + (box.top + box.height / 2) / zoom);
            var i = drag.point ? 0 : 2 * drag.inset;
            return drag.corner ? Math.round((box.width + i) / zoom) + " × " + Math.round((box.height + i) / zoom) : "x " + x + " · y " + y;
        }

        function onPointerMove(e) {
            if (!drag || e.pointerId !== drag.pointer) return;
            var dx = e.clientX - drag.x, dy = e.clientY - drag.y;
            if (!drag.moved && Math.abs(dx) + Math.abs(dy) < 5) return;
            e.preventDefault();
            if (!drag.moved) {
                drag.moved = true;
                frame.classList.add("map-frame--editing-drag");
                drag.el.classList.add("map-room--dragging");
                var origin = frame.querySelector('[data-room="' + CSS.escape(drag.id) + '"]:not([data-map-handle])');
                if (origin) { origin.classList.add("map-box--origin"); drag.origin_el = origin; }
                drag.chip = document.createElement("span");
                drag.chip.className = "map-drag-readout";
                drag.chip.setAttribute("aria-hidden", "true");
                drag.figure.appendChild(drag.chip);
            }
            var box = shaped(dx, dy);
            drag.dx = box.dx; drag.dy = box.dy;
            var s = drag.el.style;
            s.left = box.left + "px"; s.top = box.top + "px"; s.width = box.width + "px"; s.height = box.height + "px";
            drag.chip.textContent = readout(box);
            drag.chip.hidden = drag.chip.textContent === "";
            drag.chip.style.left = box.left + box.width / 2 + "px";
            drag.chip.style.top = box.top + box.height + 8 + "px";
        }

        // Puts the room back as the page drew it, so the page's next drawing, moved or not, is what shows.
        function restore() {
            if (!drag) return;
            if (drag.style === null) drag.el.removeAttribute("style"); else drag.el.setAttribute("style", drag.style);
            drag.el.classList.remove("map-room--dragging");
            if (drag.origin_el) drag.origin_el.classList.remove("map-box--origin");
            if (drag.chip) drag.chip.remove();
            frame.classList.remove("map-frame--editing-drag");
        }

        function stop() {
            window.removeEventListener("pointermove", onPointerMove);
            window.removeEventListener("pointerup", onPointerUp);
            window.removeEventListener("pointercancel", cancel);
            window.removeEventListener("keydown", onEscape, true);
        }

        function swallow(e) {
            e.preventDefault();
            e.stopPropagation();
        }

        function onPointerUp(e) {
            if (!drag || e.pointerId !== drag.pointer) return;
            stop();
            var done = drag;
            restore();
            drag = null;
            if (!done.moved) return;
            frame.addEventListener("click", swallow, { capture: true, once: true });
            window.setTimeout(function () { frame.removeEventListener("click", swallow, { capture: true }); }, 0);
            if (Math.abs(done.dx) < 0.5 && Math.abs(done.dy) < 0.5) return;
            var call = done.corner
                ? dotNet.invokeMethodAsync("Stretched", done.id, done.corner, done.dx, done.dy)
                : dotNet.invokeMethodAsync("Dragged", done.id, done.dx, done.dy);
            call.then(function () { refocus(done.id); }, function () { });
        }

        function cancel() {
            stop();
            restore();
            drag = null;
        }

        function onEscape(e) {
            if (e.key !== "Escape" || !drag) return;
            e.preventDefault();
            e.stopPropagation();
            abandon();
        }

        // Ends the drag without a change, and keeps the click that ends it from choosing anything.
        function abandon() {
            var moved = drag.moved;
            cancel();
            if (moved) {
                frame.addEventListener("click", swallow, { capture: true, once: true });
                window.setTimeout(function () { frame.removeEventListener("click", swallow, { capture: true }); }, 0);
            }
        }

        frame.addEventListener("keydown", onKeyDown);
        frame.addEventListener("pointerdown", onPointerDown);
        return {
            dispose: function () {
                frame.removeEventListener("keydown", onKeyDown);
                frame.removeEventListener("pointerdown", onPointerDown);
                cancel();
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

    // Brings the top of a World map's builder panel into the page's view once a room or cell is chosen, when it sits
    // below the map out of sight: the page scrolls only far enough to show its first lines, so the map, where the room
    // was just chosen and may be dragged next, stays in view above it. Smooth unless the reader asked for less motion.
    window.SharpMUSH.mapPanelIntoView = function (panel) {
        if (!panel) return;
        var still = window.matchMedia && window.matchMedia("(prefers-reduced-motion: reduce)").matches;
        var scroller = panel.parentElement;
        while (scroller && scroller !== document.body) {
            var overflow = getComputedStyle(scroller).overflowY;
            if ((overflow === "auto" || overflow === "scroll") && scroller.scrollHeight > scroller.clientHeight) break;
            scroller = scroller.parentElement;
        }
        if (!scroller || scroller === document.body) scroller = document.scrollingElement || document.documentElement;
        var view = scroller === document.scrollingElement || scroller === document.documentElement
            ? { top: 0, bottom: window.innerHeight }
            : scroller.getBoundingClientRect();
        var peek = Math.min(240, (view.bottom - view.top) / 3);
        var top = panel.getBoundingClientRect().top;
        if (top <= view.bottom - peek) return;
        scroller.scrollBy({ top: top - (view.bottom - peek), behavior: still ? "auto" : "smooth" });
    };

    // A picture's size in pixels, [width, height], or null when it does not load: the map settings take a picture
    // map's size from its backdrop.
    window.SharpMUSH.imageSize = function (url) {
        return new Promise(function (resolve) {
            var image = new Image();
            image.onload = function () { resolve([image.naturalWidth, image.naturalHeight]); };
            image.onerror = function () { resolve(null); };
            image.src = url;
        });
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
