// Small layout helpers for the responsive shell.
window.sharpmushLayout = {
	// True when the shell is in "touch chrome" mode (off-canvas drawer + bottom nav rather
	// than the desktop sidebar), so the hamburger opens the drawer instead of toggling the
	// desktop rail. MUST stay in sync with the touch-chrome @media condition in custom.css:
	// any touch device (pointer: coarse) OR a narrow window (<=760px).
	isTouchChrome: function () {
		return window.matchMedia('(max-width: 760px), (pointer: coarse)').matches;
	},

	// Back-compat alias.
	isNarrow: function () {
		return this.isTouchChrome();
	}
};
