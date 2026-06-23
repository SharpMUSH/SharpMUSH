window.SharpMUSH = window.SharpMUSH || {};

// Measures a character grid for a terminal output element rendered in a monospace font, and
// reports NAWS-style {cols, rows} on resize. Advance/line-height are measured from a hidden
// probe (long run averages sub-pixel rounding); never derived from font-size.
window.SharpMUSH.Metrics = {
    measure: function (elementId) {
        var el = document.getElementById(elementId);
        if (!el) return { cols: 80, rows: 24 };

        var cs = getComputedStyle(el);
        var probe = document.createElement('span');
        probe.style.position = 'absolute';
        probe.style.visibility = 'hidden';
        probe.style.whiteSpace = 'pre';
        probe.style.fontFamily = cs.fontFamily;
        probe.style.fontSize = cs.fontSize;
        probe.style.lineHeight = cs.lineHeight;
        probe.style.letterSpacing = cs.letterSpacing;
        probe.style.fontFeatureSettings = cs.fontFeatureSettings;
        el.appendChild(probe);

        probe.textContent = '0'.repeat(200);
        var advance = probe.getBoundingClientRect().width / 200;

        probe.textContent = '0\n0\n0\n0\n0';
        var lineHeight = probe.getBoundingClientRect().height / 5;

        el.removeChild(probe);

        var padX = parseFloat(cs.paddingLeft) + parseFloat(cs.paddingRight);
        var padY = parseFloat(cs.paddingTop) + parseFloat(cs.paddingBottom);
        var contentW = el.clientWidth - padX;
        var contentH = el.clientHeight - padY;

        var clamp = function (v) { return v < 1 ? 1 : (v > 1000 ? 1000 : v); };
        if (!advance || advance <= 0) return { cols: 80, rows: 24 };
        if (!lineHeight || lineHeight <= 0) return { cols: 80, rows: 24 };

        return {
            cols: clamp(Math.floor(contentW / advance)),
            rows: clamp(Math.floor(contentH / lineHeight))
        };
    },

    observe: function (elementId, dotNetRef) {
        var self = this;
        var el = document.getElementById(elementId);
        if (!el) return { dispose: function () { } };

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
