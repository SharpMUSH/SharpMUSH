using SharpMUSH.Client.Models;

namespace SharpMUSH.Client.Services;

public interface ITerminalService
{
	/// <summary>Fires on every new terminal line received from the server or sent by the client.</summary>
	event Action<TerminalLine>? LineReceived;

	/// <summary>Fires when the underlying WebSocket connection state changes.</summary>
	event Action<bool>? ConnectionStateChanged;

	bool IsConnected { get; }

	/// <summary>Read-only snapshot of the in-memory line buffer (up to 2000 lines).</summary>
	IReadOnlyList<TerminalLine> Lines { get; }

	Task ConnectAsync(string serverUri);
	Task DisconnectAsync();

	/// <summary>Send a raw command string to the MUSH server.</summary>
	Task SendAsync(string command);

	/// <summary>
	/// Send a command and collect all response lines until a unique end-marker is echoed back
	/// by the server.  A <c>@pemit me=SHARP_END:&lt;id&gt;</c> command is appended automatically.
	/// </summary>
	Task<string[]> SendCommandAsync(string command, int timeoutMs = 5000);
}
