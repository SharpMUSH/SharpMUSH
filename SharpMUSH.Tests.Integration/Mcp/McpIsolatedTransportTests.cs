using System.Net;
using System.Globalization;
using System.Runtime.Loader;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using System.Threading.RateLimiting;
using SharpMUSH.Implementation.Services;
using SharpMUSH.Library.Plugins;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Server.Registration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using NSubstitute;
using SharpMUSH.CodeAnalysis;
using SharpMUSH.CodeAnalysis.Catalog;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests.Integration.AnalysisTransport;
using SharpMUSH.Server.Authentication;
using SharpMUSH.Server.Mcp;

namespace SharpMUSH.Tests.Integration.Mcp;

/// <summary>Real HTTP MCP dispatch with test-only server identities; no game or password setup.</summary>
public class McpIsolatedTransportTests
{
	internal sealed class FixtureAuthentication(
		IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
		: AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
	{
		protected override Task<AuthenticateResult> HandleAuthenticateAsync()
		{
			var actor = Request.Headers["X-Fixture-Character"];
			if (actor.Count == 0 && Scheme.Name != "DebugAuth")
				return Task.FromResult(AuthenticateResult.NoResult());
			var identity = new ClaimsIdentity([
				new Claim("character_key", actor.Count == 0 ? "1" : actor.ToString()), new Claim("character_creation_time", "100")], Scheme.Name);
			return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
		}
	}

	private static async Task<McpClient> Connect(HttpClient http, string actor, string path = "/mcp", string? protocolVersion = null)
		=> await McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
		{
			Endpoint = new Uri("https://localhost" + path), TransportMode = HttpTransportMode.StreamableHttp,
			AdditionalHeaders = new Dictionary<string, string> { ["X-Fixture-Character"] = actor }
		}, http), new McpClientOptions { ProtocolVersion = protocolVersion });

	private static async Task<WebApplication> StartApp(Action<McpOptions>? configure = null, IMushCodeAnalyzer? analyzer = null, string state = "enabled", int? permits = null, string? allowedOrigin = null, IAnalysisSessionFactory? sessions = null)
	{
		var builder = WebApplication.CreateBuilder();
		builder.WebHost.UseTestServer();
		builder.Logging.ClearProviders();
		if (allowedOrigin is not null) builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["AnalysisTransports:AllowedOrigins:0"] = allowedOrigin });
		var settings = new McpOptions { Enabled = state != "config-false" };
		configure?.Invoke(settings);
		builder.Configuration.AddInMemoryCollection(typeof(McpOptions).GetProperties().ToDictionary(
			property => "Mcp:" + property.Name, property => Convert.ToString(property.GetValue(settings), CultureInfo.InvariantCulture)));
		var fixture = Path.Combine(Path.GetTempPath(), "sharpmush-mcp-plugin-" + Guid.NewGuid().ToString("N"));
		var builtIn = Path.Combine(fixture, "shipped");
		var installed = Path.Combine(fixture, "installed");
		Directory.CreateDirectory(builtIn);
		if (state != "absent")
		{
			var payload = Environment.GetEnvironmentVariable("SHARPMUSH_MCP_TEST_PAYLOAD") ?? Path.Combine(AppContext.BaseDirectory, "plugins", "mcp");
			foreach (var source in Directory.GetFiles(payload, "*", SearchOption.AllDirectories))
			{
				var target = Path.Combine(builtIn, "mcp", Path.GetRelativePath(payload, source));
				Directory.CreateDirectory(Path.GetDirectoryName(target)!);
				File.Copy(source, target);
			}
		}
		if (state == "disabled") PluginState.Empty.With("mcp", false).Write(Path.Combine(installed, PluginState.FileName));
		var catalog = PluginCatalog.Build(builder.Services, new PluginDirectories(builtIn, installed), NullLogger.Instance);
		if (state is "absent" or "disabled" && catalog.Plugins.Count != 0)
			throw new InvalidOperationException("Unavailable plugin unexpectedly loaded.");
		if (state is "enabled" or "config-false")
		{
			if (catalog.Plugins.Count != 1 || catalog.EndpointContributors.Count != 1)
				throw new InvalidOperationException("MCP plugin did not load/register: " + string.Join("; ", catalog.Entries.Select(entry => entry.Reason)));
			var pluginAssembly = catalog.Plugins.Single().GetType().Assembly;
			var tools = pluginAssembly.GetType(typeof(MushTools).FullName!)!;
			var sdkAssembly = tools.GetCustomAttributesData().Single(attribute => attribute.AttributeType.Name == "McpServerToolTypeAttribute").AttributeType.Assembly;
			if (AssemblyLoadContext.GetLoadContext(sdkAssembly) != AssemblyLoadContext.GetLoadContext(pluginAssembly) ||
				AssemblyLoadContext.GetLoadContext(sdkAssembly) == AssemblyLoadContext.Default)
				throw new InvalidOperationException("MCP SDK was not loaded from the plugin's private context.");
			if (tools.GetConstructors().Single().GetParameters()[0].ParameterType != typeof(IAnalysisSessionFactory))
				throw new InvalidOperationException("Analyzer contract type identity was duplicated by plugin loading.");
		}
		builder.Services.AddSingleton(analyzer ?? Substitute.For<IMushCodeAnalyzer>());
		builder.Services.AddOptions<AnalysisExecutionOptions>().BindConfiguration("Mcp").BindConfiguration("Analysis");
		builder.Services.AddSingleton(provider => new AnalysisExecutor(provider.GetRequiredService<IOptions<AnalysisExecutionOptions>>(), provider.GetRequiredService<Microsoft.Extensions.Hosting.IHostApplicationLifetime>().ApplicationStopping));
		builder.Services.AddSingleton<IAnalysisSessionFactory>(sessions ?? new FixedAnalysisSessions(analyzer ?? Substitute.For<IMushCodeAnalyzer>()));
		builder.Services.AddAuthentication("DebugAuth")
			.AddScheme<AuthenticationSchemeOptions, FixtureAuthentication>("DebugAuth", _ => { })
			.AddScheme<AuthenticationSchemeOptions, FixtureAuthentication>(
			MushBasicAuthenticationHandler.SchemeName, _ => { });
		builder.Services.AddAnalysisTransportPolicy(builder.Configuration);
		builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
		builder.Services.AddRateLimiter(options =>
		{
			options.RejectionStatusCode = 429;
			options.AddPolicy(AnalysisTransportSecurity.RateLimitPolicy, _ => permits is { } limit
				? RateLimitPartition.GetFixedWindowLimiter("fixture", _ => new FixedWindowRateLimiterOptions { PermitLimit = limit, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })
				: RateLimitPartition.GetNoLimiter("fixture"));
		});
		builder.Services.AddSingleton(_ => new FixtureDirectory(fixture));
		var app = builder.Build();
		app.Services.GetRequiredService<FixtureDirectory>();
		app.UseAuthentication();
		app.UseRateLimiter();
		app.UseAuthorization();
		foreach (var contributor in catalog.EndpointContributors) contributor.MapEndpoints(app);
		app.MapAnalysisTransportFallback(app.Configuration);
		app.MapFallback(() => Results.Content("<html>fixture portal</html>", "text/html"));
		await app.StartAsync();
		return app;
	}

	[Test]
	public async Task EnabledStatelessEndpoint_AuthenticatedGetReturnsMethodNotAllowed()
	{
		await using var app = await StartApp();
		using var http = app.GetTestClient();
		using var request = new HttpRequestMessage(HttpMethod.Get, "/mcp");
		request.Headers.Add("X-Fixture-Character", "1");
		using var response = await http.SendAsync(request);
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.MethodNotAllowed);
	}

	[Test]
	[Arguments(null, "https://untrusted.invalid", false)]
	[Arguments("https://trusted.invalid", "https://untrusted.invalid", false)]
	[Arguments("https://trusted.invalid", "null", false)]
	[Arguments("https://trusted.invalid", "https://trusted.invalid", true)]
	public async Task Origin_RequiresExplicitHostAllowlist(string? allowed, string origin, bool succeeds)
	{
		await using var app = await StartApp(allowedOrigin: allowed);
		using var http = app.GetTestClient();
		await using var client = await Connect(http, "1");
		using var request = Request(new StringContent("{\"jsonrpc\":\"2.0\",\"id\":998,\"method\":\"tools/list\"}"));
		request.Headers.TryAddWithoutValidation("Origin", origin);
		using var response = await http.SendAsync(request);
		await Assert.That(response.StatusCode).IsEqualTo(succeeds ? HttpStatusCode.OK : HttpStatusCode.Forbidden);
	}

	[Test]
	public async Task Intelligence_UsesPrincipalFilteredCatalogAndRefreshesRestrictions()
	{
		var functions = new FunctionLibraryService();
		functions.Add("publicfn", (new FunctionDefinition(new SharpFunctionAttribute { Name = "publicfn", Flags = FunctionFlags.Regular }, _ => throw new NotSupportedException()), true));
		functions.Add("godfn", (new FunctionDefinition(new SharpFunctionAttribute { Name = "godfn", Flags = FunctionFlags.GodOnly }, _ => throw new NotSupportedException()), true));
		var users = new UserDefinedFunctionService();
		var catalog = new LiveAnalysisCatalog(functions, new CommandLibraryService(), users, Substitute.For<ILockService>());
		await using var app = await StartApp(sessions: new CatalogAnalysisSessions(catalog));
		using var http = app.GetTestClient();
		await using var ordinary = await Connect(http, "2");
		await using var god = await Connect(http, "1");
		async Task<string> Names(McpClient client)
		{
			var result = await client.CallToolAsync("complete", new Dictionary<string, object?> { ["line"] = 0, ["character"] = 0, ["code"] = "" });
			return string.Join("", result.Content.OfType<TextContentBlock>().Select(block => block.Text));
		}
		await Assert.That((await Names(ordinary)).Contains("publicfn")).IsTrue();
		await Assert.That((await Names(ordinary)).Contains("godfn")).IsFalse();
		await Assert.That((await Names(god)).Contains("godfn")).IsTrue();
		users.SetBuiltinRestriction("publicfn", "nobody");
		await Assert.That((await Names(ordinary)).Contains("publicfn")).IsFalse();
	}

	[Test]
	[Arguments("2025-06-18")]
	[Arguments("2025-11-25")]
	[Arguments("2026-07-28")]
	public async Task SupportedRevisions_AreNegotiatedBySdkWithoutMixedLifecycle(string version)
	{
		await using var app = await StartApp();
		using var http = app.GetTestClient();
		await using var client = await Connect(http, "1", protocolVersion: version);
		await Assert.That(client.NegotiatedProtocolVersion).IsEqualTo(version);
		await Assert.That((await client.ListToolsAsync()).Count).IsEqualTo(8);
	}

	[Test]
	public async Task November2025_NotificationHasEmpty202AndUnsupportedVersionIs400()
	{
		await using var app = await StartApp();
		using var http = app.GetTestClient();
		await using var client = await Connect(http, "1", protocolVersion: "2025-11-25");
		using var notification = Request(new StringContent("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}"));
		notification.Headers.Remove("MCP-Protocol-Version");
		notification.Headers.TryAddWithoutValidation("MCP-Protocol-Version", "2025-11-25");
		using var accepted = await http.SendAsync(notification);
		await Assert.That(accepted.StatusCode).IsEqualTo(HttpStatusCode.Accepted);
		await Assert.That((await accepted.Content.ReadAsByteArrayAsync()).Length).IsEqualTo(0);
		using var invalid = Request(new StringContent("{\"jsonrpc\":\"2.0\",\"id\":998,\"method\":\"tools/list\"}"));
		invalid.Headers.Remove("MCP-Protocol-Version");
		invalid.Headers.TryAddWithoutValidation("MCP-Protocol-Version", "2099-01-01");
		using var rejected = await http.SendAsync(invalid);
		await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
	}

	[Test]
	public async Task FailedAuthentication_StillConsumesIpRateLimit()
	{
		await using var app = await StartApp(permits: 1);
		using var http = app.GetTestClient();
		using var first = Request(new StringContent("{}"), authenticated: false);
		using var denied = await http.SendAsync(first);
		await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
		using var second = Request(new StringContent("{}"), authenticated: false);
		using var limited = await http.SendAsync(second);
		await Assert.That(limited.StatusCode).IsEqualTo(HttpStatusCode.TooManyRequests);
	}

	[Test]
	public async Task HostPolicy_RejectsAuthenticatedDebugDefault_AndCustomPathStillWorks()
	{
		await using var app = await StartApp(options => options.Path = "/private/code/mcp");
		using var http = app.GetTestClient();
		using var request = new HttpRequestMessage(HttpMethod.Post, "/private/code/mcp") { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
		using var denied = await http.SendAsync(request);
		await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
		await using var client = await Connect(http, "1", "/private/code/mcp");
		var tools = await client.ListToolsAsync();
		await Assert.That(tools.Count).IsEqualTo(8);
	}

	[Test]
	public async Task PackagedPlugin_HasRuntimeDependencyClosureAndNoSharedContractCopies()
	{
		var payload = Environment.GetEnvironmentVariable("SHARPMUSH_MCP_TEST_PAYLOAD") ?? Path.Combine(AppContext.BaseDirectory, "plugins", "mcp");
		var host = Directory.GetParent(Directory.GetParent(payload)!.FullName)!.FullName;
		await Assert.That(File.Exists(Path.Combine(payload, "SharpMUSH.CodeAnalysis.dll"))).IsFalse();
		await Assert.That(File.Exists(Path.Combine(payload, "SharpMUSH.Library.dll"))).IsFalse();
		using var deps = JsonDocument.Parse(File.ReadAllText(Path.Combine(payload, "SharpMUSH.Plugins.Mcp.deps.json")));
		foreach (var target in deps.RootElement.GetProperty("targets").EnumerateObject())
			foreach (var library in target.Value.EnumerateObject())
			{
				if (library.Value.TryGetProperty("runtime", out var assets))
					foreach (var asset in assets.EnumerateObject())
					{
						var name = Path.GetFileName(asset.Name);
						var owner = name.StartsWith("SharpMUSH.") && name != "SharpMUSH.Plugins.Mcp.dll" ? host : payload;
						if (!File.Exists(Path.Combine(owner, name))) throw new FileNotFoundException("Missing declared runtime asset: " + asset.Name, Path.Combine(owner, name));
					}
				if (library.Value.TryGetProperty("runtimeTargets", out var targets))
					foreach (var asset in targets.EnumerateObject())
						await Assert.That(File.Exists(Path.Combine(payload, asset.Name))).IsTrue();
			}
		var serverDeps = Path.Combine(host, "SharpMUSH.Server.deps.json");
		using var server = JsonDocument.Parse(File.ReadAllText(serverDeps));
		await Assert.That(server.RootElement.GetProperty("libraries").EnumerateObject().Any(library =>
			library.Name.StartsWith("ModelContextProtocol") || library.Name.StartsWith("SharpMUSH.Plugins.Mcp/"))).IsFalse();
	}

	[Test]
	[Arguments("absent")]
	[Arguments("disabled")]
	[Arguments("config-false")]
	public async Task OptionalPlugin_DoesNotExposeAnEndpointWhenUnavailable(string state)
	{
		await using var app = await StartApp(analyzer: McpAnalysisExecutorTests.OfflineAnalyzer(), state: state);
		await Assert.That(app.Services.GetRequiredService<IMushCodeAnalyzer>().Validate("add(1,2)").Count).IsEqualTo(0);
		using var http = app.GetTestClient();
		foreach (var method in new[] { HttpMethod.Get, HttpMethod.Post })
		{
			using var request = new HttpRequestMessage(method, "/mcp") { Content = new StringContent("{}") };
			using var response = await http.SendAsync(request);
			await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
			await Assert.That(response.Content.Headers.ContentType?.MediaType == "text/html").IsFalse();
		}
	}

	[Test]
	public async Task RealMcpDispatch_PreservesRequestIdentityAcrossToolCalls()
	{
		await using var app = await StartApp();
		using var http = app.GetTestClient();
		await using var first = await Connect(http, "1");
		await using var second = await Connect(http, "2");
		var opened = await first.CallToolAsync("open_document", new Dictionary<string, object?> { ["code"] = "private" });
		await Assert.That(opened.IsError == true).IsFalse();
		var id = opened.Content.OfType<TextContentBlock>().Single().Text;
		var foreign = await second.CallToolAsync("validate", new Dictionary<string, object?> { ["documentId"] = id });
		await Assert.That(foreign.IsError == true).IsTrue();
		var close = await second.CallToolAsync("close_document", new Dictionary<string, object?> { ["documentId"] = id });
		await Assert.That(close.Content.OfType<TextContentBlock>().Single().Text).IsEqualTo("false");
		var own = await first.CallToolAsync("validate", new Dictionary<string, object?> { ["documentId"] = id });
		await Assert.That(own.IsError == true).IsFalse();
		await app.StopAsync();
	}
	private sealed class UnknownLengthContent(byte[] bytes) : HttpContent
	{
		protected override bool TryComputeLength(out long length) { length = 0; return false; }
		protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
			=> stream.WriteAsync(bytes).AsTask();
	}

	private static HttpRequestMessage Request(HttpContent content, bool authenticated = true)
	{
		content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
		var request = new HttpRequestMessage(HttpMethod.Post, "https://localhost/mcp") { Content = content };
		request.Headers.Accept.ParseAdd("application/json");
		request.Headers.Accept.ParseAdd("text/event-stream");
		request.Headers.TryAddWithoutValidation("MCP-Protocol-Version", "2025-06-18");
		if (authenticated) request.Headers.Add("X-Fixture-Character", "1");
		return request;
	}

	[Test]
	public async Task HttpOversize_Is413ForDeclaredAndUnknownLengthBodies_AndUnauthenticatedIs401()
	{
		await using var app = await StartApp(options => options.MaxRequestBodyBytes = 1024);
		using var http = app.GetTestClient();
		foreach (var content in new HttpContent[] { new StringContent(new string('x', 2048)), new UnknownLengthContent(new byte[2048]) })
		{
			using var request = Request(content);
			using var response = await http.SendAsync(request).WaitAsync(TimeSpan.FromSeconds(5));
			await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.RequestEntityTooLarge);
		}
		using var unauthenticated = Request(new StringContent(new string('x', 2048)), authenticated: false);
		using var denied = await http.SendAsync(unauthenticated);
		await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
	}

	[Test]
	public async Task HttpFiveMiBEscapedDocument_IsAcceptedWithJsonEnvelopeOverhead()
	{
		await using var app = await StartApp();
		using var http = app.GetTestClient();
		await using var client = await Connect(http, "1");
		const int bytes = 5 * 1024 * 1024;
		var escaped = string.Create(bytes * 6, 0, static (buffer, _) =>
		{
			for (var i = 0; i < buffer.Length; i += 6) "\\u0078".AsSpan().CopyTo(buffer[i..]);
		});
		var payload = "{\"jsonrpc\":\"2.0\",\"id\":123,\"method\":\"tools/call\",\"params\":{\"name\":\"open_document\",\"arguments\":{\"code\":\"" + escaped + "\"}}}";
		using var request = Request(new StringContent(payload, Encoding.UTF8));
		using var response = await http.SendAsync(request).WaitAsync(TimeSpan.FromSeconds(15));
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var body = await response.Content.ReadAsStringAsync();
		using var json = JsonDocument.Parse(body.TrimStart().StartsWith('{') ? body : body.Split('\n').First(line => line.StartsWith("data:"))[5..].Trim());
		await Assert.That(json.RootElement.TryGetProperty("error", out _)).IsFalse();
		var result = json.RootElement.GetProperty("result");
		await Assert.That(result.TryGetProperty("isError", out var error) && error.GetBoolean()).IsFalse();
		var id = result.GetProperty("content")[0].GetProperty("text").GetString();
		var closed = await client.CallToolAsync("close_document", new Dictionary<string, object?> { ["documentId"] = id });
		await Assert.That(closed.Content.OfType<TextContentBlock>().Single().Text).IsEqualTo("true");
	}

	[Test]
	public async Task ToolSchemas_KeepEightNamesAndDoNotExposeCancellationOrIdentityArguments()
	{
		await using var app = await StartApp();
		using var http = app.GetTestClient();
		await using var client = await Connect(http, "1");
		var tools = await client.ListToolsAsync();
		var expected = new Dictionary<string, string[]>
		{
			["validate"] = ["code", "parseType", "documentId"], ["format"] = ["code", "documentId"],
			["hover"] = ["line", "character", "code", "documentId"], ["complete"] = ["line", "character", "code", "documentId"],
			["signature_help"] = ["line", "character", "code", "documentId"], ["document_symbols"] = ["code", "documentId"],
			["open_document"] = ["code"], ["close_document"] = ["documentId"]
		};
		await Assert.That(tools.Select(tool => tool.Name).Order().SequenceEqual(expected.Keys.Order())).IsTrue();
		foreach (var tool in tools)
			await Assert.That(tool.JsonSchema.GetProperty("properties").EnumerateObject().Select(property => property.Name).Order()
				.SequenceEqual(expected[tool.Name].Order())).IsTrue();
	}

	[Test]
	public async Task HttpDeadline_StopsActualMultiLineSyntaxAndNextRequestStillWorks()
	{
		var analyzer = McpAnalysisExecutorTests.OfflineAnalyzer();
		analyzer.Validate("add(1,2)");
		await using var app = await StartApp(options => options.AnalysisTimeout = TimeSpan.FromMilliseconds(100), analyzer);
		using var http = app.GetTestClient();
		await using var client = await Connect(http, "1");
		var code = string.Concat(Enumerable.Repeat("think x\n", 5 * 1024 * 1024 / 8));
		try
		{
			var timed = await client.CallToolAsync("validate", new Dictionary<string, object?> { ["code"] = code, ["parseType"] = "commandsperline" })
				.AsTask().WaitAsync(TimeSpan.FromSeconds(5));
			await Assert.That(timed.IsError == true).IsTrue();
		}
		catch (ModelContextProtocol.McpException) { }
		var next = await client.CallToolAsync("validate", new Dictionary<string, object?> { ["code"] = "add(1,2)" });
		await Assert.That(next.IsError == true).IsFalse();
	}

}
