using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OneOf;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests;
using System.Diagnostics;
using A = MarkupString.MarkupStringModule;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// Integration tests for the @http command using postman-echo.com endpoints.
/// Tests all HTTP verbs (GET, POST, PUT, DELETE, PATCH) and encoding support.
///
/// Isolation strategy: each test generates a unique token (UUID hex) and embeds it
/// both in the HTTP request (as a query param or body field echoed by postman-echo)
/// and as a prefix in the callback attribute ("think {token} %0"), so every assertion
/// can key on a string that is guaranteed to belong to that test's own response.
/// </summary>
[NotInParallel]
public class PostmanEchoHttpTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private ISharpDatabase Database => WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	private Task<TestIsolationHelpers.TestPlayer> CreateTestPlayerAsync(string namePrefix) =>
		TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, namePrefix);

	private const string PostmanEchoBase = "https://postman-echo.com";
	private const int MaxWaitSeconds = 15;

	/// <summary>
	/// Generates a unique MUSH attribute name for a test (max 20 uppercase chars).
	/// </summary>
	private static string GenerateAttributeName(string prefix)
		=> $"{prefix.ToUpperInvariant()}{Guid.NewGuid():N}"[..20];

	/// <summary>
	/// Generates a short unique token suitable for use as a query parameter value or
	/// body field value that will be echoed back in postman-echo responses.
	/// </summary>
	private static string GenerateUniqueToken()
		=> Guid.NewGuid().ToString("N")[..16];

	/// <summary>
	/// Sets an attribute on the given player object for use as a callback by @http.
	/// By prefixing the output with <paramref name="uniqueToken"/>, every notification
	/// from this test contains a string that no other test can produce, ensuring
	/// assertions are fully scoped to this test's own HTTP response.
	/// </summary>
	private async Task SetCallbackAttribute(DBRef playerDbRef, string attributeName, string uniqueToken)
	{
		var player = (await Database.GetObjectNodeAsync(playerDbRef)).AsPlayer;
		await Database.SetAttributeAsync(
			player.Object.DBRef,
			[attributeName],
			A.single($"think {uniqueToken} %0"),
			player);
	}

	/// <summary>
	/// Sets an attribute on the given player object with custom MUSH code content.
	/// </summary>
	private async Task SetCallbackAttributeWithContent(DBRef playerDbRef, string attributeName, string mushCode)
	{
		var player = (await Database.GetObjectNodeAsync(playerDbRef)).AsPlayer;
		await Database.SetAttributeAsync(
			player.Object.DBRef,
			[attributeName],
			A.single(mushCode),
			player);
	}

	/// <summary>
	/// Polls until a matching <see cref="INotifyService.Notify"/> call is observed, or the
	/// <paramref name="timeout"/> elapses. This keeps individual test durations short on fast
	/// networks while still allowing generous headroom for slow or busy environments.
	/// </summary>
	private async Task WaitForNotify(
		Func<OneOf<MString, string>, bool> predicate,
		TimeSpan? timeout = null)
	{
		var deadline = Stopwatch.GetTimestamp()
			+ Stopwatch.Frequency * (long)(timeout?.TotalSeconds ?? MaxWaitSeconds);

		while (Stopwatch.GetTimestamp() < deadline)
		{
			var received = NotifyService.ReceivedCalls()
				.Any(call =>
				{
					var args = call.GetArguments();
					return args.Length >= 2
						&& args[1] is OneOf<MString, string> msg
						&& predicate(msg);
				});

			if (received)
				return;

			await Task.Delay(200);
		}
	}

	[Test]
	public async ValueTask HttpGet_ReturnsJsonWithEchoedUrl()
	{
		var testPlayer = await CreateTestPlayerAsync("HttpGet");
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));

		var token = GenerateUniqueToken();
		var attrName = GenerateAttributeName("HTTPGET");
		await SetCallbackAttribute(testPlayer.DbRef, attrName, token);

		await Parser.CommandParse(testPlayer.Handle, ConnectionService,
			MModule.single($"@http {testPlayer.DbRef}/{attrName}={PostmanEchoBase}/get?testid={token}"));

		await WaitForNotify(msg => TestHelpers.MessageContains(msg, token));

		await NotifyService
			.Received()
			.Notify(
				Arg.Any<AnySharpObject>(),
				Arg.Is<OneOf<MString, string>>(msg =>
					TestHelpers.MessageContains(msg, token) &&
					TestHelpers.MessageContains(msg, "postman-echo.com/get")));
	}

	[Test]
	public async ValueTask HttpPost_WithFormData_EchoesFormFields()
	{
		var testPlayer = await CreateTestPlayerAsync("HttpPost");
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));

		var token = GenerateUniqueToken();
		var attrName = GenerateAttributeName("HTTPPOST");
		await SetCallbackAttribute(testPlayer.DbRef, attrName, token);

		await Parser.CommandParse(testPlayer.Handle, ConnectionService,
			MModule.single($"@http/post {testPlayer.DbRef}/{attrName}={PostmanEchoBase}/post,testid={token}"));

		await WaitForNotify(msg => TestHelpers.MessageContains(msg, token));

		await NotifyService
			.Received()
			.Notify(
				Arg.Any<AnySharpObject>(),
				Arg.Is<OneOf<MString, string>>(msg =>
					TestHelpers.MessageContains(msg, token) &&
					TestHelpers.MessageContains(msg, "postman-echo.com/post")));
	}

	[Test]
	public async ValueTask HttpPut_WithBody_EchoesData()
	{
		var testPlayer = await CreateTestPlayerAsync("HttpPut");
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));

		var token = GenerateUniqueToken();
		var attrName = GenerateAttributeName("HTTPPUT");
		await SetCallbackAttribute(testPlayer.DbRef, attrName, token);

		await Parser.CommandParse(testPlayer.Handle, ConnectionService,
			MModule.single($"@http/put {testPlayer.DbRef}/{attrName}={PostmanEchoBase}/put,testid={token}"));

		await WaitForNotify(msg => TestHelpers.MessageContains(msg, token));

		await NotifyService
			.Received()
			.Notify(
				Arg.Any<AnySharpObject>(),
				Arg.Is<OneOf<MString, string>>(msg =>
					TestHelpers.MessageContains(msg, token) &&
					TestHelpers.MessageContains(msg, "postman-echo.com/put")));
	}

	[Test]
	public async ValueTask HttpDelete_ReturnsOkResponse()
	{
		var testPlayer = await CreateTestPlayerAsync("HttpDel");
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));

		var token = GenerateUniqueToken();
		var attrName = GenerateAttributeName("HTTPDEL");
		await SetCallbackAttribute(testPlayer.DbRef, attrName, token);

		await Parser.CommandParse(testPlayer.Handle, ConnectionService,
			MModule.single($"@http/delete {testPlayer.DbRef}/{attrName}={PostmanEchoBase}/delete?testid={token}"));

		await WaitForNotify(msg => TestHelpers.MessageContains(msg, token));

		await NotifyService
			.Received()
			.Notify(
				Arg.Any<AnySharpObject>(),
				Arg.Is<OneOf<MString, string>>(msg =>
					TestHelpers.MessageContains(msg, token) &&
					TestHelpers.MessageContains(msg, "postman-echo.com/delete")));
	}

	[Test]
	public async ValueTask HttpPatch_WithBody_EchoesData()
	{
		var testPlayer = await CreateTestPlayerAsync("HttpPatch");
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));

		var token = GenerateUniqueToken();
		var attrName = GenerateAttributeName("HTTPPATCH");
		await SetCallbackAttribute(testPlayer.DbRef, attrName, token);

		await Parser.CommandParse(testPlayer.Handle, ConnectionService,
			MModule.single($"@http/patch {testPlayer.DbRef}/{attrName}={PostmanEchoBase}/patch,testid={token}"));

		await WaitForNotify(msg => TestHelpers.MessageContains(msg, token));

		await NotifyService
			.Received()
			.Notify(
				Arg.Any<AnySharpObject>(),
				Arg.Is<OneOf<MString, string>>(msg =>
					TestHelpers.MessageContains(msg, token) &&
					TestHelpers.MessageContains(msg, "postman-echo.com/patch")));
	}

	[Test]
	public async ValueTask HttpGet_GzipEndpoint_DecompressesResponse()
	{
		var testPlayer = await CreateTestPlayerAsync("HttpGzip");
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));

		var token = GenerateUniqueToken();
		var attrName = GenerateAttributeName("HTTPGZIP");
		await SetCallbackAttribute(testPlayer.DbRef, attrName, token);

		await Parser.CommandParse(testPlayer.Handle, ConnectionService,
			MModule.single($"@http {testPlayer.DbRef}/{attrName}={PostmanEchoBase}/gzip"));

		await WaitForNotify(msg =>
			TestHelpers.MessageContains(msg, token) &&
			TestHelpers.MessageContains(msg, "gzipped"));

		await NotifyService
			.Received()
			.Notify(
				Arg.Any<AnySharpObject>(),
				Arg.Is<OneOf<MString, string>>(msg =>
					TestHelpers.MessageContains(msg, token) &&
					TestHelpers.MessageContains(msg, "gzipped")));
	}

	[Test]
	public async ValueTask HttpGet_DeflateEndpoint_DecompressesResponse()
	{
		var testPlayer = await CreateTestPlayerAsync("HttpDefl");
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));

		var token = GenerateUniqueToken();
		var attrName = GenerateAttributeName("HTTPDEFL");
		await SetCallbackAttribute(testPlayer.DbRef, attrName, token);

		await Parser.CommandParse(testPlayer.Handle, ConnectionService,
			MModule.single($"@http {testPlayer.DbRef}/{attrName}={PostmanEchoBase}/deflate"));

		await WaitForNotify(msg =>
			TestHelpers.MessageContains(msg, token) &&
			TestHelpers.MessageContains(msg, "deflated"));

		await NotifyService
			.Received()
			.Notify(
				Arg.Any<AnySharpObject>(),
				Arg.Is<OneOf<MString, string>>(msg =>
					TestHelpers.MessageContains(msg, token) &&
					TestHelpers.MessageContains(msg, "deflated")));
	}

	[Test]
	public async ValueTask HttpGet_WithQueryParams_EchoesArgsField()
	{
		var testPlayer = await CreateTestPlayerAsync("HttpQP");
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));

		var token = GenerateUniqueToken();
		var attrName = GenerateAttributeName("HTTPQP");
		await SetCallbackAttribute(testPlayer.DbRef, attrName, token);

		await Parser.CommandParse(testPlayer.Handle, ConnectionService,
			MModule.single($"@http {testPlayer.DbRef}/{attrName}={PostmanEchoBase}/get?{token}={token}"));

		await WaitForNotify(msg => TestHelpers.MessageContains(msg, token));

		await NotifyService
			.Received()
			.Notify(
				Arg.Any<AnySharpObject>(),
				Arg.Is<OneOf<MString, string>>(msg =>
					TestHelpers.MessageContains(msg, token)));
	}

	[Test]
	public async ValueTask HttpCommand_InvalidUrl_ReturnsErrorImmediately()
	{
		var testPlayer = await CreateTestPlayerAsync("HttpErr");
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));

		var token = GenerateUniqueToken();
		var attrName = GenerateAttributeName("HTTPERR");
		await SetCallbackAttribute(testPlayer.DbRef, attrName, token);

		var result = await Parser.CommandParse(testPlayer.Handle, ConnectionService,
			MModule.single($"@http {testPlayer.DbRef}/{attrName}=not-a-valid-url"));

		// Invalid URLs are rejected synchronously before the task is queued.
		await Assert.That(result.Message?.ToPlainText()).Contains("#-1");
	}

	[Test]
	public async ValueTask HttpCommand_GetWithBody_RejectsImmediately()
	{
		var testPlayer = await CreateTestPlayerAsync("HttpGErr");
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));

		var token = GenerateUniqueToken();
		var attrName = GenerateAttributeName("HTTPGERR");
		await SetCallbackAttribute(testPlayer.DbRef, attrName, token);

		await Parser.CommandParse(testPlayer.Handle, ConnectionService,
			MModule.single($"@http/get {testPlayer.DbRef}/{attrName}={PostmanEchoBase}/get,{token}"));

		// GET with a body is refused before the task is queued — error message is immediate.
		await NotifyService
			.Received()
			.Notify(
				Arg.Any<AnySharpObject>(),
				Arg.Is<OneOf<MString, string>>(msg =>
					TestHelpers.MessageContains(msg, "GET requests cannot have a body")));
	}

	[Test]
	public async ValueTask HttpGet_StatusRegister_Contains200()
	{
		var testPlayer = await CreateTestPlayerAsync("HttpStat");
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));

		var token = GenerateUniqueToken();
		var attrName = GenerateAttributeName("HTTPSTAT");

		await SetCallbackAttributeWithContent(testPlayer.DbRef, attrName, $"think {token} %q<STATUS>");

		await Parser.CommandParse(testPlayer.Handle, ConnectionService,
			MModule.single($"@http {testPlayer.DbRef}/{attrName}={PostmanEchoBase}/get?testid={token}"));

		await WaitForNotify(msg =>
			TestHelpers.MessageContains(msg, token) &&
			TestHelpers.MessageContains(msg, "200"));

		await NotifyService
			.Received()
			.Notify(
				Arg.Any<AnySharpObject>(),
				Arg.Is<OneOf<MString, string>>(msg =>
					TestHelpers.MessageContains(msg, token) &&
					TestHelpers.MessageContains(msg, "200")));
	}

	[Test]
	public async ValueTask HttpGet_StatusRegister_Contains404ForNotFoundEndpoint()
	{
		var testPlayer = await CreateTestPlayerAsync("HttpSt4");
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));

		var token = GenerateUniqueToken();
		var attrName = GenerateAttributeName("HTTPST4");

		await SetCallbackAttributeWithContent(testPlayer.DbRef, attrName, $"think {token} %q<STATUS>");

		await Parser.CommandParse(testPlayer.Handle, ConnectionService,
			MModule.single($"@http {testPlayer.DbRef}/{attrName}={PostmanEchoBase}/status/404"));

		await WaitForNotify(msg =>
			TestHelpers.MessageContains(msg, token) &&
			TestHelpers.MessageContains(msg, "404"));

		await NotifyService
			.Received()
			.Notify(
				Arg.Any<AnySharpObject>(),
				Arg.Is<OneOf<MString, string>>(msg =>
					TestHelpers.MessageContains(msg, token) &&
					TestHelpers.MessageContains(msg, "404")));
	}
}
