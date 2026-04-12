using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OneOf;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
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
	/// Sets an attribute on the #1 player object for use as a callback by @http.
	/// By prefixing the output with <paramref name="uniqueToken"/>, every notification
	/// from this test contains a string that no other test can produce, ensuring
	/// assertions are fully scoped to this test's own HTTP response.
	/// </summary>
	private async Task SetCallbackAttribute(string attributeName, string uniqueToken)
	{
		var playerOne = (await Database.GetObjectNodeAsync(new DBRef(1))).AsPlayer;
		await Database.SetAttributeAsync(
			playerOne.Object.DBRef,
			[attributeName],
			A.single($"think {uniqueToken} %0"),
			playerOne);
	}

	/// <summary>
	/// Sets an attribute on the #1 player object with custom MUSH code content.
	/// </summary>
	private async Task SetCallbackAttributeWithContent(string attributeName, string mushCode)
	{
		var playerOne = (await Database.GetObjectNodeAsync(new DBRef(1))).AsPlayer;
		await Database.SetAttributeAsync(
			playerOne.Object.DBRef,
			[attributeName],
			A.single(mushCode),
			playerOne);
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
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var token = GenerateUniqueToken();
		var attrName = GenerateAttributeName("HTTPGET");
		await SetCallbackAttribute(attrName, token);

		// postman-echo echoes the request URL in the response, which includes the unique token.
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@http #1/{attrName}={PostmanEchoBase}/get?testid={token}"));

		await WaitForNotify(msg => TestHelpers.MessageContains(msg, token));

		await NotifyService
			.Received()
			.Notify(
				Arg.Any<AnySharpObject>(),
				Arg.Is<OneOf<MString, string>>(msg =>
					TestHelpers.MessageContains(msg, token) &&
					TestHelpers.MessageContains(msg, "postman-echo.com/get")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask HttpPost_WithFormData_EchoesFormFields()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var token = GenerateUniqueToken();
		var attrName = GenerateAttributeName("HTTPPOST");
		await SetCallbackAttribute(attrName, token);

		// postman-echo echoes the form body; the unique token appears in the "form" JSON field.
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@http/post #1/{attrName}={PostmanEchoBase}/post,testid={token}"));

		await WaitForNotify(msg => TestHelpers.MessageContains(msg, token));

		await NotifyService
			.Received()
			.Notify(
				Arg.Any<AnySharpObject>(),
				Arg.Is<OneOf<MString, string>>(msg =>
					TestHelpers.MessageContains(msg, token) &&
					TestHelpers.MessageContains(msg, "postman-echo.com/post")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask HttpPut_WithBody_EchoesData()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var token = GenerateUniqueToken();
		var attrName = GenerateAttributeName("HTTPPUT");
		await SetCallbackAttribute(attrName, token);

		// postman-echo echoes the PUT body; the unique token appears in the "form" JSON field.
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@http/put #1/{attrName}={PostmanEchoBase}/put,testid={token}"));

		await WaitForNotify(msg => TestHelpers.MessageContains(msg, token));

		await NotifyService
			.Received()
			.Notify(
				Arg.Any<AnySharpObject>(),
				Arg.Is<OneOf<MString, string>>(msg =>
					TestHelpers.MessageContains(msg, token) &&
					TestHelpers.MessageContains(msg, "postman-echo.com/put")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask HttpDelete_ReturnsOkResponse()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var token = GenerateUniqueToken();
		var attrName = GenerateAttributeName("HTTPDEL");
		await SetCallbackAttribute(attrName, token);

		// postman-echo echoes the request URL; the unique token appears in the query args.
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@http/delete #1/{attrName}={PostmanEchoBase}/delete?testid={token}"));

		await WaitForNotify(msg => TestHelpers.MessageContains(msg, token));

		await NotifyService
			.Received()
			.Notify(
				Arg.Any<AnySharpObject>(),
				Arg.Is<OneOf<MString, string>>(msg =>
					TestHelpers.MessageContains(msg, token) &&
					TestHelpers.MessageContains(msg, "postman-echo.com/delete")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask HttpPatch_WithBody_EchoesData()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var token = GenerateUniqueToken();
		var attrName = GenerateAttributeName("HTTPPATCH");
		await SetCallbackAttribute(attrName, token);

		// postman-echo echoes the PATCH body; the unique token appears in the "form" JSON field.
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@http/patch #1/{attrName}={PostmanEchoBase}/patch,testid={token}"));

		await WaitForNotify(msg => TestHelpers.MessageContains(msg, token));

		await NotifyService
			.Received()
			.Notify(
				Arg.Any<AnySharpObject>(),
				Arg.Is<OneOf<MString, string>>(msg =>
					TestHelpers.MessageContains(msg, token) &&
					TestHelpers.MessageContains(msg, "postman-echo.com/patch")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask HttpGet_GzipEndpoint_DecompressesResponse()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var token = GenerateUniqueToken();
		var attrName = GenerateAttributeName("HTTPGZIP");
		await SetCallbackAttribute(attrName, token);

		// The /gzip endpoint returns a gzip-compressed body {"gzipped":true,...}.
		// Automatic decompression must be configured for the "api" HttpClient.
		// The unique token is prefixed by the callback attribute ("think {token} %0"),
		// so it appears in the notification regardless of the response body content.
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@http #1/{attrName}={PostmanEchoBase}/gzip"));

		await WaitForNotify(msg =>
			TestHelpers.MessageContains(msg, token) &&
			TestHelpers.MessageContains(msg, "gzipped"));

		await NotifyService
			.Received()
			.Notify(
				Arg.Any<AnySharpObject>(),
				Arg.Is<OneOf<MString, string>>(msg =>
					TestHelpers.MessageContains(msg, token) &&
					TestHelpers.MessageContains(msg, "gzipped")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask HttpGet_DeflateEndpoint_DecompressesResponse()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var token = GenerateUniqueToken();
		var attrName = GenerateAttributeName("HTTPDEFL");
		await SetCallbackAttribute(attrName, token);

		// The /deflate endpoint returns a deflate-compressed body {"deflated":true,...}.
		// Automatic decompression must be configured for the "api" HttpClient.
		// The unique token is prefixed by the callback attribute ("think {token} %0"),
		// so it appears in the notification regardless of the response body content.
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@http #1/{attrName}={PostmanEchoBase}/deflate"));

		await WaitForNotify(msg =>
			TestHelpers.MessageContains(msg, token) &&
			TestHelpers.MessageContains(msg, "deflated"));

		await NotifyService
			.Received()
			.Notify(
				Arg.Any<AnySharpObject>(),
				Arg.Is<OneOf<MString, string>>(msg =>
					TestHelpers.MessageContains(msg, token) &&
					TestHelpers.MessageContains(msg, "deflated")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask HttpGet_WithQueryParams_EchoesArgsField()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var token = GenerateUniqueToken();
		var attrName = GenerateAttributeName("HTTPQP");
		await SetCallbackAttribute(attrName, token);

		// Use the unique token as both the query param key and value so the assertion
		// is scoped to this test's request. postman-echo echoes query params in "args".
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@http #1/{attrName}={PostmanEchoBase}/get?{token}={token}"));

		await WaitForNotify(msg => TestHelpers.MessageContains(msg, token));

		await NotifyService
			.Received()
			.Notify(
				Arg.Any<AnySharpObject>(),
				Arg.Is<OneOf<MString, string>>(msg =>
					TestHelpers.MessageContains(msg, token)), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask HttpCommand_InvalidUrl_ReturnsErrorImmediately()
	{
		var token = GenerateUniqueToken();
		var attrName = GenerateAttributeName("HTTPERR");
		await SetCallbackAttribute(attrName, token);

		var result = await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@http #1/{attrName}=not-a-valid-url"));

		// Invalid URLs are rejected synchronously before the task is queued.
		await Assert.That(result.Message?.ToPlainText()).Contains("#-1");
	}

	[Test]
	public async ValueTask HttpCommand_GetWithBody_RejectsImmediately()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var token = GenerateUniqueToken();
		var attrName = GenerateAttributeName("HTTPGERR");
		await SetCallbackAttribute(attrName, token);

		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@http/get #1/{attrName}={PostmanEchoBase}/get,{token}"));

		// GET with a body is refused before the task is queued — error message is immediate.
		await NotifyService
			.Received()
			.Notify(
				Arg.Any<AnySharpObject>(),
				Arg.Is<OneOf<MString, string>>(msg =>
					TestHelpers.MessageContains(msg, "GET requests cannot have a body")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask HttpGet_StatusRegister_Contains200()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var token = GenerateUniqueToken();
		var attrName = GenerateAttributeName("HTTPSTAT");

		// Use %q<STATUS> to emit the HTTP status code alongside the unique token.
		await SetCallbackAttributeWithContent(attrName, $"think {token} %q<STATUS>");

		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@http #1/{attrName}={PostmanEchoBase}/get?testid={token}"));

		// The callback should emit "{token} 200" because postman-echo returns 200 OK.
		await WaitForNotify(msg =>
			TestHelpers.MessageContains(msg, token) &&
			TestHelpers.MessageContains(msg, "200"));

		await NotifyService
			.Received()
			.Notify(
				Arg.Any<AnySharpObject>(),
				Arg.Is<OneOf<MString, string>>(msg =>
					TestHelpers.MessageContains(msg, token) &&
					TestHelpers.MessageContains(msg, "200")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask HttpGet_StatusRegister_Contains404ForNotFoundEndpoint()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var token = GenerateUniqueToken();
		var attrName = GenerateAttributeName("HTTPST4");

		// Use %q<STATUS> to emit the HTTP status code alongside the unique token.
		await SetCallbackAttributeWithContent(attrName, $"think {token} %q<STATUS>");

		// postman-echo.com/status/404 deliberately returns a 404 response.
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@http #1/{attrName}={PostmanEchoBase}/status/404"));

		// The callback should emit "{token} 404".
		await WaitForNotify(msg =>
			TestHelpers.MessageContains(msg, token) &&
			TestHelpers.MessageContains(msg, "404"));

		await NotifyService
			.Received()
			.Notify(
				Arg.Any<AnySharpObject>(),
				Arg.Is<OneOf<MString, string>>(msg =>
					TestHelpers.MessageContains(msg, token) &&
					TestHelpers.MessageContains(msg, "404")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}
}

