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
/// Both are guarded on <c>IsConnected</c>, so tearing down an idle terminal is a no-op.
/// </remarks>
public sealed class TerminalSessionTeardown(ITerminalService terminal, IPlayTerminalService playTerminal)
	: IAccountSessionEndingHandler
{
	public async Task OnAccountSessionEndingAsync()
	{
		await QuitAsync(terminal);
		await QuitAsync(playTerminal);
	}

	private static async Task QuitAsync(ITerminalService terminal)
	{
		if (!terminal.IsConnected) return;

		await terminal.SendAsync("QUIT");
		await terminal.DisconnectAsync();
	}
}
