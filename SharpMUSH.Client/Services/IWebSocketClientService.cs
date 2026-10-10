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
	/// Raised when the server refused a reload's resume and started a fresh session: the slot still holds
	/// the screen the reloaded page left (<see cref="TerminalResumeSlot.TakeScrollback"/>), for the
	/// terminal to show above the new session's first lines.
	/// </summary>
	event EventHandler? ResumeRefused;

	/// <summary>
	/// Raised when the server ended the session for good (its <c>{"type":"bye"}</c>: QUIT, a ban, <c>@boot</c>).
	/// No reconnect follows, and nothing the session showed, such as its prompt, applies any more.
	/// </summary>
	event EventHandler? Terminated;

	/// <summary>
	/// Logs a reconnect the server could not resume back in. It then landed in a fresh session at the login
	/// screen; this runs before the commands buffered while disconnected are sent, and is handed the only way
	/// to write to the socket until it returns (<see cref="SendAsync"/> buffers meanwhile). It returns false
	/// when it could not log in; then, as with none, the buffered commands and any typed before the socket was
	/// ready are dropped. Its token is cancelled when that socket closes: the next reconnect logs in instead,
	/// and nothing buffered is dropped.
	/// </summary>
	Func<Func<string, Task>, CancellationToken, Task<bool>>? Relogin { get; set; }

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
	/// Where this connection keeps its resume point and screen, or null when it keeps none (no
	/// identity). The terminal keeps its lines here and restores the stored screen on a resume.
	/// </summary>
	TerminalResumeSlot? ResumeSlot { get; }

	/// <summary>
	/// Connect to the WebSocket server. With an <paramref name="identity"/>, the session's resume point is
	/// kept in sessionStorage under it, and a reloaded page connecting as the same identity resumes the
	/// session; the call then returns once the server has answered the resume (see <see cref="Resumed"/>).
	/// A socket that goes before the answer is not an answer: the resume is tried again with the same
	/// token, and if it never is answered the call throws, so a caller never logs in over the session.
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
