using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using SharpMUSH.Library.Logging;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Service for managing WebSocket connections to SharpMUSH ConnectionServer.
/// Includes automatic reconnection with exponential backoff, keep-alive, and
/// message buffering during disconnected periods so no output is lost when a
/// user moves between cell towers or has a brief connectivity interruption.
/// </summary>
public class WebSocketClientService : IWebSocketClientService
{

	private readonly ILogger<WebSocketClientService> _logger;
	private ClientWebSocket? _webSocket;
	private CancellationTokenSource? _cancellationTokenSource;
	private Task? _receiveTask;
	private string? _serverUri;
	private volatile bool _intentionalDisconnect;

	// Set when the server sends a {"type":"bye"} frame — an engine-initiated logout (QUIT / ban / @boot).
	// Like an intentional client disconnect, it suppresses auto-reconnect: the session is gone for good,
	// so reconnecting would only land at a fresh login prompt. Reset by the next explicit ConnectAsync.
	private volatile bool _serverTerminated;

	// Reconnect replay is always on: the client unwraps {"type":"seq","seq","data"} envelopes (tracking
	// the highest seq), stores the server's resume token, and re-sends {"type":"resume","token","lastSeq"}
	// on reconnect so the ConnectionServer replays output missed during a drop / network switch. With an
	// identity, the pair is also kept in sessionStorage (TerminalResumeStore), so a reload resumes too.
	private string? _resumeToken;
	private long _lastSeq;

	private readonly TerminalResumeStore _resumeStore;
	private TerminalIdentity? _identity;
	private TerminalResumeSlot? _slot;

	/// <summary>The server's answer to a resume frame, or that the socket went before it gave one.</summary>
	private enum ResumeVerdict
	{
		/// <summary><c>reattached</c>: the session continues, still logged in.</summary>
		Resumed,

		/// <summary>A fresh session's token or frame came first: the server refused the resume.</summary>
		Fresh,

		/// <summary>
		/// No answer: the socket dropped, or the server was silent too long. The session may still be there
		/// to rebind, so the resume is tried again with the same token and lastSeq, and nothing that assumes
		/// a fresh session (a login) may be sent meanwhile.
		/// </summary>
		Interrupted,
	}

	// Pending while a resume frame awaits the server's answer.
	private TaskCompletionSource<ResumeVerdict>? _verdict;

	/// <summary>How long a connect waits for the server to answer a resume before abandoning that socket.</summary>
	private static readonly TimeSpan VerdictTimeout = TimeSpan.FromSeconds(30);

	/// <summary>
	/// The waits between tries of a resume whose answer never came, inside the server's 120 s grace
	/// period. After the last, <see cref="ConnectAsync"/> fails rather than log in over the session.
	/// </summary>
	private static readonly TimeSpan[] ResumeRetryDelays =
		[TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(16)];

	/// <summary>Maximum number of messages to buffer while disconnected.</summary>
	private const int MaxBufferedMessages = 500;

	/// <summary>Messages queued for sending while disconnected.</summary>
	private readonly ConcurrentQueue<string> _sendBuffer = new();

	/// <summary>Initial delay between reconnection attempts.</summary>
	private static readonly TimeSpan InitialReconnectDelay = TimeSpan.FromSeconds(1);

	/// <summary>Maximum delay between reconnection attempts.</summary>
	private static readonly TimeSpan MaxReconnectDelay = TimeSpan.FromSeconds(30);

	public event EventHandler<string>? MessageReceived;

	/// <summary>
	/// Raised when the server confirms a rebind to an existing (still-logged-in) session, so the
	/// terminal can skip the re-login it would otherwise run after a reconnect.
	/// </summary>
	public event EventHandler? Reattached;

	/// <inheritdoc/>
	public event EventHandler? ResumeRefused;

	/// <inheritdoc/>
	public Func<Func<string, Task>, CancellationToken, Task<bool>>? Relogin { get; set; }

	/// <summary>
	/// Set when a reconnect landed in a fresh session, at the login screen, and cleared once it logged in again
	/// or could not. The token the server handed that session resumes it, still at the login screen, so a
	/// reconnect while this is set logs in even when the server resumes it.
	/// </summary>
	private bool _loginOwed;

	/// <summary>
	/// The socket a reconnect is still setting up (answering its resume, logging in again). When it closes,
	/// that reconnect tries again itself, so its receive loop starts no second one.
	/// </summary>
	private ClientWebSocket? _reconnecting;
	public event EventHandler<WebSocketState>? ConnectionStateChanged;

	/// <summary>
	/// Open, and its hello/resume frame sent. Before that the socket is not usable by anyone else: the
	/// server reads anything ahead of the first frame as a login-screen command, and in the browser
	/// <see cref="ClientWebSocket.State"/> already reads Open while <c>ConnectAsync</c> is still
	/// finishing, when a send throws "not connected" (the terminal's resize observer hit that window).
	/// </summary>
	public bool IsConnected => _webSocket is { State: WebSocketState.Open } socket && ReferenceEquals(socket, _greeted);

	/// <summary>The socket whose first frame has gone out; a reconnect's new socket is not it until then.</summary>
	private ClientWebSocket? _greeted;

	/// <summary>
	/// The socket <see cref="SendAsync"/> writes to: greeted, its resume answered, logged in again when it
	/// had to be, and the buffer flushed. Until then a send is buffered, so nothing typed during a reconnect
	/// reaches a login screen ahead of the login or goes out while the flush is writing.
	/// </summary>
	private ClientWebSocket? _ready;

	/// <summary>
	/// A ready socket left at the login screen because logging in again failed. A send begun before it was
	/// ready was typed for the session that was lost and is dropped; one begun after is the reader's own login.
	/// </summary>
	private ClientWebSocket? _loggedOut;

	/// <summary>Serializes writes to <see cref="_ready"/>: a send against the flush that opens it.</summary>
	private readonly SemaphoreSlim _sendLock = new(1, 1);

	/// <inheritdoc/>
	public bool Resumed { get; private set; }

	/// <inheritdoc/>
	public TerminalResumeSlot? ResumeSlot => _slot;

	/// <summary>
	/// The presence class declared on this connection's first frame: "play" for a real interactive game
	/// session, "portal" for a background query connection the portal opens for out-of-band lookups (which
	/// must NOT count as a player being online). Set once at construction/registration; rides on both the
	/// hello and the resume first frame so it survives reconnects. Defaults to "play".
	/// </summary>
	// Literal (not PresenceClasses.Play) because the browser bundle deliberately does not reference
	// SharpMUSH.Library — see the ProjectReference note in SharpMUSH.Client.csproj. Keep in sync.
	public string PresenceClass { get; set; } = "play";

	public WebSocketClientService(ILogger<WebSocketClientService> logger, TerminalResumeStore resumeStore)
	{
		_logger = logger;
		_resumeStore = resumeStore;
	}

	/// <inheritdoc/>
	public async Task ConnectAsync(string serverUri, TerminalIdentity? identity = null)
	{
		if (_webSocket?.State == WebSocketState.Open)
		{
			_logger.LogWarning("Already connected to WebSocket server");
			return;
		}

		_serverUri = serverUri;
		_intentionalDisconnect = false;
		_serverTerminated = false;
		// The caller logs this connection in itself, unless it resumes a session that already is.
		_loginOwed = false;

		await AdoptIdentityAsync(identity);

		// A resume the server never answered is tried again, as it was: only the server's own answer
		// says whether the session is there. Giving up throws, so a caller never logs in over it.
		for (var retry = 0; await ConnectInternalAsync(reconnect: false) == ResumeVerdict.Interrupted; retry++)
		{
			if (retry == ResumeRetryDelays.Length || _intentionalDisconnect)
				throw new WebSocketException("The server did not answer the session resume.");
			_logger.LogInformation("The resume was interrupted; trying it again in {Delay}s", ResumeRetryDelays[retry].TotalSeconds);
			await Task.Delay(ResumeRetryDelays[retry]);
		}
	}

	/// <summary>
	/// Points this client at <paramref name="identity"/>'s resume point. A different identity (or none,
	/// after one) drops the session held in memory: a connection as someone else never resumes it.
	/// </summary>
	private async Task AdoptIdentityAsync(TerminalIdentity? identity)
	{
		if (identity == _identity && (identity is null || _slot is { Revoked: false }))
			return;

		_identity = identity;
		_slot = null;
		_resumeToken = null;
		_lastSeq = 0;
		if (identity is not { } id)
			return;

		_slot = await _resumeStore.OpenAsync(PresenceClass, id);
		if (_slot.Stored is TerminalResumePoint point)
		{
			_resumeToken = point.Token;
			_lastSeq = point.LastSeq;
		}
	}

	/// <summary>
	/// Opens a socket and sends the first frame; the server's answer when that was a resume. The commands
	/// buffered while disconnected go out once the session is known to be logged in: at once when the
	/// server resumed it, and on a <paramref name="reconnect"/> that landed in a fresh session (at the login
	/// screen) only after <see cref="Relogin"/>, or never when there is none to run.
	/// </summary>
	private async Task<ResumeVerdict?> ConnectInternalAsync(bool reconnect)
	{
		if (_serverUri is null) return null;

		ClientWebSocket? socket = null;
		try
		{
			_webSocket?.Dispose();
			_webSocket = new ClientWebSocket();

			// KeepAliveInterval is not supported in the browser (Blazor WASM)
			if (!OperatingSystem.IsBrowser())
			{
				_webSocket.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);
			}

			_cancellationTokenSource = new CancellationTokenSource();
			socket = _webSocket;
			if (reconnect) _reconnecting = socket;

			_logger.LogInformation("Connecting to WebSocket server: {ServerUri}", LogSanitizer.Sanitize(_serverUri));
			await _webSocket.ConnectAsync(new Uri(_serverUri), _cancellationTokenSource.Token);

			_logger.LogInformation("Connected to WebSocket server");

			// Mandatory first frame: resume on reconnect (we hold a token), else hello. The server
			// uses this to rebind a reconnect to the existing session or register a fresh one.
			Resumed = false;
			var verdict = _resumeToken is not null
				? new TaskCompletionSource<ResumeVerdict>(TaskCreationOptions.RunContinuationsAsynchronously)
				: null;
			_verdict = verdict;
			// A fresh session numbers its frames from 1, and whatever an earlier page stored for this
			// identity (a screen above all) belongs to a session this one is not.
			if (verdict is null)
			{
				_lastSeq = 0;
				if (_slot is { } stale) await stale.ClearAsync();
			}
			var firstFrame = _resumeToken is not null
				? ResumeFrameParser.Resume(_resumeToken, _lastSeq, PresenceClass)
				: ResumeFrameParser.Hello(PresenceClass);
			await _webSocket.SendAsync(
				new ArraySegment<byte>(Encoding.UTF8.GetBytes(firstFrame)),
				WebSocketMessageType.Text, true, _cancellationTokenSource.Token);

			_greeted = _webSocket;

			// Announced only now: a listener that reacts by sending (the terminal reports its NAWS on
			// connect) must not put its frame ahead of the hello/resume, which the server requires
			// first — anything else is read as a login-screen command ("No such command available
			// at login.").
			ConnectionStateChanged?.Invoke(this, _webSocket.State);

			// Cancelled, then disposed, when this socket's receive loop ends: whatever waits on this socket
			// stops waiting. The token is taken first because a disposed source no longer hands one out.
			var gone = new CancellationTokenSource();
			var socketGone = gone.Token;
			_receiveTask = ReceiveMessagesAsync(_webSocket, verdict, gone, _cancellationTokenSource.Token);

			// Wait for the server's answer to a resume, so a caller knows whether the session is the one
			// it left (still logged in) or a fresh one that needs a login.
			ResumeVerdict? answer = null;
			if (verdict is not null)
			{
				answer = await AwaitVerdictAsync(verdict, _cancellationTokenSource.Token);
				Resumed = answer == ResumeVerdict.Resumed;
				// Nobody knows yet what the session is: the commands wait for the next try.
				if (answer == ResumeVerdict.Interrupted) return answer;
			}

			if (reconnect && answer != ResumeVerdict.Resumed) _loginOwed = true;
			bool loggedIn;
			if (!reconnect || (answer == ResumeVerdict.Resumed && !_loginOwed))
				loggedIn = true;
			else if (await ReloginAsync(_webSocket, socketGone) is { } relogged)
				loggedIn = relogged;
			else
				// The socket closed while logging in: the reconnect loop tries again, with the commands kept.
				return ResumeVerdict.Interrupted;
			_loginOwed = false;

			await _sendLock.WaitAsync(_cancellationTokenSource.Token);
			try
			{
				// What was typed for the lost session is not read as login-screen commands.
				if (!loggedIn) ClearSendBuffer();
				await FlushSendBufferAsync();
				_loggedOut = loggedIn ? null : _webSocket;
				_ready = _webSocket;
			}
			finally
			{
				_sendLock.Release();
			}
			return answer;
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error connecting to WebSocket server");
			throw;
		}
		finally
		{
			if (socket is not null && ReferenceEquals(_reconnecting, socket)) _reconnecting = null;
		}
	}

	/// <summary>
	/// Send a message to the server, buffering it if currently disconnected.
	/// </summary>
	public async Task SendAsync(string message)
	{
		var readyBefore = _ready;
		await _sendLock.WaitAsync();
		try
		{
			if (_ready is { } ready && !ReferenceEquals(ready, readyBefore) && ReferenceEquals(ready, _loggedOut))
			{
				_logger.LogDebug("Dropping a command typed for the session a reconnect could not log back in");
				return;
			}

			// One read of the socket: the one checked for readiness is the one written to, even if a reconnect
			// replaces it meanwhile.
			if (_webSocket is { State: WebSocketState.Open } socket && ReferenceEquals(socket, _ready))
			{
				try
				{
					await WriteAsync(socket, message);
					return;
				}
				catch (OperationCanceledException)
				{
					throw;
				}
				catch (WebSocketException ex)
				{
					_logger.LogWarning(ex, "Failed to send message, buffering for retry");
				}
			}

			if (_sendBuffer.Count < MaxBufferedMessages)
			{
				_sendBuffer.Enqueue(message);
			}
			else
			{
				_logger.LogWarning("Send buffer full ({Max} messages), dropping message", MaxBufferedMessages);
			}
		}
		finally
		{
			_sendLock.Release();
		}
	}

	private Task WriteAsync(ClientWebSocket socket, string message) =>
		socket.SendAsync(
			new ArraySegment<byte>(Encoding.UTF8.GetBytes(message)),
			WebSocketMessageType.Text,
			true,
			_cancellationTokenSource?.Token ?? CancellationToken.None);

	/// <summary>
	/// Disconnect from the server
	/// </summary>
	public async Task DisconnectAsync()
	{
		_intentionalDisconnect = true;

		if (_webSocket?.State == WebSocketState.Open)
		{
			try
			{
				_cancellationTokenSource?.Cancel();
				await _webSocket.CloseAsync(
					WebSocketCloseStatus.NormalClosure,
					"Client disconnecting",
					CancellationToken.None);

				ConnectionStateChanged?.Invoke(this, _webSocket.State);
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error disconnecting from WebSocket server");
			}
		}

		_webSocket?.Dispose();
		_cancellationTokenSource?.Dispose();
		_webSocket = null;
		_cancellationTokenSource = null;
		ClearSendBuffer();
	}

	/// <inheritdoc/>
	public void ClearSendBuffer()
	{
		var count = _sendBuffer.Count;
		_sendBuffer.Clear();
		if (count > 0)
			_logger.LogDebug("Send buffer cleared ({Count} stale messages discarded)", count);
	}

	/// <summary>
	/// Logs a reconnect that could not resume back in, before anything buffered goes out. False when there is
	/// no way to or it failed: the session stays at the login screen.
	/// </summary>
	/// <returns>Null when <paramref name="socketGone"/> fired first: the socket closed before a login could go out.</returns>
	private async Task<bool?> ReloginAsync(ClientWebSocket socket, CancellationToken socketGone)
	{
		if (Relogin is not { } relogin) return false;

		try
		{
			// The login goes straight to this socket; nothing else writes to it until it is ready.
			return await relogin(message => WriteAsync(socket, message), socketGone);
		}
		catch (OperationCanceledException) when (socketGone.IsCancellationRequested)
		{
			_logger.LogInformation("The socket closed before the reconnect could log in; the next one will");
			return null;
		}
		catch (WebSocketException) when (socket.State != WebSocketState.Open)
		{
			_logger.LogInformation("The socket closed while the reconnect was logging in; the next one will");
			return null;
		}
		// Any other cancellation is not a failed login either: it ends this attempt, and the reconnect loop takes it.
		catch (Exception ex) when (ex is WebSocketException or HttpRequestException or InvalidOperationException)
		{
			_logger.LogWarning(ex, "Logging in again after a reconnect failed");
			return false;
		}
	}

	private async Task<ResumeVerdict> AwaitVerdictAsync(TaskCompletionSource<ResumeVerdict> verdict, CancellationToken ct)
	{
		try
		{
			return await verdict.Task.WaitAsync(VerdictTimeout, ct);
		}
		catch (TimeoutException)
		{
			// Silence is not a refusal: abandon this socket (its receive loop then leaves the retry to the
			// caller) and try the resume again.
			_logger.LogWarning("The server did not answer the resume within {Timeout}s; abandoning that socket",
				VerdictTimeout.TotalSeconds);
			verdict.TrySetResult(ResumeVerdict.Interrupted);
			_cancellationTokenSource?.Cancel();
			_webSocket?.Abort();
			return ResumeVerdict.Interrupted;
		}
	}

	/// <summary>
	/// Raises <see cref="MessageReceived"/>, first intercepting the control frames. A resume-token frame
	/// is consumed silently; a seq envelope has its sequence tracked and its inner payload surfaced, unless
	/// this page already showed it. Anything else is passed through unchanged, so a resume-capable client
	/// talking to a non-sequencing server still works.
	/// </summary>
	private async Task SurfaceMessageAsync(string message)
	{
		if (ResumeFrameParser.IsBye(message))
		{
			// Engine-initiated logout: the session is over for good. Suppress auto-reconnect and drop the
			// resume state so a later explicit reconnect starts fresh (a hello), not a resume-to-dead that
			// would replay the old session's goodbye output, and so a reload does not try either.
			_serverTerminated = true;
			_resumeToken = null;
			_lastSeq = 0;
			_verdict?.TrySetResult(ResumeVerdict.Fresh);
			if (_slot is { } ended) await ended.ClearAsync();
			return;
		}

		if (ResumeFrameParser.IsReattached(message))
		{
			// The session continues, still logged in: no re-login needed. The event goes first, so its
			// handlers have run by the time the verdict lets ConnectAsync return.
			Reattached?.Invoke(this, EventArgs.Empty);
			_verdict?.TrySetResult(ResumeVerdict.Resumed);
			return;
		}

		if (ResumeFrameParser.TryReadResumeToken(message, out var token) && token is not null)
		{
			await SettleAsFreshIfUnansweredAsync();
			_resumeToken = token;
			if (_slot is { } slot) await slot.SaveAsync(new TerminalResumePoint(token, _lastSeq));
			return;
		}

		if (ResumeFrameParser.TryReadSeq(message, out var seq, out var data))
		{
			await SettleAsFreshIfUnansweredAsync();
			// A frame this page already showed, sent again.
			if (seq <= _lastSeq) return;
			_lastSeq = seq;
			if (_slot is { } slot && _resumeToken is { } current)
				await slot.StageAsync(new TerminalResumePoint(current, seq));
			if (data is not null)
				MessageReceived?.Invoke(this, data);
			return;
		}

		MessageReceived?.Invoke(this, message);
	}

	/// <summary>
	/// The server answers a resume with <c>reattached</c> before anything else. A token or a frame first
	/// means it refused the resume (the token expired, or the session ended) and registered a fresh
	/// session, whose frames count from 1 again: forget the refused token, here and in storage.
	/// </summary>
	private async ValueTask SettleAsFreshIfUnansweredAsync()
	{
		if (_verdict is not { Task.IsCompleted: false } verdict) return;

		_lastSeq = 0;
		_resumeToken = null;
		if (_slot is { } slot) await slot.ClearAsync();
		verdict.TrySetResult(ResumeVerdict.Fresh);
		// The stored point is gone, but the screen it kept is still in the slot: the same character in the same
		// tab, so the reloaded page shows it above the fresh session rather than starting blank.
		ResumeRefused?.Invoke(this, EventArgs.Empty);
	}

	/// <param name="socket">The socket this loop reads.</param>
	/// <param name="verdict">This socket's pending resume answer, or null for a hello.</param>
	/// <param name="gone">Cancelled and disposed when the loop ends; the loop owns it.</param>
	private async Task ReceiveMessagesAsync(ClientWebSocket socket, TaskCompletionSource<ResumeVerdict>? verdict,
		CancellationTokenSource gone, CancellationToken cancellationToken)
	{
		var buffer = new byte[1024 * 4];
		using var messageBuffer = new MemoryStream();

		try
		{
			while (_webSocket?.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
			{
				// A single server message (e.g. a serialized markup envelope) can exceed the receive
				// buffer and arrive as several fragments. Accumulate until EndOfMessage and decode the
				// complete UTF-8 payload once, so callers never see a partial (invalid JSON) frame or a
				// multi-byte character split across a chunk boundary.
				WebSocketReceiveResult result;
				messageBuffer.SetLength(0);

				do
				{
					result = await _webSocket.ReceiveAsync(
						new ArraySegment<byte>(buffer),
						cancellationToken);

					if (result.MessageType == WebSocketMessageType.Close)
					{
						_logger.LogInformation("WebSocket server closed the connection");
						ConnectionStateChanged?.Invoke(this, WebSocketState.Closed);
						break;
					}

					messageBuffer.Write(buffer, 0, result.Count);
				}
				while (!result.EndOfMessage);

				// Server-initiated close: leave the receive loop so reconnection can run.
				if (result.MessageType == WebSocketMessageType.Close)
					break;

				if (result.MessageType == WebSocketMessageType.Text && messageBuffer.Length > 0)
				{
					var message = Encoding.UTF8.GetString(messageBuffer.GetBuffer(), 0, (int)messageBuffer.Length);
					await SurfaceMessageAsync(message);
				}
			}
		}
		catch (OperationCanceledException)
		{
			_logger.LogInformation("WebSocket receive cancelled");
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error receiving WebSocket messages");
			ConnectionStateChanged?.Invoke(this, WebSocketState.Aborted);
		}

		// The socket went before the server answered its resume. Whoever is waiting for that answer (a
		// connect, or the reconnect loop) tries the resume again; reconnecting from here as well would race
		// it with a second socket.
		var unanswered = verdict is not null
			&& (verdict.TrySetResult(ResumeVerdict.Interrupted) || verdict.Task.Result == ResumeVerdict.Interrupted);

		// Likewise a socket a reconnect was still setting up: that reconnect tries again.
		var reconnecting = ReferenceEquals(_reconnecting, socket);
		gone.Cancel();
		gone.Dispose();

		// Attempt automatic reconnection if the disconnect was neither client-intentional nor an
		// engine-initiated logout (the server's {"type":"bye"}).
		if (!unanswered && !reconnecting && !_intentionalDisconnect && !_serverTerminated && _serverUri is not null)
		{
			_ = Task.Run(async () =>
			{
				try
				{
					await ReconnectAsync();
				}
				catch (OperationCanceledException)
				{
					// Expected during intentional disconnect
				}
				catch (Exception ex)
				{
					_logger.LogError(ex, "Unhandled exception during WebSocket reconnection");
				}
			});
		}
	}

	private async Task ReconnectAsync()
	{
		var delay = InitialReconnectDelay;
		var attempt = 0;

		while (!_intentionalDisconnect && !_serverTerminated)
		{
			attempt++;
			_logger.LogInformation("Attempting reconnection (attempt {Attempt}, delay {Delay}s)...",
				attempt, delay.TotalSeconds);

			await Task.Delay(delay);

			try
			{
				// An unanswered resume is a failed attempt: the next one resumes again, as it was.
				if (await ConnectInternalAsync(reconnect: true) != ResumeVerdict.Interrupted && _webSocket?.State == WebSocketState.Open)
				{
					_logger.LogInformation("Reconnected successfully after {Attempt} attempt(s).", attempt);
					return;
				}
			}
			catch (OperationCanceledException)
			{
				_logger.LogDebug("Reconnection attempt {Attempt} cancelled", attempt);
			}
			catch (WebSocketException ex)
			{
				_logger.LogWarning(ex, "Reconnection attempt {Attempt} failed", attempt);
			}

			delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, MaxReconnectDelay.Ticks));
		}

		_logger.LogInformation("Reconnection stopped after {Attempts} attempt(s) due to intentional disconnect.", attempt);
	}

	private async Task FlushSendBufferAsync()
	{
		while (_sendBuffer.TryDequeue(out var message))
		{
			if (_webSocket?.State != WebSocketState.Open) break;

			try
			{
				var bytes = Encoding.UTF8.GetBytes(message);
				await _webSocket.SendAsync(
					new ArraySegment<byte>(bytes),
					WebSocketMessageType.Text,
					true,
					_cancellationTokenSource?.Token ?? CancellationToken.None);
			}
			catch (OperationCanceledException)
			{
				if (_sendBuffer.Count < MaxBufferedMessages)
				{
					_sendBuffer.Enqueue(message);
				}
				else
				{
					_logger.LogWarning("Send buffer full, dropping message during flush");
				}
				break;
			}
			catch (WebSocketException ex)
			{
				_logger.LogWarning(ex, "Failed to flush buffered message");
				if (_sendBuffer.Count < MaxBufferedMessages)
				{
					_sendBuffer.Enqueue(message);
				}
				else
				{
					_logger.LogWarning("Send buffer full, dropping message during flush");
				}
				break;
			}
		}
	}

	public async ValueTask DisposeAsync()
	{
		await DisconnectAsync();
		GC.SuppressFinalize(this);
	}
}
