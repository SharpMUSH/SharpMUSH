// Small layout helpers for the responsive shell.
// The mobile breakpoint MUST match the 760px used throughout custom.css.
window.sharpmushLayout = {
	// True when the viewport is at/below the mobile breakpoint, so the hamburger
	// opens the off-canvas drawer instead of toggling the desktop rail.
	isNarrow: function () {
		return window.matchMedia('(max-width: 760px)').matches;
	}
};
