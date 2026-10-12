using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SharpMUSH.AnalysisProtocol;
using SharpMUSH.CodeAnalysis;

namespace SharpMUSH.Plugins.Lsp;

/// <summary>Small LSP 3.17 adapter with explicit receive-order snapshots and independent connection state.</summary>
public sealed class LspEndpoint(IAnalysisSessionFactory sessions, AnalysisExecutor execution, LspResources resources, IOptions<LspOptions> options, IHostApplicationLifetime lifetime)
{
	public async Task InvokeAsync(HttpContext context)
	{
		if (!context.WebSockets.IsWebSocketRequest || !context.WebSockets.WebSocketRequestedProtocols.Contains(LspFrames.Subprotocol))
		{
			context.Response.StatusCode = StatusCodes.Status400BadRequest;
			return;
		}
		IAnalysisSession identity;
		LspSessionDocuments documents;
		try { identity = sessions.CreateSession(context.User); documents = resources.OpenSession(identity.PrincipalKey); }
		catch (UnauthorizedAccessException) { context.Response.StatusCode = StatusCodes.Status403Forbidden; return; }
		catch (InvalidOperationException) { context.Response.StatusCode = StatusCodes.Status429TooManyRequests; return; }
		using (documents)
		using (var socket = await context.WebSockets.AcceptWebSocketAsync(LspFrames.Subprotocol))
		{
			await new Connection(socket, identity, execution, resources, documents, options.Value, context.RequestAborted, lifetime.ApplicationStopping).RunAsync();
		}
	}

	private sealed class Connection : IDisposable
	{
		private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
		private readonly WebSocket _socket;
		private readonly IAnalysisSession _identity;
		private readonly AnalysisExecutor _execution;
		private readonly LspResources _resources;
		private readonly LspSessionDocuments _documents;
		private readonly LspOptions _options;
		private readonly CancellationTokenSource _stop;
		private readonly CancellationTokenSource _idle = new();
		private readonly CancellationTokenSource _absolute = new();
		private readonly SemaphoreSlim _writes = new(1);
		private readonly ConcurrentDictionary<string, Work> _pending = new(StringComparer.Ordinal);
		private volatile bool _initialized;
		private bool _initializing;
		private bool _shutdown;
		private bool _snippets;
		private bool _hoverMarkdown;
		private bool _signatureMarkdown;
		private bool _diagnosticVersions;
		private sealed class Work(CancellationTokenSource cancellation) : IDisposable
		{
			private readonly object _gate = new();
			private bool _disposed;
			public CancellationToken Token { get; } = cancellation.Token;
			public Task Task { get; set; } = Task.CompletedTask;
			public void Cancel() { lock (_gate) { if (!_disposed) cancellation.Cancel(); } }
			public void Dispose() { lock (_gate) { if (_disposed) return; _disposed = true; cancellation.Dispose(); } }
		}

		public Connection(WebSocket socket, IAnalysisSession identity, AnalysisExecutor execution, LspResources resources, LspSessionDocuments documents, LspOptions options, CancellationToken disconnected, CancellationToken stopping)
		{
			_socket = socket; _identity = identity; _execution = execution; _resources = resources; _documents = documents; _options = options;
			_stop = CancellationTokenSource.CreateLinkedTokenSource(disconnected, stopping, _idle.Token, _absolute.Token);
			_idle.CancelAfter(options.IdleTimeout);
			_absolute.CancelAfter(options.SessionTimeout);
		}

		public async Task RunAsync()
		{
			try
			{
				while (!_stop.IsCancellationRequested)
				{
					var bytes = await LspFrames.ReadWebSocketAsync(_socket, _options.MaxMessageBytes, _stop.Token,
						_resources.AcquireReaderAsync, _options.MessageReadTimeout);
					if (bytes is null) break;
					_idle.CancelAfter(_options.IdleTimeout);
					using var message = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 64 });
					await DispatchAsync(message.RootElement);
				}
			}
			catch (Exception exception) when (exception is OperationCanceledException or WebSocketException or InvalidDataException or JsonException or ArgumentException or InvalidOperationException or KeyNotFoundException) { }
			finally
			{
				_stop.Cancel();
				foreach (var work in _pending.Values) work.Cancel();
				_documents.Dispose();
				try { await Task.WhenAll(_pending.Values.Select(work => work.Task)); }
				catch (Exception) { /* Observe failed sends; transport teardown must still release connection state. */ }
				if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
				{
					using var closing = new CancellationTokenSource(TimeSpan.FromSeconds(2));
					try { await _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "LSP session ended", closing.Token); }
					catch (Exception exception) when (exception is WebSocketException or OperationCanceledException) { }
				}
				Dispose();
			}
		}

		private async Task DispatchAsync(JsonElement message)
		{
			if (message.ValueKind != JsonValueKind.Object || !message.TryGetProperty("jsonrpc", out var version) || version.GetString() != "2.0" ||
				!message.TryGetProperty("method", out var methodValue) || methodValue.ValueKind != JsonValueKind.String)
				throw new InvalidDataException("Invalid LSP message.");
			var method = methodValue.GetString()!;
			JsonElement? id = message.TryGetProperty("id", out var identifier) ? identifier.Clone() : null;
			if (id is { } value && (value.ValueKind is not (JsonValueKind.Number or JsonValueKind.String) || value.GetRawText().Length > 128)) throw new InvalidDataException("Invalid request id.");
			var parameters = message.TryGetProperty("params", out var provided) ? provided : default;
			if (method == "exit" && id is null) { _stop.Cancel(); return; }
			if (!_initialized && method != "initialize") { if (id is not null) await Error(id.Value, -32002, "Server not initialized"); return; }
			if (_shutdown) { if (id is not null) await Error(id.Value, -32600, "Server shut down"); return; }
			if (method == "$/cancelRequest" && id is null)
			{
				if (parameters.TryGetProperty("id", out var cancelled) && _pending.TryGetValue(cancelled.GetRawText(), out var work)) work.Cancel();
				return;
			}
			if (method == "initialize")
			{
				if (id is null) return;
				if (_initializing || _initialized) { await Error(id.Value, -32600, "Already initialized"); return; }
				if (parameters.ValueKind != JsonValueKind.Object || !parameters.TryGetProperty("capabilities", out var capabilities) || capabilities.ValueKind != JsonValueKind.Object)
				{ await Error(id.Value, -32602, "Invalid initialize parameters"); return; }
				_initializing = true;
				_snippets = Capability(parameters, "capabilities", "textDocument", "completion", "completionItem", "snippetSupport");
				_diagnosticVersions = Capability(parameters, "capabilities", "textDocument", "publishDiagnostics", "versionSupport");
				_hoverMarkdown = Formats(parameters, "hover", "contentFormat");
				_signatureMarkdown = Formats(parameters, "signatureHelp", "signatureInformation", "documentationFormat");
				await StartAsync(id, null, null, async token =>
				{
					_ = await _identity.CreateAnalyzerAsync(token);
					_initialized = true;
					return new
					{
						capabilities = new
						{
							positionEncoding = "utf-16",
							textDocumentSync = new { openClose = true, change = 1 },
							hoverProvider = true,
							completionProvider = new { resolveProvider = false, triggerCharacters = new[] { "[", "@", "%" } },
							signatureHelpProvider = new { triggerCharacters = new[] { "(", "," } }
						},
						serverInfo = new { name = "SharpMUSH remote LSP", version = "1.0.0" }
					};
				});
				return;
			}
			if (method == "shutdown") { if (id is not null) { _shutdown = true; await Result(id.Value, null); } return; }
			if (id is null)
			{
				if (method is "initialized" or "$/setTrace" or "textDocument/didSave") return;
				if (method is not ("textDocument/didOpen" or "textDocument/didChange" or "textDocument/didClose")) return;
				var document = parameters.GetProperty("textDocument");
				var uri = document.GetProperty("uri").GetString()!;
				if (method == "textDocument/didClose") { _documents.Close(uri); await Diagnostics(uri, null, []); return; }
				if (method == "textDocument/didOpen") _documents.Open(uri, document.GetProperty("text").GetString()!, document.GetProperty("version").GetInt32());
				else
				{
					// Full sync: all full replacements are applied sequentially; range edits are not advertised.
					string? text = null;
					foreach (var change in parameters.GetProperty("contentChanges").EnumerateArray())
					{
						if (change.TryGetProperty("range", out _)) throw new InvalidDataException("Incremental sync is unsupported.");
						text = change.GetProperty("text").GetString()!;
					}
					if (text is null || !_documents.Change(uri, text, document.GetProperty("version").GetInt32())) return;
				}
				var snapshot = _documents.Get(uri)!;
				await StartAsync(null, uri, snapshot, async token =>
				{
					var analyzer = await _identity.CreateAnalyzerAsync(token);
					return analyzer.Validate(snapshot.Text, MushParseMode.ForFileName(new Uri(uri).AbsolutePath));
				});
				return;
			}
			if (method is not ("textDocument/hover" or "textDocument/completion" or "textDocument/signatureHelp")) { await Error(id.Value, -32601, "Unknown method"); return; }
			try
			{
				var uri = parameters.GetProperty("textDocument").GetProperty("uri").GetString()!;
				// Snapshot in receive order. Later changes do not cancel or invalidate this request result.
				var snapshot = _documents.Get(uri);
				if (snapshot is null) { await Result(id.Value, null); return; }
				var position = parameters.GetProperty("position");
				var line = position.GetProperty("line").GetInt32();
				var character = position.GetProperty("character").GetInt32();
				var cursor = Cursor(snapshot.Text, line, character);
				await StartAsync(id, uri, snapshot, async token =>
				{
					var analyzer = await _identity.CreateAnalyzerAsync(token);
					if (method == "textDocument/hover")
					{
						var hover = analyzer.Hover(snapshot.Text, line, cursor);
						return hover is null ? null : new { contents = new { kind = _hoverMarkdown ? "markdown" : "plaintext", value = Markup(hover.Markdown, _hoverMarkdown) }, range = hover.Range };
					}
					if (method == "textDocument/completion")
					{
						var values = analyzer.Complete(snapshot.Text, line, cursor);
						return new
						{
							isIncomplete = values.Count > 4096,
							items = values.Take(4096).Select(item => new
							{
								label = item.Label,
								kind = item.Kind == "Function" ? 3 : item.Kind == "Keyword" ? 14 : 6,
								detail = item.Detail,
								documentation = Markup(item.Documentation ?? "", false),
								insertText = Insertion(item),
								insertTextFormat = _snippets && item.IsSnippet ? 2 : 1,
								textEdit = new { range = WordRange(snapshot.Text, line, cursor), newText = Insertion(item) }
							}).ToArray()
						};
					}
					var signature = analyzer.SignatureHelp(snapshot.Text, line, cursor);
					return signature is null ? null : new
					{
						signatures = new[] { new { label = signature.Label, documentation = new { kind = _signatureMarkdown ? "markdown" : "plaintext", value = Markup(signature.Documentation, _signatureMarkdown) },
						parameters = signature.Parameters.Select(parameter => new { label = parameter.Label, documentation = parameter.Documentation }).ToArray() } },
						activeSignature = 0,
						activeParameter = Math.Max(0, Math.Min(signature.ActiveParameter, signature.Parameters.Count - 1))
					};
				});
			}
			catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or KeyNotFoundException or JsonException) { await Error(id.Value, -32602, "Invalid request parameters"); }
		}

		private async Task StartAsync(JsonElement? id, string? uri, LspDocument? snapshot, Func<CancellationToken, Task<object?>> operation)
		{
			var key = id?.GetRawText() ?? "notification/" + Guid.NewGuid().ToString("N");
			if (_pending.Count >= _options.MaxPendingRequests)
			{
				if (id is not null) await Error(id.Value, -32000, "Session work capacity exceeded");
				else _stop.Cancel(); // Never silently accept a document update whose diagnostics cannot be serviced.
				return;
			}
			var work = new Work(CancellationTokenSource.CreateLinkedTokenSource(_stop.Token));
			if (!_pending.TryAdd(key, work)) { work.Dispose(); throw new InvalidDataException("Duplicate active request id."); }
			work.Task = ProcessAsync(key, work, id, uri, snapshot, operation);
		}

		private async Task ProcessAsync(string key, Work work, JsonElement? id, string? uri, LspDocument? snapshot, Func<CancellationToken, Task<object?>> operation)
		{
			try
			{
				try
				{
					var result = await _execution.ExecuteAsync(() => operation(SharpMUSH.Library.ParserInterfaces.ExecutionBudget.CurrentToken), work.Token);
					if (id is not null) await Result(id.Value, result);
					else if (uri is not null && snapshot is not null && _documents.Get(uri)?.Generation == snapshot.Generation)
						await Diagnostics(uri, snapshot.Version, ((IReadOnlyList<SharpMUSH.Library.Models.Diagnostic>)result!).Take(1000).Select(diagnostic => new
						{
							range = diagnostic.Range,
							severity = diagnostic.Severity.ToString() == "Error" ? 1 : diagnostic.Severity.ToString() == "Warning" ? 2 : diagnostic.Severity.ToString() == "Hint" ? 4 : 3,
							message = diagnostic.Message.Length > 1024 ? diagnostic.Message[..1024] + "..." : diagnostic.Message,
							source = diagnostic.Source,
							code = diagnostic.Code
						}).ToArray(), () => _documents.Get(uri)?.Generation == snapshot.Generation);
				}
				catch (OperationCanceledException) when (!_stop.IsCancellationRequested)
				{
					if (id is not null) await Error(id.Value, -32800, "Request cancelled");
					else if (uri is not null && snapshot is not null)
						await Diagnostics(uri, snapshot.Version, [new
						{
							range = new { start = new { line = 0, character = 0 }, end = new { line = 0, character = 0 } },
							severity = 2, message = "Analysis timed out; diagnostics are unavailable for this version. Edit the document to retry.",
							source = "SharpMUSH", code = "analysis-timeout"
						}], () => _documents.Get(uri)?.Generation == snapshot.Generation);
				}
				catch (OperationCanceledException) { }
				catch (UnauthorizedAccessException) { if (id is not null) await Error(id.Value, -32001, "Analysis identity is unavailable"); _stop.Cancel(); }
				catch (Exception exception) when (exception is InvalidOperationException or ArgumentException) { if (id is not null) await Error(id.Value, -32000, "Analysis unavailable or capacity exceeded"); else _stop.Cancel(); }
				catch (Exception) { if (id is not null && !_stop.IsCancellationRequested) await Error(id.Value, -32603, "Analysis failed"); else _stop.Cancel(); }
			}
			catch (Exception) { _stop.Cancel(); } // Observe failed error replies before releasing work.
			finally { _pending.TryRemove(key, out _); work.Dispose(); }
		}

		private Task Result(JsonElement id, object? result) => Send(new { jsonrpc = "2.0", id, result });
		private Task Error(JsonElement id, int code, string message) => Send(new { jsonrpc = "2.0", id, error = new { code, message } });
		private Task Diagnostics(string uri, int? version, object[] diagnostics, Func<bool>? current = null)
		{
			var parameters = new Dictionary<string, object?> { ["uri"] = uri, ["diagnostics"] = diagnostics };
			if (_diagnosticVersions && version is not null) parameters["version"] = version;
			return Send(new { jsonrpc = "2.0", method = "textDocument/publishDiagnostics", @params = parameters }, current);
		}
		private async Task Send(object message, Func<bool>? current = null)
		{
			var bytes = JsonSerializer.SerializeToUtf8Bytes(message, Json);
			if (bytes.Length > _options.MaxResponseBytes) throw new InvalidDataException("LSP response exceeds limit.");
			using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
			timeout.CancelAfter(_options.MessageReadTimeout);
			await _writes.WaitAsync(timeout.Token);
			try { if (current is null || current()) await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, timeout.Token); }
			finally { _writes.Release(); }
		}
		private static bool Capability(JsonElement element, params string[] path)
		{
			foreach (var part in path) if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(part, out element)) return false;
			return element.ValueKind == JsonValueKind.True;
		}
		private static bool Formats(JsonElement root, params string[] path)
		{
			if (!root.TryGetProperty("capabilities", out var element) || !element.TryGetProperty("textDocument", out element)) return false;
			foreach (var part in path) if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(part, out element)) return false;
			return element.ValueKind == JsonValueKind.Array && element.EnumerateArray().Any(value => value.ValueKind == JsonValueKind.String && value.GetString() == "markdown");
		}
		private static string Markup(string value, bool markdown) => markdown ? value : value.Replace("**", "").Replace("`", "");
		private string Insertion(CompletionSuggestion item) => _snippets || !item.IsSnippet ? (item.InsertText ?? item.Label) : Regex.Replace(Regex.Replace(item.InsertText ?? item.Label, @"\$\{\d+:([^}]*)\}", "$1"), @"\$\d+|\$\{\d+\}", "");
		private static string Line(string text, int line)
		{
			if (line < 0) throw new ArgumentException("Invalid line.");
			var start = 0;
			for (var i = 0; i < line; i++) { var end = text.IndexOf('\n', start); if (end < 0) throw new ArgumentException("Invalid line."); start = end + 1; }
			var last = text.IndexOf('\n', start);
			return text[start..(last < 0 ? text.Length : last)].TrimEnd('\r');
		}
		private static int Cursor(string text, int line, int character) => character < 0 ? throw new ArgumentException("Invalid character.") : Math.Min(character, Line(text, line).Length);
		private static object WordRange(string text, int line, int character)
		{
			var value = Line(text, line); var start = character; var end = character;
			static bool Word(char c) => char.IsLetterOrDigit(c) || c is '_' or '@' or '%' or '#';
			while (start > 0 && Word(value[start - 1])) start--;
			while (end < value.Length && Word(value[end])) end++;
			return new { start = new { line, character = start }, end = new { line, character = end } };
		}
		public void Dispose() { _stop.Dispose(); _idle.Dispose(); _absolute.Dispose(); _writes.Dispose(); }
	}
}
