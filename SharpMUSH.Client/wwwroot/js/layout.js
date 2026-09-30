// Small layout helpers for the responsive shell.
window.sharpmushLayout = {
	// True when the shell is in "touch chrome" mode (off-canvas drawer + bottom nav rather
	// than the desktop sidebar), so the hamburger opens the drawer instead of toggling the
	// desktop rail. MUST stay in sync with the touch-chrome @media condition in custom.css:
	// any touch device (pointer: coarse) OR a narrow window (<=760px).
	isTouchChrome: function () {
		return window.matchMedia('(max-width: 760px), (pointer: coarse)').matches;
	},

	// A modal (the image viewer) remembers what opened it and hands focus back when it closes, so a
	// keyboard user is not dropped on <body>.
	rememberFocus: function () {
		this._focusReturn = document.activeElement instanceof HTMLElement ? document.activeElement : null;
	},

	restoreFocus: function () {
		const target = this._focusReturn;
		this._focusReturn = null;
		if (target && target.isConnected) {
			target.focus();
		}
	},

	// ⌘K / Ctrl+K opens the command palette (README §10 Q1) from anywhere except a place the reader is
	// typing: the terminal input and every text field keep their keys. One listener for the page; a
	// later registration (a re-rendered shell) replaces the one it answers to.
	registerPaletteHotkey: function (dotnetRef) {
		this._paletteRef = dotnetRef;
		if (this._paletteListening) {
			return;
		}

		this._paletteListening = true;
		document.addEventListener('keydown', event => {
			if (event.key !== 'k' && event.key !== 'K') return;
			if (!event.ctrlKey && !event.metaKey) return;
			// AltGr+K (ctrl+alt on Windows layouts) types a character; Ctrl+Shift+K is a browser's console.
			if (event.altKey || event.shiftKey) return;
			if (!this._paletteRef) return;
			const target = event.target;
			const tag = target && target.tagName;
			if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT' || (target && target.isContentEditable)) return;
			event.preventDefault();
			const pending = this._paletteRef.invokeMethodAsync('OpenPaletteFromHotkey');
			if (pending && typeof pending.catch === 'function') {
				// The shell was disposed between the key and the call (a page on another layout).
				pending.catch(() => { });
			}
		});
	},

	// The shell that registered is going away (the login and setup pages use another layout).
	unregisterPaletteHotkey: function () {
		this._paletteRef = null;
	},

	// Back-compat alias.
	isNarrow: function () {
		return this.isTouchChrome();
	},

	// How far a pointer must travel to grow or shrink a layout-editor widget by one grid column.
	// A run of N columns measures N tracks plus the N-1 gaps between them, so one column's worth of
	// travel is one track plus one gap — which is (content width + gap) / columns. The zone's own
	// padding is not track space, hence the subtraction.
	//
	// Returns 0 when the zone has collapsed to fewer tracks than asked for (the narrow-viewport rule
	// in LayoutEditor.razor.css drops the grid to a single column). Callers treat that as "pointer
	// resize is unavailable here" rather than dividing by a meaningless number.
	gridColumnWidth: function (zoneWrapperId, columns) {
		const zone = document.getElementById(zoneWrapperId)?.querySelector('.le-zone-drop');
		if (!zone || !columns) {
			return 0;
		}

		const style = getComputedStyle(zone);
		if (style.gridTemplateColumns.split(' ').filter(Boolean).length < columns) {
			return 0;
		}

		const gap = parseFloat(style.columnGap) || 0;
		const content = zone.clientWidth - parseFloat(style.paddingLeft) - parseFloat(style.paddingRight);
		return content > 0 ? (content + gap) / columns : 0;
	},

	// Routes the rest of a resize gesture to the grip even once the pointer has left it — which it
	// will, because the widget the grip sits on is being resized out from under it. The alternative,
	// a full-viewport overlay, cannot work here: an ancestor declares a CSS container, and a container
	// is a containing block for position:fixed, so the overlay would be clipped to the page area.
	capturePointer: function (elementId, pointerId) {
		const el = document.getElementById(elementId);
		if (!el) {
			return;
		}

		try {
			el.setPointerCapture(pointerId);
		} catch {
			// The pointer was released between the Blazor round trip and this call. Nothing to capture.
		}
	}
};
