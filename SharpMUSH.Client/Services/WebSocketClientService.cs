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

	// Pending while a resume frame awaits the server's answer: reattached (true), or a fresh session (false).
	private TaskCompletionSource<bool>? _verdict;

	/// <summary>How long a connect waits for the server to answer a resume before treating it as refused.</summary>
	private static readonly TimeSpan VerdictTimeout = TimeSpan.FromSeconds(30);

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
	public event EventHandler<WebSocketState>? ConnectionStateChanged;

	public bool IsConnected => _webSocket?.State == WebSocketState.Open;

	/// <inheritdoc/>
	public bool Resumed { get; private set; }

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

		await AdoptIdentityAsync(identity);
		await ConnectInternalAsync();
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

	private async Task ConnectInternalAsync()
	{
		if (_serverUri is null) return;

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

			_logger.LogInformation("Connecting to WebSocket server: {ServerUri}", LogSanitizer.Sanitize(_serverUri));
			await _webSocket.ConnectAsync(new Uri(_serverUri), _cancellationTokenSource.Token);

			ConnectionStateChanged?.Invoke(this, _webSocket.State);
			_logger.LogInformation("Connected to WebSocket server");

			// Mandatory first frame: resume on reconnect (we hold a token), else hello. The server
			// uses this to rebind a reconnect to the existing session or register a fresh one.
			Resumed = false;
			var verdict = _resumeToken is not null
				? new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously)
				: null;
			_verdict = verdict;
			// A fresh session numbers its frames from 1.
			if (verdict is null) _lastSeq = 0;
			var firstFrame = _resumeToken is not null
				? ResumeFrameParser.Resume(_resumeToken, _lastSeq, PresenceClass)
				: ResumeFrameParser.Hello(PresenceClass);
			await _webSocket.SendAsync(
				new ArraySegment<byte>(Encoding.UTF8.GetBytes(firstFrame)),
				WebSocketMessageType.Text, true, _cancellationTokenSource.Token);

			await FlushSendBufferAsync();

			_receiveTask = ReceiveMessagesAsync(_cancellationTokenSource.Token);

			// Wait for the server's answer to a resume, so a caller knows whether the session is the one
			// it left (still logged in) or a fresh one that needs a login.
			if (verdict is not null)
				Resumed = await AwaitVerdictAsync(verdict, _cancellationTokenSource.Token);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error connecting to WebSocket server");
			throw;
		}
	}

	/// <summary>
	/// Send a message to the server, buffering it if currently disconnected.
	/// </summary>
	public async Task SendAsync(string message)
	{
		if (_webSocket?.State == WebSocketState.Open)
		{
			try
			{
				var bytes = Encoding.UTF8.GetBytes(message);
				await _webSocket.SendAsync(
					new ArraySegment<byte>(bytes),
					WebSocketMessageType.Text,
					true,
					_cancellationTokenSource?.Token ?? CancellationToken.None);
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

	private async Task<bool> AwaitVerdictAsync(TaskCompletionSource<bool> verdict, CancellationToken ct)
	{
		try
		{
			return await verdict.Task.WaitAsync(VerdictTimeout, ct);
		}
		catch (TimeoutException)
		{
			_logger.LogWarning("The server did not answer the resume within {Timeout}s; treating the session as fresh",
				VerdictTimeout.TotalSeconds);
			return false;
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
			_verdict?.TrySetResult(false);
			if (_slot is { } ended) await ended.ClearAsync();
			return;
		}

		if (ResumeFrameParser.IsReattached(message))
		{
			// The session continues, still logged in: no re-login needed.
			_verdict?.TrySetResult(true);
			Reattached?.Invoke(this, EventArgs.Empty);
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
		verdict.TrySetResult(false);
	}

	private async Task ReceiveMessagesAsync(CancellationToken cancellationToken)
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

		// The socket went before the server answered a resume.
		_verdict?.TrySetResult(false);

		// Attempt automatic reconnection if the disconnect was neither client-intentional nor an
		// engine-initiated logout (the server's {"type":"bye"}).
		if (!_intentionalDisconnect && !_serverTerminated && _serverUri is not null)
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
				await ConnectInternalAsync();

				if (_webSocket?.State == WebSocketState.Open)
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
