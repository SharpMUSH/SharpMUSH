using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Globalization;
using System.Runtime.Loader;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SharpMUSH.Implementation.Services;
using SharpMUSH.Library.Plugins;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharpMUSH.AnalysisProtocol;
using SharpMUSH.CodeAnalysis;
using SharpMUSH.CodeAnalysis.Catalog;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Plugins.Lsp;
using SharpMUSH.Server.Authentication;
using SharpMUSH.Server.Registration;
using SharpMUSH.Tests.Integration.Mcp;

namespace SharpMUSH.Tests.Integration.AnalysisTransport;

public class LspIsolatedTransportTests
{
	internal static async Task<WebApplication> StartApp(Action<LspOptions>? configure = null, IAnalysisSessionFactory? sessions = null, string availability = "enabled", AnalysisExecutionOptions? executionOptions = null)
	{
		var builder = WebApplication.CreateBuilder();
		builder.WebHost.UseTestServer();
		builder.Logging.ClearProviders();
		var settings = new LspOptions { Enabled = availability != "config-false" };
		configure?.Invoke(settings);
		builder.Configuration.AddInMemoryCollection(typeof(LspOptions).GetProperties().ToDictionary(property => "Lsp:" + property.Name,
			property => Convert.ToString(property.GetValue(settings), CultureInfo.InvariantCulture)));
		var fixture = Path.Combine(Path.GetTempPath(), "sharpmush-lsp-payload-" + Guid.NewGuid().ToString("N"));
		var builtIn = Path.Combine(fixture, "shipped");
		var installed = Path.Combine(fixture, "installed");
		Directory.CreateDirectory(builtIn);
		if (availability != "absent")
		{
			var payload = Environment.GetEnvironmentVariable("SHARPMUSH_LSP_TEST_PAYLOAD") ?? Path.Combine(AppContext.BaseDirectory, "plugins", "lsp");
			foreach (var source in Directory.GetFiles(payload, "*", SearchOption.AllDirectories))
			{
				var target = Path.Combine(builtIn, "lsp", Path.GetRelativePath(payload, source));
				Directory.CreateDirectory(Path.GetDirectoryName(target)!);
				File.Copy(source, target);
			}
		}
		if (availability == "disabled") PluginState.Empty.With("lsp", false).Write(Path.Combine(installed, PluginState.FileName));
		var catalog = PluginCatalog.Build(builder.Services, new PluginDirectories(builtIn, installed), NullLogger.Instance);
		if (availability is "enabled" or "config-false")
		{
			if (catalog.EndpointContributors.Count != 1) throw new InvalidOperationException("LSP plugin failed: " + string.Join(";", catalog.Entries.Select(entry => entry.Reason)));
			var assembly = catalog.Plugins.Single().GetType().Assembly;
			var endpoint = assembly.GetType(typeof(LspEndpoint).FullName!)!;
			if (endpoint.GetConstructors().Single().GetParameters()[0].ParameterType != typeof(IAnalysisSessionFactory)) throw new InvalidOperationException("Session type identity is duplicated.");
			var protocol = AssemblyLoadContext.GetLoadContext(assembly)!.LoadFromAssemblyName(typeof(LspFrames).Assembly.GetName());
			if (AssemblyLoadContext.GetLoadContext(protocol) != AssemblyLoadContext.GetLoadContext(assembly)) throw new InvalidOperationException("Protocol dependency is not private.");
		}
		builder.Services.AddSingleton<IAnalysisSessionFactory>(sessions ?? new FixedAnalysisSessions(AnalysisCatalogAnalyzer.Create(BuiltInAnalysisCatalog.CreateSnapshot(), SharpMUSHOptions.Default())));
		builder.Services.AddSingleton(new AnalysisExecutor(Options.Create(executionOptions ?? new AnalysisExecutionOptions())));
		builder.Services.AddAuthentication("DebugAuth")
			.AddScheme<AuthenticationSchemeOptions, McpIsolatedTransportTests.FixtureAuthentication>("DebugAuth", _ => { })
			.AddScheme<AuthenticationSchemeOptions, McpIsolatedTransportTests.FixtureAuthentication>(MushBasicAuthenticationHandler.SchemeName, _ => { });
		builder.Services.AddAnalysisTransportPolicy(builder.Configuration);
		builder.Services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationPolicyProvider, PermissionPolicyProvider>();
		builder.Services.AddRateLimiter(options => options.AddPolicy("mcp", _ => System.Threading.RateLimiting.RateLimitPartition.GetNoLimiter("fixture")));
		builder.Services.AddSingleton(_ => new FixtureDirectory(fixture));
		var app = builder.Build();
		app.Services.GetRequiredService<FixtureDirectory>();
		app.UseWebSockets();
		app.UseAuthentication();
		app.UseRateLimiter();
		app.UseAuthorization();
		foreach (var contributor in catalog.EndpointContributors) contributor.MapEndpoints(app);
		app.MapAnalysisTransportFallback(app.Configuration);
		app.MapFallback(() => Microsoft.AspNetCore.Http.Results.Content("<html>portal</html>", "text/html"));
		await app.StartAsync();
		return app;
	}

	internal static async Task<WebSocket> Connect(WebApplication app, string actor = "2")
	{
		var client = app.GetTestServer().CreateWebSocketClient();
		client.SubProtocols.Add(LspFrames.Subprotocol);
		client.ConfigureRequest = request => request.Headers["X-Fixture-Character"] = actor;
		return await client.ConnectAsync(new Uri("wss://localhost/lsp"), CancellationToken.None);
	}

	internal static async Task Send(WebSocket socket, object message)
		=> await socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(message), WebSocketMessageType.Text, true, CancellationToken.None);
	internal static async Task<JsonElement> Receive(WebSocket socket)
	{
		using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
		using var json = JsonDocument.Parse((await LspFrames.ReadWebSocketAsync(socket, LspFrames.DefaultMaximumBytes, cancellation.Token))!);
		return json.RootElement.Clone();
	}
	internal static async Task<JsonElement> Response(WebSocket socket, int id)
	{
		while (true)
		{
			var message = await Receive(socket);
			if (message.TryGetProperty("id", out var responseId) && responseId.GetInt32() == id) return message;
		}
	}
	internal static async Task Initialize(WebSocket socket)
	{
		await Send(socket, new { jsonrpc = "2.0", id = 1, method = "initialize", @params = new { capabilities = new { } } });
		var response = await Response(socket, 1);
		if (!response.TryGetProperty("result", out _)) throw new InvalidOperationException(response.ToString());
		await Send(socket, new { jsonrpc = "2.0", method = "initialized", @params = new { } });
	}

	[Test]
	public async Task CloseDuringDiagnosticProjection_SuppressesOldPublicationAfterClear()
	{
		using var analyzer = new GatedAnalyzer { GateProjection = true };
		await using var app = await StartApp(sessions: new FixedAnalysisSessions(analyzer));
		using var socket = await Connect(app); await Initialize(socket);
		const string uri = "file:///projection.fun";
		await Send(socket, new { jsonrpc = "2.0", method = "textDocument/didOpen", @params = new { textDocument = new { uri, version = 1, text = "slow[" } } });
		await analyzer.Entered(TimeSpan.FromSeconds(3));
		try
		{
			await Send(socket, new { jsonrpc = "2.0", method = "textDocument/didClose", @params = new { textDocument = new { uri } } });
			await Assert.That((await Receive(socket)).GetProperty("params").GetProperty("diagnostics").GetArrayLength()).IsEqualTo(0);
			analyzer.Release.Set();
			await Send(socket, new { jsonrpc = "2.0", id = 2, method = "unknown" });
			await Assert.That((await Receive(socket)).GetProperty("id").GetInt32()).IsEqualTo(2);
			await Task.Delay(50);
			await Send(socket, new { jsonrpc = "2.0", id = 3, method = "unknown" });
			await Assert.That((await Receive(socket)).GetProperty("id").GetInt32()).IsEqualTo(3);
		}
		finally { analyzer.Release.Set(); }
	}

	[Test]
	public async Task CapacityRepliesAndCancellationRaces_KeepSessionUsable()
	{
		using var analyzer = new GatedAnalyzer();
		await using var app = await StartApp(options => options.MaxPendingRequests = 1, new FixedAnalysisSessions(analyzer));
		using var socket = await Connect(app); await Initialize(socket);
		const string uri = "file:///overload.fun";
		await Send(socket, new { jsonrpc = "2.0", method = "textDocument/didOpen", @params = new { textDocument = new { uri, version = 1, text = "get" } } });
		await Receive(socket);
		await Send(socket, new { jsonrpc = "2.0", id = 2, method = "textDocument/completion", @params = new { textDocument = new { uri }, position = new { line = 0, character = 3 } } });
		await analyzer.Entered(TimeSpan.FromSeconds(3));
		try
		{
			for (var id = 3; id < 35; id++) await Send(socket, new { jsonrpc = "2.0", id, method = "textDocument/hover", @params = new { textDocument = new { uri }, position = new { line = 0, character = 2 } } });
			for (var id = 3; id < 35; id++) await Assert.That((await Response(socket, id)).GetProperty("error").GetProperty("code").GetInt32()).IsEqualTo(-32000);
			await Send(socket, new { jsonrpc = "2.0", method = "$/cancelRequest", @params = new { id = 2 } });
			await Assert.That((await Response(socket, 2)).GetProperty("error").GetProperty("code").GetInt32()).IsEqualTo(-32800);
			analyzer.Release.Set();
			for (var id = 40; id < 140; id++)
			{
				await Send(socket, new { jsonrpc = "2.0", id, method = "textDocument/hover", @params = new { textDocument = new { uri }, position = new { line = 0, character = 2 } } });
				await Send(socket, new { jsonrpc = "2.0", method = "$/cancelRequest", @params = new { id } });
				// Nothing else is outstanding once the previous reply has arrived, so the session has room.
				var answer = await Response(socket, id);
				await Assert.That(answer.TryGetProperty("error", out var error) ? error.GetProperty("code").GetInt32() : 0).IsNotEqualTo(-32000);
				await Send(socket, new { jsonrpc = "2.0", method = "$/cancelRequest", @params = new { id } });
			}
			await Send(socket, new { jsonrpc = "2.0", id = 200, method = "unknown" });
			await Assert.That((await Response(socket, 200)).GetProperty("error").GetProperty("code").GetInt32()).IsEqualTo(-32601);
		}
		finally { analyzer.Release.Set(); }
	}

	[Test]
	public async Task DiagnosticsDeadline_PublishesWarningAndKeepsSessionUsable()
	{
		using var analyzer = new GatedAnalyzer { GateValidation = true };
		await using var app = await StartApp(sessions: new FixedAnalysisSessions(analyzer), executionOptions: new AnalysisExecutionOptions { AnalysisTimeout = TimeSpan.FromMilliseconds(200) });
		using var socket = await Connect(app); await Initialize(socket);
		const string uri = "file:///deadline.fun";
		await Send(socket, new { jsonrpc = "2.0", method = "textDocument/didOpen", @params = new { textDocument = new { uri, version = 1, text = "slow[" } } });
		var diagnostic = (await Receive(socket)).GetProperty("params").GetProperty("diagnostics")[0];
		await Assert.That(diagnostic.GetProperty("code").GetString()).IsEqualTo("analysis-timeout");
		await Assert.That(diagnostic.GetProperty("severity").GetInt32()).IsEqualTo(2);
		await Send(socket, new { jsonrpc = "2.0", id = 2, method = "unknown" });
		await Assert.That((await Response(socket, 2)).GetProperty("error").GetProperty("code").GetInt32()).IsEqualTo(-32601);
		await Send(socket, new { jsonrpc = "2.0", method = "textDocument/didChange", @params = new { textDocument = new { uri, version = 2 }, contentChanges = new[] { new { text = "get(#1/DESC)" } } } });
		await Assert.That((await Receive(socket)).GetProperty("params").GetProperty("diagnostics").GetArrayLength()).IsEqualTo(0);
	}

	[Test]
	public async Task QuietSessions_DoNotConsumeMessageReadersOrMessageDeadline()
	{
		await using var app = await StartApp(options => { options.MessageReadTimeout = TimeSpan.FromMilliseconds(250); options.IdleTimeout = TimeSpan.FromSeconds(3); });
		using var first = await Connect(app); using var second = await Connect(app); using var third = await Connect(app, "3");
		await Initialize(first); await Initialize(second);
		await Initialize(third).WaitAsync(TimeSpan.FromSeconds(1));
		await Task.Delay(400);
		await Send(first, new { jsonrpc = "2.0", id = 2, method = "unknown" });
		await Assert.That((await Response(first, 2)).GetProperty("error").GetProperty("code").GetInt32()).IsEqualTo(-32601);
	}

	[Test]
	public async Task SyncCapacityFailure_ClosesSessionRatherThanSilentlyLosingLatestText()
	{
		using var analyzer = new GatedAnalyzer { GateValidation = true };
		await using var app = await StartApp(options => options.MaxPendingRequests = 1, new FixedAnalysisSessions(analyzer));
		using var socket = await Connect(app); await Initialize(socket);
		const string uri = "file:///pressure.fun";
		await Send(socket, new { jsonrpc = "2.0", method = "textDocument/didOpen", @params = new { textDocument = new { uri, version = 1, text = "slow[" } } });
		await analyzer.Entered(TimeSpan.FromSeconds(3));
		try
		{
			await Send(socket, new { jsonrpc = "2.0", method = "textDocument/didChange", @params = new { textDocument = new { uri, version = 2 }, contentChanges = new[] { new { text = "get" } } } });
			using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
			await Assert.That(await LspFrames.ReadWebSocketAsync(socket, 1024, deadline.Token)).IsNull();
		}
		finally { analyzer.Release.Set(); }
	}

	[Test]
	[Arguments("mush", "think [get(#1/DESC)]", 10)]
	[Arguments("mu", "think [get(#1/DESC)]", 10)]
	[Arguments("mushfn", "get(#1/DESC)", 2)]
	[Arguments("fun", "get(#1/DESC)", 2)]
	[Arguments("mushcmd", "think [get(#1/DESC)]", 10)]
	public async Task Features_AllFiveExtensionsHaveDiagnosticsHoverCompletionAndSignatures(string extension, string text, int character)
	{
		await using var app = await StartApp();
		using var socket = await Connect(app);
		await Initialize(socket);
		var uri = "file:///example." + extension;
		await Send(socket, new { jsonrpc = "2.0", method = "textDocument/didOpen", @params = new { textDocument = new { uri, languageId = "mush", version = 1, text } } });
		var diagnostics = await Receive(socket);
		await Assert.That(diagnostics.GetProperty("method").GetString()).IsEqualTo("textDocument/publishDiagnostics");
		await Assert.That(diagnostics.GetProperty("params").GetProperty("diagnostics").GetArrayLength()).IsEqualTo(0);
		await Send(socket, new { jsonrpc = "2.0", id = 2, method = "textDocument/hover", @params = new { textDocument = new { uri }, position = new { line = 0, character } } });
		await Assert.That((await Response(socket, 2)).GetProperty("result").ValueKind).IsEqualTo(JsonValueKind.Object);
		await Send(socket, new { jsonrpc = "2.0", id = 3, method = "textDocument/completion", @params = new { textDocument = new { uri }, position = new { line = 0, character } } });
		await Assert.That((await Response(socket, 3)).GetProperty("result").GetProperty("items").GetArrayLength() > 0).IsTrue();
		await Send(socket, new { jsonrpc = "2.0", id = 4, method = "textDocument/signatureHelp", @params = new { textDocument = new { uri }, position = new { line = 0, character = text.IndexOf('(') + 1 } } });
		await Assert.That((await Response(socket, 4)).GetProperty("result").ValueKind).IsEqualTo(JsonValueKind.Object);
		await Send(socket, new { jsonrpc = "2.0", method = "textDocument/didChange", @params = new { textDocument = new { uri, version = 2 }, contentChanges = new[] { new { text = text + "[" } } } });
		var broken = await Receive(socket);
		await Assert.That(broken.GetProperty("params").GetProperty("diagnostics").GetArrayLength() > 0).IsTrue();
		await Send(socket, new { jsonrpc = "2.0", method = "textDocument/didClose", @params = new { textDocument = new { uri } } });
		await Assert.That((await Receive(socket)).GetProperty("params").GetProperty("diagnostics").GetArrayLength()).IsEqualTo(0);
	}

	[Test]
	public async Task ReceiveOrder_RequestUsesSnapshotAndIsNotCancelledByLaterChange()
	{
		using var analyzer = new GatedAnalyzer();
		await using var app = await StartApp(sessions: new FixedAnalysisSessions(analyzer));
		using var socket = await Connect(app);
		await Initialize(socket);
		const string uri = "file:///snapshot.fun";
		await Send(socket, new { jsonrpc = "2.0", method = "textDocument/didOpen", @params = new { textDocument = new { uri, languageId = "mush", version = 1, text = "get" } } });
		await Receive(socket);
		await Send(socket, new { jsonrpc = "2.0", method = "textDocument/didChange", @params = new { textDocument = new { uri, version = 2 }, contentChanges = new[] { new { text = "get" } } } });
		await Send(socket, new { jsonrpc = "2.0", id = 2, method = "textDocument/completion", @params = new { textDocument = new { uri }, position = new { line = 0, character = 3 } } });
		await analyzer.Entered(TimeSpan.FromSeconds(3));
		await Send(socket, new { jsonrpc = "2.0", method = "textDocument/didChange", @params = new { textDocument = new { uri, version = 3 }, contentChanges = new[] { new { text = "nomatchzz" } } } });
		analyzer.Release.Set();
		var response = await Response(socket, 2);
		await Assert.That(response.GetProperty("result").GetProperty("items").EnumerateArray().Any(item => item.GetProperty("label").GetString() == "get")).IsTrue();
	}

	[Test]
	public async Task Cancellation_ReturnsOneResponseAndCompletedUnknownIdsAreHarmless()
	{
		using var analyzer = new GatedAnalyzer();
		await using var app = await StartApp(sessions: new FixedAnalysisSessions(analyzer));
		using var socket = await Connect(app);
		await Initialize(socket);
		const string uri = "file:///cancel.fun";
		await Send(socket, new { jsonrpc = "2.0", method = "textDocument/didOpen", @params = new { textDocument = new { uri, languageId = "mush", version = 1, text = "get" } } });
		await Receive(socket);
		await Send(socket, new { jsonrpc = "2.0", id = 2, method = "textDocument/completion", @params = new { textDocument = new { uri }, position = new { line = 0, character = 3 } } });
		await analyzer.Entered(TimeSpan.FromSeconds(3));
		await Send(socket, new { jsonrpc = "2.0", method = "$/cancelRequest", @params = new { id = 2 } });
		await Assert.That((await Response(socket, 2)).GetProperty("error").GetProperty("code").GetInt32()).IsEqualTo(-32800);
		await Send(socket, new { jsonrpc = "2.0", method = "$/cancelRequest", @params = new { id = 2 } });
		await Send(socket, new { jsonrpc = "2.0", method = "$/cancelRequest", @params = new { id = 999 } });
		await Send(socket, new { jsonrpc = "2.0", id = 3, method = "unknown" });
		var next = await Receive(socket);
		await Assert.That(next.GetProperty("id").GetInt32()).IsEqualTo(3);
		await Assert.That(next.GetProperty("error").GetProperty("code").GetInt32()).IsEqualTo(-32601);
	}

	[Test]
	public async Task CloseReopen_ReusedVersionCannotReceivePreviousGenerationsDiagnostics()
	{
		using var analyzer = new GatedAnalyzer { GateValidation = true };
		await using var app = await StartApp(sessions: new FixedAnalysisSessions(analyzer));
		using var socket = await Connect(app);
		await Initialize(socket);
		const string uri = "file:///reopen.fun";
		await Send(socket, new { jsonrpc = "2.0", method = "textDocument/didOpen", @params = new { textDocument = new { uri, languageId = "mush", version = 1, text = "slow[" } } });
		await analyzer.Entered(TimeSpan.FromSeconds(3));
		await Send(socket, new { jsonrpc = "2.0", method = "textDocument/didClose", @params = new { textDocument = new { uri } } });
		await Send(socket, new { jsonrpc = "2.0", method = "textDocument/didOpen", @params = new { textDocument = new { uri, languageId = "mush", version = 1, text = "get(#1/DESC)" } } });
		await Assert.That((await Receive(socket)).GetProperty("params").GetProperty("diagnostics").GetArrayLength()).IsEqualTo(0);
		await Assert.That((await Receive(socket)).GetProperty("params").GetProperty("diagnostics").GetArrayLength()).IsEqualTo(0);
		analyzer.Release.Set();
		await Send(socket, new { jsonrpc = "2.0", id = 3, method = "textDocument/hover", @params = new { textDocument = new { uri }, position = new { line = 0, character = 2 } } });
		while (true)
		{
			var response = await Receive(socket);
			if (response.TryGetProperty("id", out _)) break;
			await Assert.That(response.GetProperty("params").GetProperty("diagnostics").GetArrayLength()).IsEqualTo(0);
		}
	}

	[Test]
	public async Task Utf16PositionsAndPlainClientCapabilities_AreHonored()
	{
		await using var app = await StartApp();
		using var socket = await Connect(app);
		await Initialize(socket);
		const string uri = "file:///unicode.fun";
		await Send(socket, new { jsonrpc = "2.0", method = "textDocument/didOpen", @params = new { textDocument = new { uri, languageId = "mush", version = 1, text = "😀 get(#1/DESC)\r\n" } } });
		await Receive(socket);
		await Send(socket, new { jsonrpc = "2.0", id = 2, method = "textDocument/hover", @params = new { textDocument = new { uri }, position = new { line = 0, character = 4 } } });
		var hover = (await Response(socket, 2)).GetProperty("result");
		await Assert.That(hover.GetProperty("range").GetProperty("start").GetProperty("character").GetInt32()).IsEqualTo(3);
		await Assert.That(hover.GetProperty("contents").GetProperty("kind").GetString()).IsEqualTo("plaintext");
		await Send(socket, new { jsonrpc = "2.0", method = "textDocument/didChange", @params = new { textDocument = new { uri, version = 2 }, contentChanges = new[] { new { text = "x" }, new { text = "ge" } } } });
		await Receive(socket);
		await Send(socket, new { jsonrpc = "2.0", id = 3, method = "textDocument/completion", @params = new { textDocument = new { uri }, position = new { line = 0, character = 9999 } } });
		var completion = (await Response(socket, 3)).GetProperty("result").GetProperty("items").EnumerateArray().First(item => item.GetProperty("label").GetString() == "get");
		await Assert.That(completion.GetProperty("insertTextFormat").GetInt32()).IsEqualTo(1);
		await Assert.That(completion.GetProperty("insertText").GetString()!.Contains("${")).IsFalse();
		await Assert.That(completion.GetProperty("textEdit").GetProperty("range").GetProperty("end").GetProperty("character").GetInt32()).IsEqualTo(2);
	}

	[Test]
	public async Task MalformedInitialize_ReturnsInvalidParamsAndNotificationsGetNoResponses()
	{
		await using var app = await StartApp();
		using var socket = await Connect(app);
		await Send(socket, new { jsonrpc = "2.0", id = 8, method = "initialize" });
		await Assert.That((await Response(socket, 8)).GetProperty("error").GetProperty("code").GetInt32()).IsEqualTo(-32602);
		await Initialize(socket);
		await Send(socket, new { jsonrpc = "2.0", id = 9, method = "initialize", @params = new { capabilities = new { } } });
		await Assert.That((await Response(socket, 9)).GetProperty("error").GetProperty("code").GetInt32()).IsEqualTo(-32600);
		await Send(socket, new { jsonrpc = "2.0", method = "unknownNotification", @params = new { } });
		await Send(socket, new { jsonrpc = "2.0", id = "string-id", method = "unknownMethod" });
		var response = await Receive(socket);
		await Assert.That(response.GetProperty("id").GetString()).IsEqualTo("string-id");
		await Assert.That(response.GetProperty("error").GetProperty("code").GetInt32()).IsEqualTo(-32601);
	}

	[Test]
	[Arguments("absent")]
	[Arguments("disabled")]
	[Arguments("config-false")]
	public async Task OptionalPlugin_UnavailableIs404DespitePortalFallback(string availability)
	{
		await using var app = await StartApp(availability: availability);
		using var http = app.GetTestClient();
		using var response = await http.GetAsync("/lsp");
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
		await Assert.That(response.Content.Headers.ContentType?.MediaType == "text/html").IsFalse();
	}

	[Test]
	public async Task Sessions_SamePrincipalAndUriRemainIndependentOverWire()
	{
		await using var app = await StartApp();
		using var first = await Connect(app, "2");
		using var second = await Connect(app, "2");
		await Initialize(first); await Initialize(second);
		const string uri = "file:///same.fun";
		await Send(first, new { jsonrpc = "2.0", method = "textDocument/didOpen", @params = new { textDocument = new { uri, version = 1, languageId = "mush", text = "get(#1/DESC)" } } });
		await Send(second, new { jsonrpc = "2.0", method = "textDocument/didOpen", @params = new { textDocument = new { uri, version = 1, languageId = "mush", text = "zzunknown" } } });
		await Receive(first); await Receive(second);
		foreach (var socket in new[] { first, second }) await Send(socket, new { jsonrpc = "2.0", id = 2, method = "textDocument/hover", @params = new { textDocument = new { uri }, position = new { line = 0, character = 2 } } });
		await Assert.That((await Response(first, 2)).GetProperty("result").ValueKind).IsEqualTo(JsonValueKind.Object);
		await Assert.That((await Response(second, 2)).GetProperty("result").ValueKind).IsEqualTo(JsonValueKind.Null);
	}

	[Test]
	public async Task HostileOriginAndSessionCapacity_AreRejectedBeforeUpgrade()
	{
		await using var app = await StartApp(options => options.MaxSessionsPerPrincipal = 1);
		using var first = await Connect(app);
		await Initialize(first);
		using var http = app.GetTestClient();
		using var request = new HttpRequestMessage(HttpMethod.Get, "/lsp");
		request.Headers.Add("X-Fixture-Character", "2");
		request.Headers.TryAddWithoutValidation("Origin", "https://untrusted.invalid");
		using var response = await http.SendAsync(request);
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
		await Assert.That(async () => { using var second = await Connect(app); }).Throws<InvalidOperationException>();
	}

	[Test]
	public async Task OversizeMessageAndIdleTimeout_CloseSessionWithoutAffectingHost()
	{
		await using var app = await StartApp(options => options.MaxMessageBytes = 1024);
		using var oversized = await Connect(app);
		await Initialize(oversized);
		await oversized.SendAsync(new byte[2048], WebSocketMessageType.Text, true, CancellationToken.None);
		using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(3));
		await Assert.That(await LspFrames.ReadWebSocketAsync(oversized, 1024, cancellation.Token)).IsNull();
		// The idle session sends nothing: a request would race the short idle window.
		await using var idleApp = await StartApp(options => options.IdleTimeout = TimeSpan.FromMilliseconds(150));
		using var idle = await Connect(idleApp);
		using var idleCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(3));
		await Assert.That(await LspFrames.ReadWebSocketAsync(idle, 1024, idleCancellation.Token)).IsNull();
		foreach (var host in new[] { app, idleApp })
		{
			using var http = host.GetTestClient();
			using var response = await http.GetAsync("/unrelated");
			await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		}
	}

	[Test]
	public async Task AuthAndDisabledState_AreCheckedBeforeUpgrade()
	{
		await using var app = await StartApp();
		using var http = app.GetTestClient();
		using var denied = await http.GetAsync("/lsp");
		await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
		await using var disabled = await StartApp(options => options.Enabled = false);
		using var other = disabled.GetTestClient();
		using var unavailable = await other.GetAsync("/lsp");
		await Assert.That(unavailable.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
	}

	[Test]
	public async Task Lifecycle_PreInitializeErrorShutdownReplyAndIndependentSessions()
	{
		await using var app = await StartApp();
		using var first = await Connect(app);
		await Send(first, new { jsonrpc = "2.0", id = 2, method = "textDocument/hover", @params = new { } });
		await Assert.That((await Response(first, 2)).GetProperty("error").GetProperty("code").GetInt32()).IsEqualTo(-32002);
		await Initialize(first);
		using var second = await Connect(app);
		await Initialize(second);
		await Send(first, new { jsonrpc = "2.0", id = 3, method = "shutdown" });
		await Assert.That((await Response(first, 3)).GetProperty("result").ValueKind).IsEqualTo(JsonValueKind.Null);
		await Send(first, new { jsonrpc = "2.0", id = 4, method = "unknown" });
		await Assert.That((await Response(first, 4)).GetProperty("error").GetProperty("code").GetInt32()).IsEqualTo(-32600);
		await Send(first, new { jsonrpc = "2.0", method = "exit" });
		await Send(second, new { jsonrpc = "2.0", id = 5, method = "unknown" });
		await Assert.That((await Response(second, 5)).GetProperty("error").GetProperty("code").GetInt32()).IsEqualTo(-32601);
	}
}
