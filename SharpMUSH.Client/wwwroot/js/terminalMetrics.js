window.SharpMUSH = window.SharpMUSH || {};

// Measures a character grid for a terminal output element rendered in a monospace font, and
// reports NAWS-style {cols, rows} on resize. Advance/line-height are measured from a hidden
// probe (long run averages sub-pixel rounding); never derived from font-size.
//
// When minCols is set (the /play terminal asks for 78) and the column count at the element's
// natural font-size would fall short, the font is scaled DOWN so exactly minCols columns fit
// the visible width — so 78-col content (tables, ASCII art) is fully visible without
// horizontal scrolling. The fit is closed-loop: glyph advance is not perfectly linear with
// font-size (hinting widens small text), so we measure the real width at the candidate size
// and correct until minCols actually fit. On a wide screen where minCols already fit, the
// natural font-size is kept.
window.SharpMUSH.Metrics = {
    // Never shrink below this — past it, fall back to horizontal scroll rather than illegible text.
    MIN_FONT_PX: 6,

    // Width (px) of `text` rendered in `el`'s font at a given size. Probe inherits the element's
    // exact font context so the measurement matches what the browser will actually paint.
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

    measure: function (elementId, minCols) {
        var el = document.getElementById(elementId);
        if (!el) return { cols: 80, rows: 24 };

        var clamp = function (v) { return v < 1 ? 1 : (v > 1000 ? 1000 : v); };

        // Measure at the element's BASE (stylesheet) font-size, independent of any prior fit.
        el.style.fontSize = '';
        var cs = getComputedStyle(el);
        var baseFontPx = parseFloat(cs.fontSize) || 13.5;

        var advance = this._runWidth(el, cs, baseFontPx, '0'.repeat(200)) / 200;

        // Line-height probe (height of 5 lines / 5) at the base font.
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

        var naturalCols = Math.floor(contentW / advance);

        // Fit-to-minimum: shrink the font so minCols columns span the width.
        if (minCols && minCols > 0 && naturalCols < minCols) {
            var target = contentW - 1; // leave the 78ch track a hair inside the box (no scrollbar)
            var lineRatio = lineHeight / baseFontPx;
            // First estimate from the linear ratio, then correct against the real rendered width.
            var fitFont = target / minCols / (advance / baseFontPx);
            for (var iter = 0; iter < 4; iter++) {
                if (fitFont < this.MIN_FONT_PX) { fitFont = this.MIN_FONT_PX; break; }
                var actual = this._runWidth(el, cs, fitFont, '0'.repeat(minCols));
                if (actual <= target) break;
                fitFont = Math.max(this.MIN_FONT_PX, fitFont * (target / actual));
            }
            el.style.fontSize = fitFont + 'px';
            var fitLine = lineRatio * fitFont;
            return {
                cols: minCols,
                rows: clamp(Math.floor(contentH / fitLine))
            };
        }

        // No fit needed — base font-size already restored above.
        return {
            cols: clamp(naturalCols),
            rows: clamp(Math.floor(contentH / lineHeight))
        };
    },

    observe: function (elementId, dotNetRef, minCols) {
        var self = this;
        var el = document.getElementById(elementId);
        if (!el) return { dispose: function () { } };

        var last = { cols: 0, rows: 0 };
        var timer = null;

        function fire() {
            var g = self.measure(elementId, minCols);
            if (g.cols === last.cols && g.rows === last.rows) return;
            last = g;
            dotNetRef.invokeMethodAsync('OnTerminalResize', g.cols, g.rows);
        }
        function schedule() {
            if (timer) clearTimeout(timer);
            timer = setTimeout(fire, 150);
        }

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
            }
        };
    }
};
