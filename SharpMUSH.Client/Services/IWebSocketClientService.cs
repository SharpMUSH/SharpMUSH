using System.Net.WebSockets;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Interface for managing WebSocket connections to SharpMUSH ConnectionServer
/// </summary>
public interface IWebSocketClientService : IAsyncDisposable
{
	/// <summary>
	/// Event raised when a message is received from the server
	/// </summary>
	event EventHandler<string>? MessageReceived;

	/// <summary>
	/// Event raised when the connection state changes
	/// </summary>
	event EventHandler<WebSocketState>? ConnectionStateChanged;

	/// <summary>
	/// Raised when the server confirms a reconnect rebound to the existing (still-logged-in) session,
	/// so consumers can skip any re-authentication and keep their session state.
	/// </summary>
	event EventHandler? Reattached;

	/// <summary>
	/// Gets whether the WebSocket is currently connected
	/// </summary>
	bool IsConnected { get; }

	/// <summary>
	/// True when the last connect resumed the session this client (or, after a reload, this tab) held:
	/// the server rebound the socket to it, still logged in. False for a fresh session.
	/// </summary>
	bool Resumed { get; }

	/// <summary>
	/// Connect to the WebSocket server. With an <paramref name="identity"/>, the session's resume point is
	/// kept in sessionStorage under it, and a reloaded page connecting as the same identity resumes the
	/// session; the call then returns once the server has answered the resume (see <see cref="Resumed"/>).
	/// </summary>
	/// <param name="serverUri">WebSocket server URI (e.g., ws://localhost:4202/ws)</param>
	/// <param name="identity">Who the connection logs in as, or null to keep nothing (a guest).</param>
	Task ConnectAsync(string serverUri, TerminalIdentity? identity = null);

	/// <summary>
	/// Send a message to the server
	/// </summary>
	/// <param name="message">Message to send</param>
	Task SendAsync(string message);

	/// <summary>
	/// Disconnect from the server
	/// </summary>
	Task DisconnectAsync();

	/// <summary>
	/// Discard any messages queued in the send buffer.
	/// Call before an OTT login to prevent stale commands from being flushed pre-authentication.
	/// </summary>
	void ClearSendBuffer();
}
