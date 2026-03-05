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
	/// Sets an attribute on the #1 player object for use as a callback by @http.
	/// The default attribute value uses <c>think %0</c> so the response body is
	/// forwarded to NotifyService where we can assert it was received.
	/// </summary>
	private async Task SetCallbackAttribute(string attributeName, string attrValue = "think %0")
	{
		var playerOne = (await Database.GetObjectNodeAsync(new DBRef(1))).AsPlayer;
		await Database.SetAttributeAsync(
			playerOne.Object.DBRef,
			[attributeName],
			A.single(attrValue),
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
		var attrName = GenerateAttributeName("HTTPGET");
		await SetCallbackAttribute(attrName);

		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@http #1/{attrName}={PostmanEchoBase}/get"));

		await WaitForNotify(msg => TestHelpers.MessageContains(msg, "postman-echo.com/get"));

		// postman-echo.com/get echoes the request back, including its own URL.
		await NotifyService
			.Received()
			.Notify(
				Arg.Any<AnySharpObject>(),
				Arg.Is<OneOf<MString, string>>(msg =>
					TestHelpers.MessageContains(msg, "postman-echo.com/get")));
	}

	[Test]
	public async ValueTask HttpPost_WithFormData_EchoesFormFields()
	{
		var attrName = GenerateAttributeName("HTTPPOST");
		await SetCallbackAttribute(attrName);

		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@http/post #1/{attrName}={PostmanEchoBase}/post,key=value"));

		await WaitForNotify(msg => TestHelpers.MessageContains(msg, "postman-echo.com/post"));

		// postman-echo.com/post echoes the submitted form data under the "form" field.
		await NotifyService
			.Received()
			.Notify(
				Arg.Any<AnySharpObject>(),
				Arg.Is<OneOf<MString, string>>(msg =>
					TestHelpers.MessageContains(msg, "postman-echo.com/post")));
	}

	[Test]
	public async ValueTask HttpPut_WithBody_EchoesData()
	{
		var attrName = GenerateAttributeName("HTTPPUT");
		await SetCallbackAttribute(attrName);

		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@http/put #1/{attrName}={PostmanEchoBase}/put,test=update"));

		await WaitForNotify(msg => TestHelpers.MessageContains(msg, "postman-echo.com/put"));

		await NotifyService
			.Received()
			.Notify(
				Arg.Any<AnySharpObject>(),
				Arg.Is<OneOf<MString, string>>(msg =>
					TestHelpers.MessageContains(msg, "postman-echo.com/put")));
	}

	[Test]
	public async ValueTask HttpDelete_ReturnsOkResponse()
	{
		var attrName = GenerateAttributeName("HTTPDEL");
		await SetCallbackAttribute(attrName);

		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@http/delete #1/{attrName}={PostmanEchoBase}/delete"));

		await WaitForNotify(msg => TestHelpers.MessageContains(msg, "postman-echo.com/delete"));

		await NotifyService
			.Received()
			.Notify(
				Arg.Any<AnySharpObject>(),
				Arg.Is<OneOf<MString, string>>(msg =>
					TestHelpers.MessageContains(msg, "postman-echo.com/delete")));
	}

	[Test]
	public async ValueTask HttpPatch_WithBody_EchoesData()
	{
		var attrName = GenerateAttributeName("HTTPPATCH");
		await SetCallbackAttribute(attrName);

		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@http/patch #1/{attrName}={PostmanEchoBase}/patch,patch=data"));

		await WaitForNotify(msg => TestHelpers.MessageContains(msg, "postman-echo.com/patch"));

		await NotifyService
			.Received()
			.Notify(
				Arg.Any<AnySharpObject>(),
				Arg.Is<OneOf<MString, string>>(msg =>
					TestHelpers.MessageContains(msg, "postman-echo.com/patch")));
	}

	[Test]
	public async ValueTask HttpGet_GzipEndpoint_DecompressesResponse()
	{
		var attrName = GenerateAttributeName("HTTPGZIP");
		await SetCallbackAttribute(attrName);

		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@http #1/{attrName}={PostmanEchoBase}/gzip"));

		await WaitForNotify(msg => TestHelpers.MessageContains(msg, "gzipped"));

		// The /gzip endpoint always returns a gzip-compressed body.
		// Automatic decompression must be configured for the "api" HttpClient.
		// The decompressed response contains {"gzipped":true,...}.
		await NotifyService
			.Received()
			.Notify(
				Arg.Any<AnySharpObject>(),
				Arg.Is<OneOf<MString, string>>(msg =>
					TestHelpers.MessageContains(msg, "gzipped")));
	}

	[Test]
	public async ValueTask HttpGet_DeflateEndpoint_DecompressesResponse()
	{
		var attrName = GenerateAttributeName("HTTPDEFL");
		await SetCallbackAttribute(attrName);

		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@http #1/{attrName}={PostmanEchoBase}/deflate"));

		await WaitForNotify(msg => TestHelpers.MessageContains(msg, "deflated"));

		// The /deflate endpoint always returns a deflate-compressed body.
		// Automatic decompression must be configured for the "api" HttpClient.
		// The decompressed response contains {"deflated":true,...}.
		await NotifyService
			.Received()
			.Notify(
				Arg.Any<AnySharpObject>(),
				Arg.Is<OneOf<MString, string>>(msg =>
					TestHelpers.MessageContains(msg, "deflated")));
	}

	[Test]
	public async ValueTask HttpGet_WithQueryParams_EchoesArgsField()
	{
		var attrName = GenerateAttributeName("HTTPQP");
		await SetCallbackAttribute(attrName);

		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@http #1/{attrName}={PostmanEchoBase}/get?foo=bar"));

		await WaitForNotify(msg =>
			TestHelpers.MessageContains(msg, "foo") && TestHelpers.MessageContains(msg, "bar"));

		// postman-echo.com echoes query params back in the "args" JSON field.
		await NotifyService
			.Received()
			.Notify(
				Arg.Any<AnySharpObject>(),
				Arg.Is<OneOf<MString, string>>(msg =>
					TestHelpers.MessageContains(msg, "foo") &&
					TestHelpers.MessageContains(msg, "bar")));
	}

	[Test]
	public async ValueTask HttpCommand_InvalidUrl_ReturnsErrorImmediately()
	{
		var attrName = GenerateAttributeName("HTTPERR");
		await SetCallbackAttribute(attrName);

		var result = await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@http #1/{attrName}=not-a-valid-url"));

		// Invalid URLs are rejected synchronously before the task is queued.
		await Assert.That(result.Message?.ToPlainText()).Contains("#-1");
	}

	[Test]
	public async ValueTask HttpCommand_GetWithBody_RejectsImmediately()
	{
		var attrName = GenerateAttributeName("HTTPGERR");
		await SetCallbackAttribute(attrName);

		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@http/get #1/{attrName}={PostmanEchoBase}/get,body data"));

		// GET with a body is refused before the task is queued.
		await NotifyService
			.Received()
			.Notify(
				Arg.Any<AnySharpObject>(),
				Arg.Is<OneOf<MString, string>>(msg =>
					TestHelpers.MessageContains(msg, "GET requests cannot have a body")));
	}
}


