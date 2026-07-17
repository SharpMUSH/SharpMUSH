namespace SharpMUSH.Client.Services;

/// <summary>
/// Canonical character-switch flow, shared by every surface that lets a player switch which
/// character they're playing: the topbar chrome (<c>MainLayout</c>), the post-login
/// <c>CharacterPicker</c>, and the nav account panel (<c>NavMenu</c>/<c>AccountPanel</c>). Extracted
/// out of <c>MainLayout.SwitchCharacterAsync</c> once the same sequence started
/// drifting between three copy-pasted call sites — the nav panel's copy quietly dropped the terminal
/// reconnect and <see cref="AccountAuthService.InvalidateDebugOtt"/> steps, leaving its "switch"
/// button rename the profile card without ever reconnecting the terminal as the new character.
/// </summary>
/// <remarks>
/// Depends on the concrete <see cref="TerminalServiceHost"/>/<see cref="PlayTerminalServiceHost"/>
/// facades rather than <see cref="ITerminalService"/>/<see cref="IPlayTerminalService"/> because only
/// the concrete facades expose <c>RecreateAsync</c> — deliberately absent from the plain interfaces
/// to keep other consumers from recreating the connection out from under a switch in progress. Both
/// facades are DI singletons (see <see cref="TerminalServiceCollectionExtensions"/>),
/// so this service is safe to register as a singleton too.
/// </remarks>
public class CharacterSwitchService(
	AccountAuthService accountAuth,
	TerminalServiceHost terminal,
	PlayTerminalServiceHost playTerminal)
{
	private const string DefaultServerUri = "ws://localhost:4202/ws";

	/// <summary>
	/// Mints a fresh OTT for <paramref name="character"/> (which also commits it as
	/// <see cref="AccountAuthService.ActiveCharacter"/> — every consumer reading that property updates
	/// in the same tick, so callers must NOT call <c>SetActiveCharacter</c> themselves), then recreates
	/// and reconnects the command terminal. The play terminal is recreated too but deliberately left
	/// disconnected — see the inline comment below.
	/// </summary>
	/// <param name="serverUriOverride">
	/// When set, used verbatim instead of the command terminal's current <c>ServerUri</c>. Needed by
	/// <c>CharacterPicker</c>, which runs before any terminal connection exists (so
	/// <c>Terminal.ServerUri</c> is still null) and instead knows the target server from its own
	/// <c>ServerUri</c> parameter.
	/// </param>
	/// <returns>
	/// <c>false</c> if minting the OTT failed (e.g. an expired account session) — nothing else in this
	/// method ran, and identity was NOT touched. <c>true</c> otherwise, including when the final
	/// <see cref="TerminalServiceHost.ConnectWithOttAsync"/> call itself throws: identity has already
	/// committed by that point, and this method does not swallow that failure — it propagates to the
	/// caller, which decides how to surface it (a failed auto-login is a terminal error with a retry,
	/// not a rollback; there is a test pinning this no-rollback behavior).
	/// </returns>
	public async Task<bool> SwitchAsync(AccountAuthService.CharacterSummary character, string? serverUriOverride = null)
	{
		var token = await accountAuth.SwitchCharacterAsync(character);
		if (token is null) return false;

		// Recreate rather than reconnect. WebSocketClientService's resume token survives a
		// DisconnectAsync, so reconnecting sends a resume frame and the server may rebind the socket to
		// the PREVIOUS character's session — silently discarding the new OTT. Recreating sidesteps that:
		// THIS flow's fresh inner client starts with a null resume token and sends hello, which cannot
		// rebind. That guarantee holds only because this flow recreates — it is not a general property
		// of WebSocketClientService. DisconnectAsync itself never clears _resumeToken/_lastSeq, so other
		// connect entry points that reconnect a surviving client instead of recreating one (notably
		// NavMenu.HandleLogoutAsync, which calls DisconnectAsync, not RecreateAsync) can still carry a
		// tainted inner into their next connect. Capture the URI BEFORE recreating: the new inner's
		// ServerUri is null until it connects.
		var serverUri = serverUriOverride ?? terminal.ServerUri ?? DefaultServerUri;
		accountAuth.InvalidateDebugOtt();

		await terminal.RecreateAsync();
		// The play terminal was never switched before this: nothing in the codebase disconnected it, so
		// it stayed logged in as the old character. Recreating it here leaves it honestly disconnected
		// rather than stale — RecreateAsync's ConnectionStateChanged(false) reports that truthfully to
		// any subscriber. /play does NOT auto-reconnect it: GlobalTerminal's only auto-start runs in
		// OnInitializedAsync, once, at first mount, and there is no other reconnect entry point. The
		// player must revisit/reload /play to log back in. This is a deliberate, accepted limitation
		// (still strictly better than the old silent-stale-login behavior) — do not "fix" this comment to
		// claim reconnection happens somewhere.
		await playTerminal.RecreateAsync();

		// Identity commits regardless of whether the connection succeeds; a failed auto-login surfaces
		// as a terminal error with a retry, not a rollback. No try/catch here on purpose.
		await terminal.ConnectWithOttAsync(serverUri, token);
		return true;
	}
}
