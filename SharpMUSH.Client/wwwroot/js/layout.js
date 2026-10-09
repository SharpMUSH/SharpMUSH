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
	// keyboard user is not dropped on <body>. A stack: a modal opened from another (a picture from the
	// character sheet) hands focus back to the sheet, and the sheet then to what opened it.
	_focusReturns: [],

	rememberFocus: function () {
		const active = document.activeElement;
		this._focusReturns.push(active && typeof active.focus === 'function' && active !== document.body ? active : null);
		// A modal closed some other way than restoreFocus leaves its entry behind; nesting is never this deep.
		if (this._focusReturns.length > 8) this._focusReturns.shift();
	},

	restoreFocus: function () {
		const target = this._focusReturns.pop();
		if (target && target.isConnected) {
			target.focus();
		}
	},

	// Touch chrome's off-canvas panels (the menu drawer, a section's panel) behave as a dialog while open:
	// focus moves to the panel's first control, the rest of the shell is inert, and Escape closes it through
	// dotnetRef's CloseMobilePanelsFromKey. Closing hands focus back to what opened it, unless the page changed
	// (the new page takes focus for its heading). Nothing happens on a desktop, where the panels are columns.
	openPanel: function (selector, dotnetRef) {
		const panel = document.querySelector(selector);
		const shell = panel && panel.closest('.phosphor-shell');
		if (!shell || !this.isTouchChrome()) return;
		// One panel replacing another (the drawer opening a section's panel) keeps the first one's opener.
		if (this._panelOpen) this._releasePanel();
		else this.rememberFocus();
		this._panelInert = Array.from(shell.children)
			.filter(child => !child.contains(panel) && !child.classList.contains('phosphor-nav-backdrop') && !child.inert);
		this._panelInert.forEach(child => { child.inert = true; });
		this._panelKey = event => {
			if (event.key !== 'Escape') return;
			event.preventDefault();
			dotnetRef.invokeMethodAsync('CloseMobilePanelsFromKey').catch(() => { });
		};
		document.addEventListener('keydown', this._panelKey);
		this._panelOpen = true;
		this._panelEl = panel;
		this.focusFirst(panel);
	},

	closePanel: function (restore) {
		if (!this._panelOpen) return;
		this._releasePanel();
		this._panelOpen = false;
		const panel = this._panelEl;
		this._panelEl = null;
		if (restore) {
			this.restoreFocus();
			return;
		}
		this._focusReturns.pop();
		// A page change: Blazor's FocusOnNavigate tried the new page's h1 while the page was still inert, and
		// could not focus it, so focus is still on the link in the panel, which is about to be hidden.
		const active = document.activeElement;
		if (!active || active === document.body || !active.isConnected || (panel && panel.contains(active))) {
			const heading = document.querySelector('#main-content h1') || document.querySelector('h1');
			if (heading) {
				if (!heading.hasAttribute('tabindex')) heading.setAttribute('tabindex', '-1');
				heading.focus();
			}
		}
	},

	_releasePanel: function () {
		(this._panelInert || []).forEach(child => { child.inert = false; });
		this._panelInert = null;
		document.removeEventListener('keydown', this._panelKey);
		this._panelKey = null;
	},

	// PopoverMenu: the panel is drawn at the page's root, so it is not next in the tab order. Opened with
	// focus on its button (the keyboard, or a click that focused the button), focus moves to the panel's
	// first control; a click that kept focus in a text field (the format menu's mousedown is prevented)
	// leaves it there. Closed with focus in the panel, or lost to the page, focus goes back to the button.
	popoverOpened: function (wrap, panel) {
		if (!wrap || !panel || !wrap.contains(document.activeElement)) return;
		const first = panel.querySelector('button:not([disabled]), a[href], input:not([disabled]), select:not([disabled]), textarea:not([disabled])');
		if (first) first.focus();
	},

	popoverClosed: function (wrap, panel) {
		if (!wrap) return;
		const active = document.activeElement;
		const lost = !active || active === document.body || (panel && panel.contains(active));
		if (!lost) return;
		const button = wrap.querySelector('button');
		if (button) button.focus();
	},

	// A popover of actions (the account panel): Up and Down move between its controls, Home and End go to
	// the first and last, wrapping round. Only controls outside an inert part count, so a hidden level is
	// skipped. Installed once per element.
	arrowFocus: function (container) {
		if (!container || container._arrowFocus) return;
		container._arrowFocus = true;
		container.addEventListener('keydown', event => {
			if (!['ArrowDown', 'ArrowUp', 'Home', 'End'].includes(event.key)) return;
			const items = this._arrowItems(container);
			if (items.length === 0) return;
			event.preventDefault();
			const at = items.indexOf(document.activeElement);
			const next = event.key === 'Home' ? 0
				: event.key === 'End' ? items.length - 1
					: event.key === 'ArrowDown' ? (at + 1) % items.length
						: (at <= 0 ? items.length : at) - 1;
			items[next].focus();
		});
	},

	// Moves focus to the first control of a popover, as when it switches to another level.
	focusFirst: function (container) {
		const items = this._arrowItems(container);
		if (items.length > 0) items[0].focus();
	},

	_arrowItems: function (container) {
		if (!container) return [];
		return Array.from(container.querySelectorAll('button:not([disabled]), a[href]'))
			.filter(item => !item.closest('[inert]'));
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

	// Play's compact screen: a phone, either way up (sideways, or the phone width shell.css uses). The room
	// banner folds into the card header there, so the terminal keeps the height. Returns whether it matches
	// now and calls OnCompactScreenChanged on the page whenever that changes (a rotation, a resize).
	compactScreenQuery: '(orientation: landscape) and (max-height: 32rem), (max-width: 760px)',

	// Play's short screen: the landscape half of compactScreenQuery, a phone held sideways. Wide enough for
	// the tablet layout and short of height for it, so Play tightens it (play--short in Play.razor.css):
	// the header on one line, the card edge to edge in focus mode. The same shape as shell.css's SHORT
	// LANDSCAPE block. Calls OnShortScreenChanged on the page whenever it changes.
	shortScreenQuery: '(orientation: landscape) and (max-height: 32rem)',

	watchCompactScreen: function (dotnetRef) {
		return this._watchScreen('_compactScreen', this.compactScreenQuery, dotnetRef, 'OnCompactScreenChanged');
	},

	unwatchCompactScreen: function () {
		this._unwatchScreen('_compactScreen');
	},

	watchShortScreen: function (dotnetRef) {
		return this._watchScreen('_shortScreen', this.shortScreenQuery, dotnetRef, 'OnShortScreenChanged');
	},

	unwatchShortScreen: function () {
		this._unwatchScreen('_shortScreen');
	},

	// ThemeProvider: the theme's parts as data attributes on <html> (data-texture="galaxy", ...), which the rules in
	// css/themes/ match. A theme sets every part, so each call replaces the last one's values.
	applyThemeParts: function (parts) {
		for (const [name, value] of Object.entries(parts)) {
			document.documentElement.setAttribute('data-' + name, value);
		}
	},

	// ThemeService: the game's default theme follows the browser's light or dark preference.
	watchLightScheme: function (dotnetRef) {
		return this._watchScreen('_lightScheme', '(prefers-color-scheme: light)', dotnetRef, 'OnLightSchemeChanged');
	},

	_watchScreen: function (slot, condition, dotnetRef, method) {
		this._unwatchScreen(slot);
		const query = window.matchMedia(condition);
		const handler = event => {
			const pending = dotnetRef.invokeMethodAsync(method, event.matches);
			if (pending && typeof pending.catch === 'function') {
				pending.catch(() => { });
			}
		};
		if (typeof query.addEventListener === 'function') {
			query.addEventListener('change', handler);
		}
		this[slot] = { query, handler };
		return query.matches;
	},

	_unwatchScreen: function (slot) {
		const watch = this[slot];
		this[slot] = null;
		if (watch && typeof watch.query.removeEventListener === 'function') {
			watch.query.removeEventListener('change', watch.handler);
		}
	},

	// FormattedInput: a textarea with a styled layer over it. The layer copies the textarea's box (font,
	// padding, border widths, the scrollbar's width) and follows its scroll, so each rendered character sits
	// over the transparent one it shows. Undo and redo go to the draft, which keeps the formatting the
	// textarea's own history knows nothing of; Ctrl/Cmd+B, +U and +\ are the format shortcuts.
	_formattedBoxProps: ['fontFamily', 'fontSize', 'fontWeight', 'fontStyle', 'fontStretch', 'lineHeight',
		'letterSpacing', 'wordSpacing', 'tabSize', 'textIndent', 'textTransform', 'paddingTop', 'paddingLeft',
		'paddingBottom', 'borderTopWidth', 'borderRightWidth', 'borderBottomWidth', 'borderLeftWidth',
		'borderTopStyle', 'borderRightStyle', 'borderBottomStyle', 'borderLeftStyle', 'direction'],

	formattedInput: function (textarea, dotnetRef) {
		if (!textarea || textarea._sharpmushFormatted) return;
		const state = { overlay: null, observer: null };
		const call = (action) => {
			const pending = dotnetRef.invokeMethodAsync('Shortcut', action, textarea.selectionStart, textarea.selectionEnd);
			if (pending && typeof pending.catch === 'function') pending.catch(() => { });
		};
		state.keydown = event => {
			if (!(event.ctrlKey || event.metaKey) || event.altKey || event.isComposing) return;
			const key = (event.key || '').toLowerCase();
			let action = null;
			if (key === 'z') action = event.shiftKey ? 'redo' : 'undo';
			else if (key === 'y' && !event.shiftKey) action = 'redo';
			else if (key === 'b' && !event.shiftKey) action = 'bold';
			else if (key === 'u' && !event.shiftKey) action = 'underline';
			else if (key === '\\') action = 'clear';
			if (!action) return;
			event.preventDefault();
			call(action);
		};
		state.beforeinput = event => {
			if (event.inputType !== 'historyUndo' && event.inputType !== 'historyRedo') return;
			event.preventDefault();
			call(event.inputType === 'historyUndo' ? 'undo' : 'redo');
		};
		state.scroll = () => this._formattedScroll(textarea);
		textarea.addEventListener('keydown', state.keydown);
		textarea.addEventListener('beforeinput', state.beforeinput);
		textarea.addEventListener('scroll', state.scroll);
		textarea._sharpmushFormatted = state;
	},

	formattedOverlay: function (textarea, overlay) {
		const state = textarea && textarea._sharpmushFormatted;
		if (!state || !overlay) return;
		state.overlay = overlay;
		if (state.observer) state.observer.disconnect();
		if (typeof ResizeObserver === 'function') {
			state.observer = new ResizeObserver(() => this.formattedSync(textarea));
			state.observer.observe(textarea);
		}
		this.formattedSync(textarea);
	},

	formattedSync: function (textarea) {
		const state = textarea && textarea._sharpmushFormatted;
		const overlay = state && state.overlay;
		if (!overlay || !overlay.isConnected) return;
		const style = getComputedStyle(textarea);
		for (const prop of this._formattedBoxProps) overlay.style[prop] = style[prop];
		overlay.style.borderColor = 'transparent';
		// The layer is sized to the textarea's border box, whatever box-sizing the textarea itself uses.
		overlay.style.boxSizing = 'border-box';
		// The textarea's scrollbar narrows the text; the layer has none, so it pads that width instead.
		const borders = parseFloat(style.borderLeftWidth) + parseFloat(style.borderRightWidth);
		const scrollbar = Math.max(0, textarea.offsetWidth - textarea.clientWidth - borders);
		overlay.style.paddingRight = (parseFloat(style.paddingRight) + scrollbar) + 'px';
		overlay.style.left = textarea.offsetLeft + 'px';
		overlay.style.top = textarea.offsetTop + 'px';
		overlay.style.width = textarea.offsetWidth + 'px';
		overlay.style.height = textarea.offsetHeight + 'px';
		this._formattedScroll(textarea);
	},

	_formattedScroll: function (textarea) {
		const state = textarea._sharpmushFormatted;
		const overlay = state && state.overlay;
		if (!overlay || !overlay.isConnected) return;
		overlay.scrollTop = textarea.scrollTop;
		overlay.scrollLeft = textarea.scrollLeft;
	},

	unformattedInput: function (textarea) {
		const state = textarea && textarea._sharpmushFormatted;
		if (!state) return;
		textarea.removeEventListener('keydown', state.keydown);
		textarea.removeEventListener('beforeinput', state.beforeinput);
		textarea.removeEventListener('scroll', state.scroll);
		if (state.observer) state.observer.disconnect();
		textarea._sharpmushFormatted = null;
	},

	// The textarea's selection, read synchronously by FormattedInput so an input is applied before the next.
	fieldSelection: function (textarea) {
		if (!textarea || typeof textarea.selectionStart !== 'number') return null;
		return [textarea.selectionStart, textarea.selectionEnd];
	},

	fieldSelect: function (textarea, start, end) {
		if (!textarea) return;
		textarea.focus();
		textarea.setSelectionRange(start, end);
		this.formattedSync(textarea);
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
