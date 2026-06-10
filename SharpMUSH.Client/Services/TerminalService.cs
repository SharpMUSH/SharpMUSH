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
	private long? _myPort;
	private string? _serverUri;

	// Pending correlated request: (request-id, completion source, accumulated lines)
	private (string ReqId, TaskCompletionSource<string[]> Tcs, List<string> Buffer)? _pending;
	private readonly SemaphoreSlim _sendSemaphore = new(1, 1);

	public event Action<TerminalLine>? LineReceived;
	public event Action<bool>? ConnectionStateChanged;

	public bool IsConnected => wsService.IsConnected;

	/// <inheritdoc/>
	public string? ConnectedPlayerName { get; set; }

	/// <inheritdoc/>
	public long? MyPort => _myPort;

	/// <inheritdoc/>
	public string? ServerUri => _serverUri;

	public IReadOnlyList<TerminalLine> Lines
	{
		get { lock (_lines) return _lines.AsReadOnly(); }
	}

	public async Task ConnectAsync(string serverUri)
	{
		_serverUri = serverUri;
		wsService.MessageReceived -= HandleMessage;
		wsService.ConnectionStateChanged -= HandleStateChange;
		wsService.MessageReceived += HandleMessage;
		wsService.ConnectionStateChanged += HandleStateChange;

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
		_ = WaitForLoginThenInitializeAsync();
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

		_ = WaitForLoginThenInitializeAsync();
	}

	/// <summary>
	/// Waits for the first server-originated message after login is sent (indicating the server
	/// has processed the connect command and sent MOTD/look), then captures the port descriptor.
	/// Falls back after 10 s to avoid blocking indefinitely on failed logins.
	/// </summary>
	private async Task WaitForLoginThenInitializeAsync()
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
			await Task.WhenAny(tcs.Task, Task.Delay(10_000));
		}
		finally
		{
			LineReceived -= OnLine;
		}

		// Small buffer to let the login response fully settle before querying ports
		await Task.Delay(300);
		await InitializePortAsync();
	}

	public async Task DisconnectAsync()
	{
		ConnectedPlayerName = null;
		await wsService.DisconnectAsync();
		wsService.MessageReceived -= HandleMessage;
		wsService.ConnectionStateChanged -= HandleStateChange;
		AddSystemLine("Disconnected.");
	}

	public async Task SendAsync(string command)
	{
		AddLine(command, TerminalLineSource.Client);
		await wsService.SendAsync(command);
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
		foreach (var raw in message.Split('\n'))
		{
			var line = StripAnsi(raw.TrimEnd('\r'));

			// Check for our correlation end-marker
			if (_pending.HasValue && line == $"{EndMarkerPrefix}{_pending.Value.ReqId}")
			{
				var (_, tcs, buffer) = _pending.Value;
				_pending = null;
				tcs.TrySetResult(buffer.ToArray());
				// Don't surface the marker line in the terminal
				continue;
			}

			// Buffer lines for any active correlated request
			_pending?.Buffer.Add(line);

			if (!string.IsNullOrEmpty(line))
				AddLine(line, TerminalLineSource.Server);
		}
	}

	private void HandleStateChange(object? sender, WebSocketState state)
	{
		var connected = state == WebSocketState.Open;
		ConnectionStateChanged?.Invoke(connected);
		AddSystemLine(connected ? "Connection established." : $"Connection state: {state}");
	}

	private void AddSystemLine(string text) => AddLine(text, TerminalLineSource.System);

	private void AddLine(string text, TerminalLineSource source)
	{
		var line = new TerminalLine(DateTime.Now, text, source);
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
