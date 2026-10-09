// The page's main text field (Play's composer or terminal line, a channel's line, a scene's pose box, the
// wiki's source) is marked data-primary-input, and this makes it easy to reach, the way chat clients and
// terminals do, without taking a key or a click from anything else:
//
// - Arriving: a page that is for writing focuses its field once it is on screen (the HTML autofocus idea,
//   which Blazor's re-rendered DOM cannot use), unless the reader is already in a field, a dialog or one of
//   the page's own controls. Not on a touch screen, where focusing a field opens the keyboard over the page.
// - Typing: a printable key pressed while focus is on nothing in particular (the page, a heading, the
//   output) moves focus to the field before the key is delivered, so the character lands there. A key that
//   a control, a dialog or a page shortcut (Play's exit keys) handles is left alone: this listens on window,
//   after the document's listeners, and skips anything already prevented. Space still scrolls, and every
//   chord (Ctrl+C included) keeps its meaning.
// - Clicking: a plain mouse click in the output (data-input-region) that selects nothing focuses the field,
//   as a terminal does. Dragging, double-clicking and links keep selecting and following; touch never does it.
// - The shell's skip links (WCAG 2.4.1, bypass blocks): one to the page, one to its main text box.
//
// data-primary-input="start" keeps the caret at the top the first time the field is focused (the wiki's
// source); any other value puts it at the end, after the kept draft. After that the field keeps its own.
(function () {
	const interactive = [
		'a[href]', 'a[xch_cmd]', 'area[href]', 'button', 'input', 'select', 'textarea', 'summary', 'iframe', 'object', 'embed',
		'audio[controls]', 'video[controls]', 'label', '[contenteditable]:not([contenteditable="false"])',
		'dialog', '.monaco-editor',
		...['button', 'link', 'checkbox', 'radio', 'switch', 'tab', 'option', 'menuitem', 'menuitemcheckbox',
			'menuitemradio', 'slider', 'spinbutton', 'textbox', 'searchbox', 'combobox', 'listbox', 'menu',
			'menubar', 'tree', 'treegrid', 'treeitem', 'grid', 'gridcell', 'tablist', 'radiogroup', 'toolbar',
			'dialog', 'alertdialog'].map(role => `[role="${role}"]`),
	].join(',');

	// Something the reader has open over the page: a modal, an open menu or picker. Tooltips are not.
	const overlay = '[aria-modal="true"], dialog[open], .mud-dialog-container, .mud-popover-open:not(.mud-tooltip)';

	const api = {
		_focused: new WeakSet(),
		_last: null,
		_pointer: 'mouse',

		// The field that takes focus: the one last used, while it is still usable, else the first on screen.
		current: function () {
			const fields = Array.from(document.querySelectorAll('[data-primary-input]')).filter(api.usable);
			if (api._last && fields.includes(api._last)) return api._last;
			return fields[0] || null;
		},

		usable: function (field) {
			if (!field || !field.isConnected || field.disabled || field.readOnly) return false;
			if (field.closest('[inert]')) return false;
			return field.getClientRects().length > 0;
		},

		overlayOpen: function () {
			return !!document.querySelector(overlay);
		},

		// Whether focus is on something of its own: a field, an editor, a control, or inside a dialog.
		engaged: function (element) {
			if (!element || element === document.body || element === document.documentElement) return false;
			return typeof element.closest === 'function' && !!element.closest(interactive);
		},

		focusField: function (field) {
			const first = !api._focused.has(field);
			field.focus();
			if (first && field.getAttribute('data-primary-input') !== 'start' && typeof field.setSelectionRange === 'function') {
				const end = field.value.length;
				field.setSelectionRange(end, end);
			}
			return true;
		},

		// Marks a field a component library renders (no attribute of ours reaches it) as the page's main one.
		mark: function (id, caret) {
			const field = document.getElementById(id);
			if (field) field.setAttribute('data-primary-input', caret || 'end');
		},

		// The skip link, or any control that hands the reader to the field.
		focus: function () {
			const field = api.current();
			return field ? api.focusField(field) : false;
		},

		// A field that has just come on screen, on a page that is for writing. Deferred a turn so it runs after
		// the router's own focus move (FocusOnNavigate) and after a button that opened it has been removed.
		// Takes the element, or its id (a component library's field, reached by its InputId).
		arrive: function (field) {
			if (typeof field === 'string') field = document.getElementById(field);
			if (!field || window.matchMedia('(pointer: coarse)').matches) return;
			setTimeout(() => {
				if (!api.usable(field) || api.overlayOpen()) return;
				const active = document.activeElement;
				if (active === field) return;
				if (api.engaged(active)) {
					// A control the reader is on (a view's tab, a field) keeps focus; the site's navigation outside
					// the page hands it over, as FocusOnNavigate does.
					const page = document.querySelector('main');
					const control = active.closest('input, textarea, select, [contenteditable]:not([contenteditable="false"]), .monaco-editor');
					if (control || !page || page.contains(active)) return;
				}
				api.focusField(field);
			}, 0);
		},

		onKeyDown: function (event) {
			if (event.defaultPrevented || event.isComposing || event.repeat) return;
			if (event.ctrlKey || event.metaKey || event.altKey) return;
			const key = event.key;
			// One printable character: not Enter, Tab, an arrow, a dead key or an IME's Process. Space scrolls.
			if (typeof key !== 'string' || Array.from(key).length !== 1 || key === ' ') return;
			if (api.engaged(event.target) || api.overlayOpen()) return;
			const field = api.current();
			if (!field) return;
			// Focus moves now and the browser delivers this key's character to the field: no preventDefault.
			api.focusField(field);
		},

		onPointerDown: function (event) {
			api._pointer = event.pointerType || 'mouse';
		},

		onClick: function (event) {
			if (event.defaultPrevented || event.button !== 0 || event.detail !== 1) return;
			if (event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
			if (api._pointer !== 'mouse') return;
			const target = event.target;
			if (!target || typeof target.closest !== 'function' || !target.closest('[data-input-region]')) return;
			if (target.closest(interactive)) return;
			const selection = window.getSelection && window.getSelection();
			if (selection && !selection.isCollapsed) return;
			if (api.overlayOpen()) return;
			const field = api.current();
			if (!field || field === document.activeElement) return;
			api.focusField(field);
		},

		// The shell's skip links. In capture, ahead of the router's link handling: they move focus, not the page.
		onSkip: function (event) {
			const link = event.target && typeof event.target.closest === 'function' ? event.target.closest('a[data-skip]') : null;
			if (!link) return;
			event.preventDefault();
			if (link.getAttribute('data-skip') === 'input' && api.focus()) return;
			const main = document.getElementById('main-content');
			if (main) main.focus();
		},

		onFocusIn: function (event) {
			const target = event.target;
			if (target && typeof target.hasAttribute === 'function' && target.hasAttribute('data-primary-input')) {
				api._focused.add(target);
				api._last = target;
			}
		},

		// The control that had focus was taken off the page by a re-render (a channel view's close button, an exit
		// once the room changes, a confirmation's Cancel): focus would be left on <body>, and a screen reader starts
		// again from the top. It goes to the page's main text box instead, or to the page itself on a touch screen,
		// where a field would open the keyboard. Only when nothing else took focus meanwhile (a dialog handing it
		// back, the router's move to the heading), and never for a control that is still on the page.
		onFocusOut: function (event) {
			const left = event.target;
			if (event.relatedTarget || !left || typeof left.isConnected !== 'boolean') return;
			setTimeout(() => {
				if (left.isConnected) return;
				const active = document.activeElement;
				if (active && active !== document.body && active !== document.documentElement) return;
				if (api.overlayOpen()) return;
				if (!window.matchMedia('(pointer: coarse)').matches && api.focus()) return;
				const main = document.getElementById('main-content');
				if (main) main.focus({ preventScroll: true });
			}, 0);
		},

		install: function () {
			if (api._installed) return;
			api._installed = true;
			document.addEventListener('focusout', api.onFocusOut);
			window.addEventListener('keydown', api.onKeyDown);
			document.addEventListener('pointerdown', api.onPointerDown, { capture: true, passive: true });
			document.addEventListener('click', api.onSkip, { capture: true });
			document.addEventListener('click', api.onClick);
			document.addEventListener('focusin', api.onFocusIn);
		},
	};

	window.sharpmushInputFocus = api;
	if (typeof window.addEventListener === 'function' && typeof document.addEventListener === 'function') api.install();
})();
