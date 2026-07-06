window.SharpMUSH = window.SharpMUSH || {};

// Measures a character grid for a terminal output element rendered in a monospace font, and
// reports NAWS-style {cols, rows} on resize. Advance/line-height are measured from a hidden
// probe (long run averages sub-pixel rounding); never derived from font-size.
//
// When a target column count is set for an element (the /play terminal — the player's
// preferred line width, min 78), the font is SCALED so exactly that many columns fill the
// available width: it grows on a roomy screen (bigger, more readable) and shrinks on a tight
// one, clamped to a legible range. With no target, the natural grid at the base font is used.
window.SharpMUSH.Metrics = {
    MIN_FONT_PX: 6,    // below this, fall back to horizontal scroll rather than illegible text
    MAX_FONT_PX: 24,   // above this, stop growing (line-length cap) — content left-aligns
    _targets: {},      // elementId -> target column count
    _fire: {},         // elementId -> re-measure fn (so setTarget can refit immediately)

    // Width (px) of `text` in `el`'s font at a given size, measured with the element's exact
    // font context so it matches what the browser paints.
    _runWidth: function (el, cs, fontPx, text) {
        var probe = document.createElement('span');
        probe.style.position = 'absolute';
        probe.style.visibility = 'hidden';
        probe.style.whiteSpace = 'pre';
        probe.style.fontFamily = cs.fontFamily;
        probe.style.fontSize = fontPx + 'px';
        probe.style.lineHeight = cs.lineHeight;
        probe.style.letterSpacing = cs.letterSpacing;
        probe.style.fontFeatureSettings = cs.fontFeatureSettings;
        probe.textContent = text;
        el.appendChild(probe);
        var w = probe.getBoundingClientRect().width;
        el.removeChild(probe);
        return w;
    },

    measure: function (elementId) {
        var el = document.getElementById(elementId);
        if (!el) return { cols: 80, rows: 24 };

        var clamp = function (v) { return v < 1 ? 1 : (v > 1000 ? 1000 : v); };
        var target = this._targets[elementId] || 0;

        // Measure at the base (stylesheet) font, independent of any fit we applied before.
        el.style.fontSize = '';
        var cs = getComputedStyle(el);
        var baseFontPx = parseFloat(cs.fontSize) || 13.5;

        var advance = this._runWidth(el, cs, baseFontPx, '0'.repeat(200)) / 200;

        var lineHeight;
        (function () {
            var p = document.createElement('span');
            p.style.cssText = 'position:absolute;visibility:hidden;white-space:pre';
            p.style.fontFamily = cs.fontFamily; p.style.fontSize = baseFontPx + 'px';
            p.style.lineHeight = cs.lineHeight;
            p.textContent = '0\n0\n0\n0\n0';
            el.appendChild(p); lineHeight = p.getBoundingClientRect().height / 5; el.removeChild(p);
        })();

        if (!advance || advance <= 0) return { cols: 80, rows: 24 };
        if (!lineHeight || lineHeight <= 0) return { cols: 80, rows: 24 };

        var padX = parseFloat(cs.paddingLeft) + parseFloat(cs.paddingRight);
        var padY = parseFloat(cs.paddingTop) + parseFloat(cs.paddingBottom);
        var contentW = el.clientWidth - padX;
        var contentH = el.clientHeight - padY;

        // Fit-to-target: size the font so `target` columns span the width.
        if (target && target > 0) {
            var advanceRatio = advance / baseFontPx;
            var lineRatio = lineHeight / baseFontPx;
            var targetW = contentW - 1;            // a hair inside the box (no sub-pixel scrollbar)
            var fitFont = targetW / target / advanceRatio;
            if (fitFont > this.MAX_FONT_PX) fitFont = this.MAX_FONT_PX;
            // Closed-loop: glyph advance isn't perfectly linear with size (hinting), so correct
            // downward if the real width overflows. Growth is already bounded by MAX_FONT_PX.
            for (var i = 0; i < 4; i++) {
                if (fitFont <= this.MIN_FONT_PX) { fitFont = this.MIN_FONT_PX; break; }
                var actual = this._runWidth(el, cs, fitFont, '0'.repeat(target));
                if (actual <= targetW) break;
                fitFont = Math.max(this.MIN_FONT_PX, fitFont * (targetW / actual));
            }
            el.style.fontSize = fitFont + 'px';
            var fitLine = lineRatio * fitFont;
            return { cols: target, rows: clamp(Math.floor(contentH / fitLine)) };
        }

        // No target: natural grid at the base font.
        return {
            cols: clamp(Math.floor(contentW / advance)),
            rows: clamp(Math.floor(contentH / lineHeight))
        };
    },

    observe: function (elementId, dotNetRef, target) {
        var self = this;
        var el = document.getElementById(elementId);
        if (!el) return { dispose: function () { } };

        self._targets[elementId] = target || 0;

        var last = { cols: 0, rows: 0 };
        var timer = null;

        function fire() {
            var g = self.measure(elementId);
            if (g.cols === last.cols && g.rows === last.rows) return;
            last = g;
            dotNetRef.invokeMethodAsync('OnTerminalResize', g.cols, g.rows);
        }
        function schedule() {
            if (timer) clearTimeout(timer);
            timer = setTimeout(fire, 150);
        }
        self._fire[elementId] = fire;

        var ro = new ResizeObserver(schedule);
        ro.observe(el);

        if (document.fonts && document.fonts.ready) {
            document.fonts.ready.then(fire);
        } else {
            fire();
        }

        return {
            dispose: function () {
                if (timer) clearTimeout(timer);
                ro.disconnect();
                delete self._targets[elementId];
                delete self._fire[elementId];
            }
        };
    },

    // Live-update the preferred column count and refit immediately (forces a fresh report
    // even if the grid happens to match, so the new font/NAWS take effect).
    setTarget: function (elementId, target) {
        this._targets[elementId] = target || 0;
        var fire = this._fire[elementId];
        if (fire) { fire(); } else { this.measure(elementId); }
    }
};
