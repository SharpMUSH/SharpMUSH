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

	/// <summary>
	/// The port descriptor of this WebSocket connection, captured after login via
	/// <see cref="InitializePortAsync"/>.  Null until initialization completes.
	/// </summary>
	long? MyPort { get; }

	/// <summary>Read-only snapshot of the in-memory line buffer (up to 2000 lines).</summary>
	IReadOnlyList<TerminalLine> Lines { get; }

	Task ConnectAsync(string serverUri);

	/// <summary>
	/// Connect to the WebSocket server and authenticate using a One-Time Token fetched from
	/// the SharpMUSH API.  Falls back to direct <c>connect name password</c> if the OTT
	/// request fails (e.g. API server unreachable).
	/// </summary>
	Task ConnectAndLoginAsync(string serverUri, string playerName, string password, OttAuthService ottAuth);

	/// <summary>
	/// Connect to <paramref name="serverUri"/> and authenticate with an already-obtained OTT.
	/// Used when the account session provided the token directly.
	/// </summary>
	Task ConnectWithOttAsync(string serverUri, string ott);

	Task DisconnectAsync();

	/// <summary>Send a raw command string to the MUSH server.</summary>
	Task SendAsync(string command);

	/// <summary>
	/// Send a raw control frame (JSON envelope) to the server without echoing it as a terminal
	/// line. Used for client→server control messages such as NAWS window-size reports.
	/// </summary>
	Task SendControlAsync(string controlJson);

	/// <summary>
	/// Queries the server for the current connection's port descriptor and stores it in
	/// <see cref="MyPort"/>.  Should be called once, ~1.5 s after a successful login, so that
	/// subsequent <see cref="SendCommandAsync"/> calls route their end-markers — and
	/// <see cref="MushQueryService"/> routes its output — to this connection only.
	/// </summary>
	Task InitializePortAsync();

	/// <summary>
	/// Send a command and collect all response lines until a unique end-marker is echoed back
	/// by the server.  A <c>@pemit me=SHARP_END:&lt;id&gt;</c> command is appended automatically.
	/// </summary>
	Task<string[]> SendCommandAsync(string command, int timeoutMs = 5000);

	/// <summary>Latest out-of-band channel payloads received on this connection.</summary>
	IOobChannelStore OobChannels { get; }
}
