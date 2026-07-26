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

	/// <summary>Read-only snapshot of the in-memory line buffer (up to 2000 lines).</summary>
	IReadOnlyList<TerminalLine> Lines { get; }

	Task ConnectAsync(string serverUri);

	/// <summary>
	/// Connect to <paramref name="serverUri"/> and authenticate with an already-obtained OTT.
	/// Used when the account session provided the token directly.
	/// </summary>
	Task ConnectWithOttAsync(string serverUri, string ott);

	/// <summary>
	/// Connect to <paramref name="serverUri"/> and log in as a temporary guest (<c>connect guest</c>).
	/// Used when an anonymous visitor enters the play area.
	/// </summary>
	Task ConnectAsGuestAsync(string serverUri);

	Task DisconnectAsync();

	/// <summary>Send a raw command string to the MUSH server.</summary>
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
