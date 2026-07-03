using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.RegularExpressions;
using SharpMUSH.Client.Models;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Singleton terminal service that wraps <see cref="IWebSocketClientService"/>, maintains a
/// bounded line buffer, and provides a request/response correlation mechanism for structured
/// MUSH command output.
/// </summary>
public partial class TerminalService(IWebSocketClientService wsService, ILogger<TerminalService> logger)
	: ITerminalService
{
	private const int MaxLines = 2000;
	private const string EndMarkerPrefix = "SHARP_END:";

	private readonly ILogger<TerminalService> _logger = logger;
	private readonly List<TerminalLine> _lines = new(MaxLines);
	private readonly OobChannelStore _oob = new();
	private long? _myPort;
	private string? _serverUri;

	private (string ReqId, TaskCompletionSource<string[]> Tcs, List<string> Buffer)? _pending;
	private readonly SemaphoreSlim _sendSemaphore = new(1, 1);

	// Scopes the post-login port-initialization waiter to the current connection so a
	// stale waiter from a previous/interrupted login cannot initialize the wrong socket.
	private CancellationTokenSource? _loginCts;

	public event Action<TerminalLine>? LineReceived;
	public event Action<bool>? ConnectionStateChanged;

	public bool IsConnected => wsService.IsConnected;

	/// <inheritdoc/>
	public string? ConnectedPlayerName { get; set; }

	/// <inheritdoc/>
	public long? MyPort => _myPort;

	/// <inheritdoc/>
	public IOobChannelStore OobChannels => _oob;

	/// <inheritdoc/>
	public string? ServerUri => _serverUri;

	public IReadOnlyList<TerminalLine> Lines
	{
		get { lock (_lines) return _lines.AsReadOnly(); }
	}

	public async Task ConnectAsync(string serverUri)
	{
		_serverUri = serverUri;
		// New connection/login: drop any OOB payloads from a previous session so the UI never
		// renders stale cross-session data until fresh OOB arrives.
		_oob.Clear();
		wsService.MessageReceived -= HandleMessage;
		wsService.ConnectionStateChanged -= HandleStateChange;
		wsService.Reattached -= HandleReattached;
		wsService.MessageReceived += HandleMessage;
		wsService.ConnectionStateChanged += HandleStateChange;
		wsService.Reattached += HandleReattached;

		_logger.LogInformation("Connecting to {ServerUri}", serverUri);
		await wsService.ConnectAsync(serverUri);
		AddSystemLine($"Connected to {serverUri}");
	}

	/// <inheritdoc/>
	public async Task ConnectWithOttAsync(string serverUri, string ott)
	{
		// Discard any buffered commands from a previous (possibly interrupted) session so they
		// are not flushed to the server before the new connect token is authenticated.
		wsService.ClearSendBuffer();
		await ConnectAsync(serverUri);
		await Task.Delay(300);
		_logger.LogInformation("Using pre-fetched OTT for account character login");
		// Never echo any part of the OTT: ConnectWithOttAsync is used for real account
		// logins, so the token must not leak into the terminal line buffer (or anything
		// inspecting TerminalService.Lines).
		AddSystemLine("[OTT] Sending login token…");
		await wsService.SendAsync($"connect token {ott}");
		AddSystemLine("[OTT] Authenticating…");
		_ = WaitForLoginThenInitializeAsync(BeginLoginWait());
	}

	public async Task ConnectAndLoginAsync(string serverUri, string playerName, string password, OttAuthService ottAuth)
	{
		wsService.ClearSendBuffer();
		await ConnectAsync(serverUri);

		// Give the server a moment to send the connection banner
		await Task.Delay(300);

		var token = await ottAuth.GetTokenAsync(playerName, password);
		if (token is not null)
		{
			_logger.LogInformation("Using OTT login for player {Name}", playerName);
			await wsService.SendAsync($"connect token {token}");
			AddSystemLine("[OTT] Authenticating…");
		}
		else
		{
			// OTT unavailable — fall back to direct connect (password over WSS, still encrypted)
			_logger.LogWarning("OTT unavailable, falling back to direct connect for {Name}", playerName);
			await wsService.SendAsync($"connect {playerName} {password}");
			AddSystemLine("[Login] Connecting with credentials…");
		}

		_ = WaitForLoginThenInitializeAsync(BeginLoginWait());
	}

	/// <summary>
	/// Cancels any in-flight login waiter and starts a fresh cancellation scope for the new
	/// login attempt. Returns the token the new waiter must observe.
	/// </summary>
	private CancellationToken BeginLoginWait()
	{
		_loginCts?.Cancel();
		_loginCts?.Dispose();
		_loginCts = new CancellationTokenSource();
		return _loginCts.Token;
	}

	/// <summary>
	/// Waits for the first server-originated message after login is sent (indicating the server
	/// has processed the connect command and sent MOTD/look), then captures the port descriptor.
	/// Falls back after 10 s to avoid blocking indefinitely on failed logins.
	/// </summary>
	private async Task WaitForLoginThenInitializeAsync(CancellationToken ct)
	{
		var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		void OnLine(TerminalLine line)
		{
			if (line.Source == TerminalLineSource.Server)
				tcs.TrySetResult();
		}

		LineReceived += OnLine;
		try
		{
			// Cancellation (disconnect or a newer login) ends the wait immediately.
			await Task.WhenAny(tcs.Task, Task.Delay(10_000, ct));

			// If this waiter was superseded/cancelled, do not touch the (possibly new or
			// closed) socket — that would re-queue think [ports(me)] on the wrong connection.
			if (ct.IsCancellationRequested) return;

			// Small buffer to let the login response fully settle before querying ports.
			await Task.Delay(300, ct);
			await InitializePortAsync();
		}
		catch (OperationCanceledException)
		{
			// Expected when the connection is torn down or replaced mid-wait.
		}
		finally
		{
			LineReceived -= OnLine;
		}
	}

	public async Task DisconnectAsync()
	{
		// Cancel any pending post-login port initialization so it cannot run against the
		// socket we are about to close (or a later reconnect).
		_loginCts?.Cancel();
		ConnectedPlayerName = null;
		await wsService.DisconnectAsync();
		wsService.MessageReceived -= HandleMessage;
		wsService.ConnectionStateChanged -= HandleStateChange;
		wsService.Reattached -= HandleReattached;
		AddSystemLine("Disconnected.");
	}

	public async Task SendAsync(string command)
	{
		AddLine(command, TerminalLineSource.Client);
		await wsService.SendAsync(command);
	}

	/// <inheritdoc/>
	public async Task SendControlAsync(string controlJson)
	{
		await wsService.SendAsync(controlJson);
	}

	/// <inheritdoc/>
	public async Task InitializePortAsync()
	{
		try
		{
			// ports(me) returns descriptors most-recent-first; the first entry is this connection.
			var lines = await SendCommandAsync("think [ports(me)]", 5000);
			var firstToken = lines.Length > 0 ? lines[0].Trim().Split(' ')[0] : null;
			if (long.TryParse(firstToken, out var port))
			{
				_myPort = port;
				_logger.LogInformation("Editor port captured: {Port}", port);
			}
			else
			{
				_logger.LogWarning("Could not parse port from response: {Response}", string.Join("|", lines));
			}
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Failed to capture editor port number");
		}
	}

	public async Task<string[]> SendCommandAsync(string command, int timeoutMs = 5000)
	{
		await _sendSemaphore.WaitAsync();
		try
		{
			var reqId = Guid.NewGuid().ToString("N")[..8];
			var tcs = new TaskCompletionSource<string[]>(TaskCreationOptions.RunContinuationsAsynchronously);
			_pending = (reqId, tcs, []);

			await wsService.SendAsync(command);

			// Route end-marker to this specific port when known, otherwise fall back to @pemit me=
			var endMarkerCmd = _myPort.HasValue
				? $"think [pemit({_myPort.Value}, {EndMarkerPrefix}{reqId})]"
				: $"@pemit me={EndMarkerPrefix}{reqId}";
			await wsService.SendAsync(endMarkerCmd);

			using var cts = new CancellationTokenSource(timeoutMs);
			cts.Token.Register(() => tcs.TrySetResult(_pending?.Buffer.ToArray() ?? []));

			return await tcs.Task;
		}
		finally
		{
			_pending = null;
			_sendSemaphore.Release();
		}
	}

	private void HandleMessage(object? sender, string message)
	{
		var frame = TerminalFrameRenderer.Parse(message);

		switch (frame.Kind)
		{
			case TerminalFrameKind.Markup:
			{
				var plainTrimmed = frame.Plain.TrimEnd('\r', '\n');

				// The correlation end-marker arrives as its own markup frame.
				if (TryCompletePendingRequest(plainTrimmed))
					return;

				// Buffer the response lines for any active correlated request.
				if (_pending.HasValue)
					foreach (var raw in frame.Plain.Split('\n'))
						_pending.Value.Buffer.Add(raw.TrimEnd('\r'));

				if (!string.IsNullOrEmpty(plainTrimmed))
					AddLine(frame.Plain, frame.Html, TerminalLineSource.Server);
				return;
			}

			case TerminalFrameKind.Html:
				if (!string.IsNullOrEmpty(frame.Html))
					AddLine(frame.Plain, frame.Html, TerminalLineSource.Server);
				return;

			case TerminalFrameKind.Oob:
				// Structured out-of-band data: route to the channel store; never displayed.
				_oob.Set(frame.Package, frame.DataJson);
				return;

			default:
				// Plain text (banners / pre-markup server text): legacy line-by-line, ANSI-stripped.
				foreach (var raw in message.Split('\n'))
				{
					var line = StripAnsi(raw.TrimEnd('\r'));

					if (TryCompletePendingRequest(line))
						continue;

					_pending?.Buffer.Add(line);

					if (!string.IsNullOrEmpty(line))
						AddLine(line, TerminalLineSource.Server);
				}
				return;
		}
	}

	/// <summary>
	/// If <paramref name="line"/> is the correlation end-marker for the pending request, completes it
	/// and returns true (the marker must not be surfaced in the terminal).
	/// </summary>
	private bool TryCompletePendingRequest(string line)
	{
		if (_pending.HasValue && line == $"{EndMarkerPrefix}{_pending.Value.ReqId}")
		{
			var (_, tcs, buffer) = _pending.Value;
			_pending = null;
			tcs.TrySetResult(buffer.ToArray());
			return true;
		}

		return false;
	}

	private void HandleStateChange(object? sender, WebSocketState state)
	{
		var connected = state == WebSocketState.Open;
		ConnectionStateChanged?.Invoke(connected);
		AddSystemLine(connected ? "Connection established." : $"Connection state: {state}");
	}

	/// <summary>
	/// The server rebound this reconnect to the still-live session — we are already authenticated, so
	/// cancel any pending login wait and do not re-login. The character never left.
	/// </summary>
	private void HandleReattached(object? sender, EventArgs e)
	{
		_loginCts?.Cancel();
		AddSystemLine("Session resumed — reconnected without re-login.");
	}

	private void AddSystemLine(string text) => AddLine(text, TerminalLineSource.System);

	private void AddLine(string text, TerminalLineSource source)
	{
		var line = new TerminalLine(DateTime.Now, text, source);
		AddLine(line);
	}

	private void AddLine(string text, string html, TerminalLineSource source)
	{
		var line = new TerminalLine(DateTime.Now, text, html, source);
		AddLine(line);
	}

	private void AddLine(TerminalLine line)
	{
		lock (_lines)
		{
			if (_lines.Count >= MaxLines)
				_lines.RemoveAt(0);
			_lines.Add(line);
		}
		LineReceived?.Invoke(line);
	}

	[GeneratedRegex(@"\x1B\[[0-9;]*[mGKHFJABCDsu]|\x1B\[[\d;]*[HJK]", RegexOptions.Compiled)]
	private static partial Regex AnsiRegex();

	private static string StripAnsi(string text) => AnsiRegex().Replace(text, string.Empty);
}
