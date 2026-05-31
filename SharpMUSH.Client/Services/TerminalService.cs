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

	// Pending correlated request: (request-id, completion source, accumulated lines)
	private (string ReqId, TaskCompletionSource<string[]> Tcs, List<string> Buffer)? _pending;
	private readonly SemaphoreSlim _sendSemaphore = new(1, 1);

	public event Action<TerminalLine>? LineReceived;
	public event Action<bool>? ConnectionStateChanged;

	public bool IsConnected => wsService.IsConnected;

	public IReadOnlyList<TerminalLine> Lines
	{
		get { lock (_lines) return _lines.AsReadOnly(); }
	}

	public async Task ConnectAsync(string serverUri)
	{
		wsService.MessageReceived -= HandleMessage;
		wsService.ConnectionStateChanged -= HandleStateChange;
		wsService.MessageReceived += HandleMessage;
		wsService.ConnectionStateChanged += HandleStateChange;

		_logger.LogInformation("Connecting to {ServerUri}", serverUri);
		await wsService.ConnectAsync(serverUri);
		AddSystemLine($"Connected to {serverUri}");
	}

	public async Task DisconnectAsync()
	{
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

	public async Task<string[]> SendCommandAsync(string command, int timeoutMs = 5000)
	{
		await _sendSemaphore.WaitAsync();
		try
		{
			var reqId = Guid.NewGuid().ToString("N")[..8];
			var tcs = new TaskCompletionSource<string[]>(TaskCreationOptions.RunContinuationsAsynchronously);
			_pending = (reqId, tcs, []);

			await wsService.SendAsync(command);
			await wsService.SendAsync($"@pemit me={EndMarkerPrefix}{reqId}");

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
