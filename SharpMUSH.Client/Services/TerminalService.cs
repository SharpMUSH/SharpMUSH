using System.Net.WebSockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using SharpMUSH.Client.Models;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Singleton terminal service that wraps <see cref="IWebSocketClientService"/>, maintains a
/// bounded line buffer, and provides a request/response correlation mechanism for structured
/// MUSH command output.
/// </summary>
/// <remarks>
/// Structured queries never touch the visible text stream. Each <see cref="SendCommandAsync"/>
/// wraps its softcode expression as <c>think [null(oob(me, query.&lt;reqId&gt;, json(string, &lt;expr&gt;)))]</c>:
/// the engine returns the value over the out-of-band channel as a <c>{"type":"oob","package":"query.&lt;reqId&gt;",…}</c>
/// envelope, which the client routes here and never displays — so neither the response nor any
/// correlation sentinel leaks into the terminal.
/// </remarks>
public partial class TerminalService(IWebSocketClientService wsService, ILogger<TerminalService> logger)
	: ITerminalService
{
	private const int MaxLines = 2000;
	private const string QueryPackagePrefix = "query.";

	private readonly ILogger<TerminalService> _logger = logger;
	private readonly List<TerminalLine> _lines = new(MaxLines);
	private readonly OobChannelStore _oob = new();
	private string? _serverUri;

	private (string ReqId, TaskCompletionSource<string[]> Tcs)? _pending;
	private readonly SemaphoreSlim _sendSemaphore = new(1, 1);

	private bool _disposed;

	public event Action<TerminalLine>? LineReceived;
	public event Action<bool>? ConnectionStateChanged;

	public bool IsConnected => wsService.IsConnected;

	/// <inheritdoc/>
	public string? ConnectedPlayerName { get; set; }

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
		UnsubscribeWebSocketHandlers();
		wsService.MessageReceived += HandleMessage;
		wsService.ConnectionStateChanged += HandleStateChange;
		wsService.Reattached += HandleReattached;

		_logger.LogInformation("Connecting to {ServerUri}", serverUri);
		await wsService.ConnectAsync(serverUri);
		AddSystemLine($"Connected to {serverUri}");
	}

	private void UnsubscribeWebSocketHandlers()
	{
		wsService.MessageReceived -= HandleMessage;
		wsService.ConnectionStateChanged -= HandleStateChange;
		wsService.Reattached -= HandleReattached;
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
	}

	public async Task ConnectAsGuestAsync(string serverUri)
	{
		wsService.ClearSendBuffer();
		await ConnectAsync(serverUri);
		await Task.Delay(300);
		AddSystemLine("[Guest] Connecting…");
		await wsService.SendAsync("connect guest");
	}

	public async Task DisconnectAsync()
	{
		ConnectedPlayerName = null;
		await wsService.DisconnectAsync();
		UnsubscribeWebSocketHandlers();
		AddSystemLine("Disconnected.");
	}

	/// <summary>
	/// Tears the instance down so a replacement can be built cleanly. Unsubscribes the websocket
	/// handlers wired in <see cref="ConnectAsync"/> and disposes the send semaphore and the
	/// websocket client.
	/// </summary>
	/// <remarks>
	/// Recreation — rather than reconnection — is what makes a character switch safe: a fresh
	/// <see cref="IWebSocketClientService"/> starts with a null resume token and therefore sends
	/// hello instead of resume, so the server cannot rebind the socket to the previous
	/// character's session.
	/// </remarks>
	public async ValueTask DisposeAsync()
	{
		if (_disposed) return;
		_disposed = true;

		UnsubscribeWebSocketHandlers();

		_sendSemaphore.Dispose();

		LineReceived = null;
		ConnectionStateChanged = null;

		await wsService.DisposeAsync();
		GC.SuppressFinalize(this);
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

	public async Task<string[]> SendCommandAsync(string expression, int timeoutMs = 5000)
	{
		await _sendSemaphore.WaitAsync();
		try
		{
			var reqId = Guid.NewGuid().ToString("N")[..8];
			var tcs = new TaskCompletionSource<string[]>(TaskCreationOptions.RunContinuationsAsynchronously);
			_pending = (reqId, tcs);

			// The result is returned over the out-of-band channel, so it never reaches the visible
			// stream. json(string, …) escapes the value; null(…) discards oob()'s send-count so
			// think does not echo it; oob(me, …) targets this player's own connection(s).
			await wsService.SendAsync($"think [null(oob(me, {QueryPackagePrefix}{reqId}, json(string, {expression})))]");

			using var cts = new CancellationTokenSource(timeoutMs);
			cts.Token.Register(() => tcs.TrySetResult([]));

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
					if (!string.IsNullOrEmpty(plainTrimmed))
						AddLine(frame.Plain, frame.Html, TerminalLineSource.Server);
					return;
				}

			case TerminalFrameKind.Html:
				if (!string.IsNullOrEmpty(frame.Html))
					AddLine(frame.Plain, frame.Html, TerminalLineSource.Server);
				return;

			case TerminalFrameKind.Oob:
				// A "query.<reqId>" package is a correlated request response: complete the waiter and
				// drop it. Such frames are internal — never stored as channel data, never displayed —
				// even when orphaned (their request already timed out).
				if (frame.Package.StartsWith(QueryPackagePrefix, StringComparison.Ordinal))
				{
					TryCompletePendingRequest(frame.Package, frame.DataJson);
					return;
				}

				// Structured out-of-band data: route to the channel store; never displayed.
				_oob.Set(frame.Package, frame.DataJson);
				return;

			default:
				// Plain text (banners / pre-markup server text): legacy line-by-line, ANSI-stripped.
				foreach (var raw in message.Split('\n'))
				{
					var line = StripAnsi(raw.TrimEnd('\r'));
					if (!string.IsNullOrEmpty(line))
						AddLine(line, TerminalLineSource.Server);
				}
				return;
		}
	}

	/// <summary>
	/// If <paramref name="package"/> is the correlation package for the pending request, completes it
	/// with the response lines and returns true. The OOB data is a JSON payload (typically a JSON
	/// string produced by <c>json(string, …)</c>); it is decoded back into the lines the callers parse.
	/// </summary>
	private bool TryCompletePendingRequest(string package, string dataJson)
	{
		if (_pending is not { } pending || package != $"{QueryPackagePrefix}{pending.ReqId}")
			return false;

		_pending = null;
		pending.Tcs.TrySetResult(OobDataToLines(dataJson));
		return true;
	}

	/// <summary>
	/// Decodes an OOB data payload into response lines. A JSON string is unwrapped to its content;
	/// any other JSON value is used as its raw text. The result is split on newlines.
	/// </summary>
	private static string[] OobDataToLines(string dataJson)
	{
		if (string.IsNullOrEmpty(dataJson))
			return [];

		string text;
		try
		{
			using var doc = JsonDocument.Parse(dataJson);
			text = doc.RootElement.ValueKind == JsonValueKind.String
				? doc.RootElement.GetString() ?? string.Empty
				: dataJson;
		}
		catch (JsonException)
		{
			text = dataJson;
		}

		if (text.Length == 0)
			return [];

		var parts = text.Split('\n');
		for (var i = 0; i < parts.Length; i++)
			parts[i] = parts[i].TrimEnd('\r');
		return parts;
	}

	private void HandleStateChange(object? sender, WebSocketState state)
	{
		var connected = state == WebSocketState.Open;
		ConnectionStateChanged?.Invoke(connected);
		AddSystemLine(connected ? "Connection established." : $"Connection state: {state}");
	}

	/// <summary>
	/// The server rebound this reconnect to the still-live session — we are already authenticated,
	/// so we do not re-login. The character never left.
	/// </summary>
	private void HandleReattached(object? sender, EventArgs e)
	{
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
