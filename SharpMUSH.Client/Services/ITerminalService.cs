using SharpMUSH.Client.Models;

namespace SharpMUSH.Client.Services;

public interface ITerminalService : IAsyncDisposable
{
	/// <summary>Fires on every new terminal line received from the server or sent by the client.</summary>
	event Action<TerminalLine>? LineReceived;

	/// <summary>Fires when the underlying WebSocket connection state changes.</summary>
	event Action<bool>? ConnectionStateChanged;

	bool IsConnected { get; }

	/// <summary>The player name that authenticated on this connection, or null if unknown.</summary>
	string? ConnectedPlayerName { get; set; }

	/// <summary>The last server URI connected to (or null if never connected).</summary>
	string? ServerUri { get; }

	/// <summary>Who this connection plays as, when it logged in as an account's character; null for a guest or a typed login.</summary>
	TerminalIdentity? Identity => null;

	/// <summary>Read-only snapshot of the in-memory line buffer (up to 2000 lines).</summary>
	IReadOnlyList<TerminalLine> Lines { get; }

	/// <summary>
	/// The prompt shown above the input line, or null for none. A prompt frame replaces it and never enters
	/// <see cref="Lines"/>; a clear frame for its session (or naming none) empties it. Sending a line copies it
	/// into the scrollback first, and clears it when it is a one-off prompt with no session behind it.
	/// </summary>
	TerminalPrompt? Prompt { get; }

	/// <summary>Fires whenever <see cref="Prompt"/> changes, including to null.</summary>
	event Action? PromptChanged;

	Task ConnectAsync(string serverUri);

	/// <summary>
	/// Connect to <paramref name="serverUri"/> and authenticate with an already-obtained OTT.
	/// Used when the account session provided the token directly.
	/// </summary>
	/// <param name="identity">
	/// The account and character the OTT logs in as. The session's resume point is kept under it, so a
	/// reload within the server's grace period resumes the session (still logged in, and the OTT unused)
	/// instead of logging in again. Null keeps nothing.
	/// </param>
	Task ConnectWithOttAsync(string serverUri, string ott, TerminalIdentity? identity = null);

	/// <summary>
	/// Connect to <paramref name="serverUri"/> and log in as a temporary guest (<c>connect guest</c>).
	/// Used when an anonymous visitor enters the play area.
	/// </summary>
	Task ConnectAsGuestAsync(string serverUri);

	Task DisconnectAsync();

	/// <summary>
	/// Send a line the player entered (typed, or a command link) to the MUSH server. The shown
	/// <see cref="Prompt"/> goes into the scrollback ahead of it, as on telnet.
	/// </summary>
	Task SendAsync(string command);

	/// <summary>
	/// Send a raw control frame (JSON envelope) to the server without echoing it as a terminal
	/// line. Used for client→server control messages such as NAWS window-size reports.
	/// </summary>
	Task SendControlAsync(string controlJson);

	/// <summary>
	/// Evaluate a softcode <paramref name="expression"/> and return its result as response lines.
	/// The expression is wrapped as <c>think [null(oob(me, query.&lt;reqId&gt;, json(string, &lt;expression&gt;)))]</c>
	/// so the result returns over the out-of-band channel and never appears in the visible terminal;
	/// the call completes when the matching <c>query.&lt;reqId&gt;</c> OOB envelope arrives (or on timeout).
	/// </summary>
	Task<string[]> SendCommandAsync(string expression, int timeoutMs = 5000);

	/// <summary>Latest out-of-band channel payloads received on this connection.</summary>
	IOobChannelStore OobChannels { get; }
}
