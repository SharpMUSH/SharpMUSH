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

	// A pose's mentions are rendered HTML links to /character/{Name}. In Play a plain click opens the
	// character sheet instead; a modified or middle click keeps the link's own behaviour (a new tab).
	delegateMentions: function (element, dotnetRef) {
		if (!element || element._sharpmushMentions) return;
		const handler = event => {
			if (event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
			const link = event.target && event.target.closest ? event.target.closest('a.mention[data-name]') : null;
			if (!link || !element.contains(link)) return;
			event.preventDefault();
			const pending = dotnetRef.invokeMethodAsync('OpenMention', link.getAttribute('data-name'));
			if (pending && typeof pending.catch === 'function') {
				pending.catch(() => { });
			}
		};
		element._sharpmushMentions = handler;
		element.addEventListener('click', handler);
	},

	undelegateMentions: function (element) {
		if (!element || !element._sharpmushMentions) return;
		element.removeEventListener('click', element._sharpmushMentions);
		element._sharpmushMentions = null;
	},

	// Play's exits (README §5.6): pressing a keycap's letter takes that exit. Never while the reader is
	// typing (the composer, the terminal input, any field), never with a modifier or a held key, and never
	// under a dialog (the character sheet, the palette): the page behind it is not what they are using.
	// Shift counts as a modifier: a capital letter is someone typing.
	registerExitKeys: function (dotnetRef, keys) {
		this._exitRef = dotnetRef;
		this._exitKeys = new Set((keys || []).map(k => String(k).toLowerCase()));
		if (this._exitListening) return;
		this._exitListening = true;
		document.addEventListener('keydown', event => {
			if (!this._exitRef || !this._exitKeys || event.repeat) return;
			if (event.ctrlKey || event.metaKey || event.altKey || event.shiftKey) return;
			const key = typeof event.key === 'string' ? event.key.toLowerCase() : '';
			if (!this._exitKeys.has(key)) return;
			const target = event.target;
			const tag = target && target.tagName;
			if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT' || (target && target.isContentEditable)) return;
			if (document.querySelector && document.querySelector('[aria-modal="true"]')) return;
			event.preventDefault();
			const pending = this._exitRef.invokeMethodAsync('GoByKey', key);
			if (pending && typeof pending.catch === 'function') {
				pending.catch(() => { });
			}
		});
	},

	unregisterExitKeys: function () {
		this._exitRef = null;
		this._exitKeys = null;
	},

	// Play's composer: Enter sends and Shift+Enter is a new line. Decided here, per key, because Blazor
	// decides preventDefault when it renders and would swallow the key after the one that sent. An Enter
	// that ends an IME composition belongs to the composition; Safari sends that Enter after compositionend,
	// with isComposing false, and keyCode 229 is then the only sign of it.
	composerEnter: function (element, dotnetRef) {
		if (!element || element._sharpmushEnter) return;
		const handler = event => {
			if (event.key !== 'Enter' || event.shiftKey || event.ctrlKey || event.metaKey || event.altKey
				|| event.isComposing || event.keyCode === 229) return;
			event.preventDefault();
			const pending = dotnetRef.invokeMethodAsync('SendFromEnter');
			if (pending && typeof pending.catch === 'function') {
				pending.catch(() => { });
			}
		};
		element._sharpmushEnter = handler;
		element.addEventListener('keydown', handler);
	},

	uncomposerEnter: function (element) {
		if (!element || !element._sharpmushEnter) return;
		element.removeEventListener('keydown', element._sharpmushEnter);
		element._sharpmushEnter = null;
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
