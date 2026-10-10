using System.Net.WebSockets;
using System.Text.Json;
using MarkupString.Ansi;
using SharpMUSH.Client.Models;
using SharpMUSH.Library.Logging;

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
/// <param name="loginTokens">
/// Mints the token that logs a reconnect the server could not resume back in. Without it, such a
/// reconnect stays at the login screen.
/// </param>
/// <param name="log">
/// Keeps the server's lines for the character, when the player turned the longer log on. Only the play
/// terminal has one.
/// </param>
public partial class TerminalService(IWebSocketClientService wsService, ILogger<TerminalService> logger,
	ITerminalLoginTokens? loginTokens = null, TerminalLog? log = null)
	: ITerminalService
{
	/// <summary>The most lines the buffer holds; a terminal's own transcript holds as many.</summary>
	public const int MaxLines = 2000;

	private const string QueryPackagePrefix = "query.";

	private readonly ILogger<TerminalService> _logger = logger;
	private readonly List<TerminalLine> _lines = new(MaxLines);
	private readonly OobChannelStore _oob = new();
	private string? _serverUri;

	private (string ReqId, TaskCompletionSource<string[]> Tcs)? _pending;
	private readonly SemaphoreSlim _sendSemaphore = new(1, 1);

	private bool _disposed;

	// Guarded by _lines: the receive loop sets it while the renderer sends.
	private TerminalPrompt? _prompt;

	public event Action<TerminalLine>? LineReceived;
	public event Action<bool>? ConnectionStateChanged;

	/// <inheritdoc/>
	public event Action? PromptChanged;

	public bool IsConnected => wsService.IsConnected;

	/// <inheritdoc/>
	public string? ConnectedPlayerName { get; set; }

	/// <inheritdoc/>
	public IOobChannelStore OobChannels => _oob;

	/// <inheritdoc/>
	public string? ServerUri => _serverUri;

	/// <inheritdoc/>
	public TerminalIdentity? Identity { get; private set; }

	public IReadOnlyList<TerminalLine> Lines
	{
		// A copy taken under the lock: a view of the live list would be enumerated outside it while the
		// receive loop appends.
		get { lock (_lines) return _lines.ToArray(); }
	}

	/// <inheritdoc/>
	public TerminalPrompt? Prompt
	{
		get { lock (_lines) return _prompt; }
	}

	public Task ConnectAsync(string serverUri) => ConnectAsync(serverUri, identity: null, relogin: null);

	/// <param name="relogin">Logs a reconnect the server could not resume back in (<see cref="IWebSocketClientService.Relogin"/>).</param>
	private async Task ConnectAsync(string serverUri, TerminalIdentity? identity,
		Func<Func<string, Task>, CancellationToken, Task<bool>>? relogin)
	{
		_serverUri = serverUri;
		Identity = identity;
		// New connection/login: drop any OOB payloads from a previous session so the UI never
		// renders stale cross-session data until fresh OOB arrives.
		_oob.Clear();
		UnsubscribeWebSocketHandlers();
		wsService.MessageReceived += HandleMessage;
		wsService.ConnectionStateChanged += HandleStateChange;
		wsService.Reattached += HandleReattached;
		wsService.Terminated += HandleTerminated;
		wsService.ResumeRefused += HandleResumeRefused;
		wsService.Relogin = relogin;

		_logger.LogInformation("Connecting to {ServerUri}", LogSanitizer.Sanitize(serverUri));
		await wsService.ConnectAsync(serverUri, identity);
		AddSystemLine($"Connected to {serverUri}");
	}

	private void UnsubscribeWebSocketHandlers()
	{
		wsService.MessageReceived -= HandleMessage;
		wsService.ConnectionStateChanged -= HandleStateChange;
		wsService.Reattached -= HandleReattached;
		wsService.Terminated -= HandleTerminated;
		wsService.ResumeRefused -= HandleResumeRefused;
		wsService.Relogin = null;
	}

	/// <inheritdoc/>
	public async Task ConnectWithOttAsync(string serverUri, string ott, TerminalIdentity? identity = null)
	{
		// Discard any buffered commands from a previous (possibly interrupted) session so they
		// are not flushed to the server before the new connect token is authenticated.
		wsService.ClearSendBuffer();
		// No pause before the login line. A fixed 300 ms sleep used to stand here, from before the
		// server held early input: WebSocketInputConsumer now waits for the connection to register
		// (ConnectionIncarnation.WaitForRegistrationAsync) before running it, so a line sent the moment
		// the socket opens is no longer lost — and every sign-in paid the sleep.
		await ConnectAsync(serverUri, identity,
			identity is { } character && loginTokens is { } tokens ? (send, gone) => ReloginAsync(tokens, character, send, gone) : null);
		// A reload resumed the session this tab held: it is still logged in, and the login line would run
		// in it as a command.
		if (wsService.Resumed)
		{
			_logger.LogInformation("Resumed the previous session; the OTT is not used");
			return;
		}
		_logger.LogInformation("Using pre-fetched OTT for account character login");
		// Never echo any part of the OTT: ConnectWithOttAsync is used for real account
		// logins, so the token must not leak into the terminal line buffer (or anything
		// inspecting TerminalService.Lines).
		AddSystemLine("[OTT] Sending login token…");
		await wsService.SendAsync($"connect token {ott}");
		AddSystemLine("[OTT] Authenticating…");
	}

	/// <summary>
	/// The waits between asks for a login token the server could not be reached for. The last repeats for as
	/// long as the reconnected socket stays open. Settable for tests.
	/// </summary>
	public IReadOnlyList<TimeSpan> LoginTokenRetryDelays { get; init; } =
		[TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(15)];

	/// <summary>
	/// Logs a reconnect the server could not resume back in as the same character, with a fresh token.
	/// Nothing is said in the terminal unless that cannot be done, when the reader has to log in themselves.
	/// </summary>
	/// <remarks>
	/// A reboot brings the connection server back before the game: the socket opens, but the token is asked
	/// of a game that is not answering yet. That is no reason to give up on the login, so the ask is repeated
	/// until the game answers, or until <paramref name="socketGone"/> says this socket closed. Only an answer
	/// (signed out, the character gone, refused) ends it without a login.
	/// </remarks>
	private async Task<bool> ReloginAsync(ITerminalLoginTokens tokens, TerminalIdentity identity, Func<string, Task> send,
		CancellationToken socketGone)
	{
		for (var attempt = 0; ; attempt++)
		{
			switch (await tokens.MintAsync(identity))
			{
				case string ott:
					_logger.LogInformation("Logging the reconnected terminal in again");
					await send($"connect token {ott}");
					return true;

				case ApiFailure { Kind: ApiFailureKind.Transport or ApiFailureKind.Unexpected } failure:
					var delay = LoginTokenRetryDelays[Math.Min(attempt, LoginTokenRetryDelays.Count - 1)];
					_logger.LogInformation("The game did not answer for a login token ({Message}); asking again in {Delay}s",
						failure.Message, delay.TotalSeconds);
					await Task.Delay(delay, socketGone);
					break;

				default:
					_logger.LogWarning("No login token for the reconnected terminal; it stays at the login screen");
					AddSystemLine("Reconnected, but could not log back in. Log in again to continue.");
					return false;
			}
		}
	}

	public async Task ConnectAsGuestAsync(string serverUri)
	{
		wsService.ClearSendBuffer();
		// No pause: the server holds early input until the connection registers (ConnectWithOttAsync).
		// A guest has no resume point, so a reconnect is a new guest.
		await ConnectAsync(serverUri, identity: null, relogin: async (send, _) => { await send("connect guest"); return true; });
		AddSystemLine("[Guest] Connecting…");
		await wsService.SendAsync("connect guest");
	}

	public async Task DisconnectAsync()
	{
		ConnectedPlayerName = null;
		await wsService.DisconnectAsync();
		UnsubscribeWebSocketHandlers();
		// The session the prompt asked for is gone; a reconnect's resume brings back its own.
		ClearPrompt(session: string.Empty);
		AddSystemLine("Disconnected.");
	}

	/// <summary>
	/// Tears the instance down so a replacement can be built cleanly. Unsubscribes the websocket
	/// handlers wired in <see cref="ConnectAsync"/> and disposes the send semaphore and the
	/// websocket client.
	/// </summary>
	/// <remarks>
	/// Recreation — rather than reconnection — is what makes a character switch safe: a fresh
	/// <see cref="IWebSocketClientService"/> starts with no resume token in memory, and the one it may
	/// read from storage is keyed by the identity it connects as, so it sends hello instead of resume
	/// and the server cannot rebind the socket to the previous character's session.
	/// </remarks>
	public async ValueTask DisposeAsync()
	{
		if (_disposed) return;
		_disposed = true;

		UnsubscribeWebSocketHandlers();

		_sendSemaphore.Dispose();

		LineReceived = null;
		ConnectionStateChanged = null;
		PromptChanged = null;

		await wsService.DisposeAsync();
		GC.SuppressFinalize(this);
	}

	public async Task SendAsync(string command)
	{
		// The prompt answered goes into the scrollback above the answer, so the transcript reads as telnet's
		// does. A one-off prompt is over once answered; an @input session's stays until its next prompt or
		// its clear.
		TerminalPrompt? answered;
		lock (_lines)
		{
			answered = _prompt;
			if (answered is { Session.Length: 0 })
				_prompt = null;
		}
		if (answered is not null)
		{
			AddLine(new TerminalLine(DateTime.Now, answered.Line.Text, answered.Line.Html, TerminalLineSource.Server));
			if (answered.Session.Length == 0)
				PromptCleared();
		}
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
			case TerminalFrameKind.Markup when frame.Prompt:
				ShowPrompt(new TerminalPrompt(new TerminalLine(DateTime.Now, frame.Plain.TrimEnd('\r', '\n'), frame.Html, TerminalLineSource.Server),
					frame.Session));
				return;

			case TerminalFrameKind.PromptClear:
				ClearPrompt(frame.Session);
				return;

			case TerminalFrameKind.Markup:
				{
					// A frame can be all action and no text — a sound(), a stopsound(), a clearscreen() —
					// and its HTML still has to reach the page.
					var plainTrimmed = frame.Plain.TrimEnd('\r', '\n');
					if (!string.IsNullOrEmpty(plainTrimmed) || !string.IsNullOrWhiteSpace(frame.Html))
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
				var text = message.AsSpan();
				foreach (var range in text.Split('\n'))
				{
					var raw = text[range].TrimEnd('\r');
					if (raw.IsEmpty)
						continue;
					var line = AnsiEscapeParser.Parse(raw.ToString()).ToPlainText();
					if (line.Length > 0)
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

	/// <summary>
	/// The socket's state is shown by the page's connection indicator, not in the scrollback: a drop and the
	/// reconnect that follows (a network blip, a server update) leave the screen as it was.
	/// </summary>
	/// <summary>
	/// The server ended the session (QUIT, a ban, <c>@boot</c>). It dropped the connection before the session's
	/// own clear could be sent, so the prompt comes down here.
	/// </summary>
	private void HandleTerminated(object? sender, EventArgs e) => ClearPrompt(session: string.Empty);

	private void HandleStateChange(object? sender, WebSocketState state) =>
		ConnectionStateChanged?.Invoke(state == WebSocketState.Open);

	/// <summary>
	/// The server rebound this reconnect to the still-live session — we are already authenticated,
	/// so we do not re-login. The character never left, so nothing is said about it.
	/// </summary>
	private void HandleReattached(object? sender, EventArgs e)
	{
		// A reload: the screen it had comes back first, then the frames it missed, so it reads as one.
		// The lines are already stored, so they are not kept a second time.
		if (wsService.ResumeSlot is { } slot)
		{
			foreach (var line in slot.TakeScrollback())
				AddLine(line, keep: false);
			// And the prompt it showed. A replayed prompt replaces it, and the server clears it when its
			// session ended while the page was away.
			if (slot.TakePrompt() is TerminalPrompt prompt)
				ShowPrompt(prompt, keep: false);
		}
	}

	/// <summary>
	/// The server started a fresh session instead of resuming the reloaded one (its grace ran out, it
	/// restarted, or it would not let the old session go on). The screen the page left is the same
	/// character's, so it comes back above a line saying the session started again, and is kept again for
	/// the next reload.
	/// </summary>
	private void HandleResumeRefused(object? sender, EventArgs e)
	{
		if (wsService.ResumeSlot is not { } slot) return;
		// The prompt belonged to the session that ended; the fresh one asks its own.
		_ = slot.TakePrompt();
		var earlier = slot.TakeScrollback();
		if (earlier.Count == 0) return;
		foreach (var line in earlier)
			AddLine(line);
		AddSystemLine("— Earlier lines, from before the reload. The session started again. —");
	}

	private void ShowPrompt(TerminalPrompt prompt, bool keep = true)
	{
		lock (_lines) _prompt = prompt;
		// Kept beside the scrollback, so a reload shows it again. Written on the page's timer, like the lines.
		if (keep && wsService.ResumeSlot is { } slot)
			_ = slot.KeepPromptAsync(prompt).AsTask();
		PromptChanged?.Invoke();
	}

	/// <summary>Empties the prompt when it is <paramref name="session"/>'s, or whatever is shown when that is empty.</summary>
	private void ClearPrompt(string session)
	{
		lock (_lines)
		{
			if (_prompt is null || (session.Length > 0 && _prompt.Session != session)) return;
			_prompt = null;
		}
		PromptCleared();
	}

	private void PromptCleared()
	{
		if (wsService.ResumeSlot is { } slot)
			_ = slot.KeepPromptAsync(null).AsTask();
		PromptChanged?.Invoke();
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

	private void AddLine(TerminalLine line, bool keep = true)
	{
		lock (_lines)
		{
			if (_lines.Count >= MaxLines)
				_lines.RemoveAt(0);
			_lines.Add(line);
		}
		// Kept for a reload (TerminalScrollback decides which lines, and what of them). The slot's
		// script call completes at once in the browser; a refused one only costs the stored screen.
		if (keep && wsService.ResumeSlot is { } slot)
			_ = slot.AppendLineAsync(line).AsTask();
		// And in the longer log, when the player keeps one. Its script queues the line and returns.
		if (keep && log is not null && Identity is { } identity)
			_ = log.AppendAsync(identity, line);
		LineReceived?.Invoke(line);
	}
}
