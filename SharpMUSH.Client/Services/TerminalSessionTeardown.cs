namespace SharpMUSH.Client.Services;

/// <summary>
/// Ends the game-side session when the portal session ends.
/// </summary>
/// <remarks>
/// While a character session is active the terminals are auto-connected to the ConnectionServer, and
/// leaving them up keeps the character in <c>lwho()</c> — shown as online — after the portal session
/// is gone.
/// <para>
/// A bare socket close is NOT enough. The ConnectionServer treats a client-side close as a DROP and
/// holds the session for a ~120s grace window (<c>ConnectionPump</c>: <c>sink.Detach()</c> +
/// <c>detachedTracker.Detach(handle, grace)</c>), during which the character is still in
/// <c>lwho()</c>. Sending the game <c>QUIT</c> command instead routes to
/// <c>ConnectionService.Disconnect</c> and a <c>DisconnectConnectionMessage</c> whose consumer calls
/// the FORCED <c>DisconnectAsync(handle)</c> — removing the handle from the live registry
/// immediately, so <c>lwho()</c> drops it at once, and emitting a <c>{"type":"bye"}</c> frame so the
/// client will not auto-reconnect. The follow-up <c>DisconnectAsync</c> is a safety net that closes
/// the client socket and latches the intentional-disconnect flag; <c>SendAsync</c> awaits the flush,
/// so QUIT is already on the wire first.
/// </para>
/// Every character session's play terminal goes: the sessions <c>/client</c> keeps in the background close, so the
/// next account to sign in here starts with one. Each is guarded on <c>IsConnected</c>, so tearing down an idle
/// terminal is a no-op.
/// <para>
/// Every stored resume point goes too, the idle terminals' included: on a shared browser, whoever
/// reloads the tab next must not resume the session that was just logged out.
/// </para>
/// </remarks>
public sealed class TerminalSessionTeardown(
	ITerminalService terminal, CharacterSessionRegistry sessions, TerminalResumeStore resumePoints)
	: IAccountSessionEndingHandler
{
	public async Task OnAccountSessionEndingAsync()
	{
		try
		{
			await QuitAsync(terminal);
			await QuitAsync(sessions.Focused.Terminal);
			foreach (var session in sessions.Sessions.Where(session => session != sessions.Focused).ToList())
				await CloseAsync(session);
		}
		finally
		{
			await resumePoints.ClearAllAsync();
		}
	}

	/// <summary>One character's connection failing to close leaves the others to close.</summary>
	private async Task CloseAsync(CharacterSession session)
	{
		try
		{
			await sessions.CloseAsync(session);
		}
		catch (Exception ex) when (ex is not OutOfMemoryException)
		{
			// The socket is going anyway; the server's grace window ends the character's connection.
		}
	}

	/// <summary>A terminal whose QUIT fails is still closed, and the ones after it still go.</summary>
	private static async Task QuitAsync(ITerminalService terminal)
	{
		if (!terminal.IsConnected) return;

		try
		{
			await terminal.SendAsync("QUIT");
		}
		catch (Exception ex) when (ex is not OutOfMemoryException)
		{
			// Closed below; the server's grace window ends the character's connection.
		}

		try
		{
			await terminal.DisconnectAsync();
		}
		catch (Exception ex) when (ex is not OutOfMemoryException)
		{
			// The socket is going anyway.
		}
	}
}
