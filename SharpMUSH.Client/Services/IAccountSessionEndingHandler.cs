namespace SharpMUSH.Client.Services;

/// <summary>
/// Something to finish while the account session still exists, run by
/// <see cref="AccountAuthService.LogoutAsync"/> before it clears any local state.
/// </summary>
/// <remarks>
/// <para>This exists so the auth service does not have to know what else a session was holding open.
/// It used to inject <c>ITerminalService</c> and <c>IPlayTerminalService</c> and drive the game-side
/// disconnect itself, which put the terminal layer underneath authentication; the terminals now
/// register their own teardown (<see cref="TerminalSessionTeardown"/>).</para>
///
/// <para>Handlers are awaited, not fired and forgotten, and they run at the same point in
/// <c>LogoutAsync</c> the inlined teardown did: after the server-side logout call, before the token
/// and cached identity are dropped. That ordering is the whole point — a handler that needs to talk
/// to the game has to do it while there is still a session to talk with. Logout is the single
/// chokepoint every entry point routes through, so no caller can forget one.</para>
///
/// <para>A handler that throws is logged and the logout continues. Ending the local session is not
/// allowed to depend on a best-effort cleanup succeeding: the alternative is a tab that failed to
/// hang up a socket and is also still signed in.</para>
/// </remarks>
public interface IAccountSessionEndingHandler
{
	Task OnAccountSessionEndingAsync();
}
