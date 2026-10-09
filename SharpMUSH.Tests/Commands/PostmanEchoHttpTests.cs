using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using System.Diagnostics;

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
[Category("External")]
public class PostmanEchoHttpTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private ISharpDatabase Database => WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;

	private const string PostmanEchoBase = "https://postman-echo.com";
	private const int MaxWaitSeconds = 60;

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
		var playerOne = (await Database.GetObjectNodeAsync(new DBRef(1))).Expect<SharpPlayer>();
		await Database.SetAttributeAsync(
			playerOne.Object.DBRef,
			[attributeName],
			MarkupText.Plain($"think {uniqueToken} %0"),
			playerOne);
	}

	/// <summary>
	/// Sets an attribute on the #1 player object with custom MUSH code content.
	/// </summary>
	private async Task SetCallbackAttributeWithContent(string attributeName, string mushCode)
	{
		var playerOne = (await Database.GetObjectNodeAsync(new DBRef(1))).Expect<SharpPlayer>();
		await Database.SetAttributeAsync(
			playerOne.Object.DBRef,
			[attributeName],
			MarkupText.Plain(mushCode),
			playerOne);
	}

	/// <summary>
	/// Polls until <paramref name="recipient"/> has been told something matching
	/// <paramref name="predicate"/>, or the <paramref name="timeout"/> elapses. This keeps individual
	/// test durations short on fast networks while still allowing generous headroom for slow or busy
	/// environments.
	/// </summary>
	private async Task WaitForNotify(
		DBRef recipient,
		Func<string, bool> predicate,
		TimeSpan? timeout = null)
	{
		var timeoutSeconds = timeout?.TotalSeconds ?? MaxWaitSeconds;
		var deadline = Stopwatch.GetTimestamp()
			+ Stopwatch.Frequency * (long)timeoutSeconds;

		while (Stopwatch.GetTimestamp() < deadline)
		{
			if (WebAppFactoryArg.Notifications.For(recipient).Any(predicate))
				return;

			await Task.Delay(200);
		}

		throw new TimeoutException(
			$"Timed out after {timeoutSeconds}s waiting for expected @http callback notification from postman-echo.");
	}

	/// <summary>How many announcements <paramref name="recipient"/> made to themselves that match <paramref name="predicate"/>.</summary>
	private int Announced(DBRef recipient, Func<string, bool> predicate)
		=> WebAppFactoryArg.Notifications.DeliveriesFor(recipient).Count(delivery =>
			delivery.Sender == recipient
			&& delivery.Type == INotifyService.NotificationType.Announce
			&& predicate(delivery.Message));

	[Test]
	public async ValueTask HttpGet_ReturnsJsonWithEchoedUrl()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var token = GenerateUniqueToken();
		var attrName = GenerateAttributeName("HTTPGET");
		await SetCallbackAttribute(attrName, token);

		// postman-echo echoes the request URL in the response, which includes the unique token.
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@http #1/{attrName}={PostmanEchoBase}/get?testid={token}"));

		await WaitForNotify(executor, msg => msg.Contains(token));

		await Assert.That(Announced(executor, msg => msg.Contains(token) && msg.Contains("postman-echo.com/get"))).IsEqualTo(1);
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
			MarkupText.Plain($"@http/post #1/{attrName}={PostmanEchoBase}/post,testid={token}"));

		await WaitForNotify(executor, msg => msg.Contains(token));

		await Assert.That(Announced(executor, msg => msg.Contains(token) && msg.Contains("postman-echo.com/post"))).IsEqualTo(1);
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
			MarkupText.Plain($"@http/put #1/{attrName}={PostmanEchoBase}/put,testid={token}"));

		await WaitForNotify(executor, msg => msg.Contains(token));

		await Assert.That(Announced(executor, msg => msg.Contains(token) && msg.Contains("postman-echo.com/put"))).IsEqualTo(1);
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
			MarkupText.Plain($"@http/delete #1/{attrName}={PostmanEchoBase}/delete?testid={token}"));

		await WaitForNotify(executor, msg => msg.Contains(token));

		await Assert.That(Announced(executor, msg => msg.Contains(token) && msg.Contains("postman-echo.com/delete"))).IsEqualTo(1);
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
			MarkupText.Plain($"@http/patch #1/{attrName}={PostmanEchoBase}/patch,testid={token}"));

		await WaitForNotify(executor, msg => msg.Contains(token));

		await Assert.That(Announced(executor, msg => msg.Contains(token) && msg.Contains("postman-echo.com/patch"))).IsEqualTo(1);
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
			MarkupText.Plain($"@http #1/{attrName}={PostmanEchoBase}/gzip"));

		await WaitForNotify(executor, msg =>
			msg.Contains(token) &&
			msg.Contains("gzipped"));

		await Assert.That(Announced(executor, msg => msg.Contains(token) && msg.Contains("gzipped"))).IsEqualTo(1);
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
			MarkupText.Plain($"@http #1/{attrName}={PostmanEchoBase}/deflate"));

		await WaitForNotify(executor, msg =>
			msg.Contains(token) &&
			msg.Contains("deflated"));

		await Assert.That(Announced(executor, msg => msg.Contains(token) && msg.Contains("deflated"))).IsEqualTo(1);
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
			MarkupText.Plain($"@http #1/{attrName}={PostmanEchoBase}/get?{token}={token}"));

		await WaitForNotify(executor, msg => msg.Contains(token));

		await Assert.That(Announced(executor, msg => msg.Contains(token))).IsEqualTo(1);
	}

	[Test]
	public async ValueTask HttpCommand_InvalidUrl_ReturnsErrorImmediately()
	{
		var token = GenerateUniqueToken();
		var attrName = GenerateAttributeName("HTTPERR");
		await SetCallbackAttribute(attrName, token);

		var result = await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@http #1/{attrName}=not-a-valid-url"));

		// Invalid URLs are rejected synchronously before the task is queued.
		await Assert.That(result.Message.ToPlainText()).Contains("#-1");
	}

	[Test]
	public async ValueTask HttpCommand_GetWithBody_RejectsImmediately()
	{
		// The refusal is not unique to this test, so it is read from a player of its own.
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, WebAppFactoryArg.Services.GetRequiredService<IMediator>(), ConnectionService, "HttpGetBody");
		var token = GenerateUniqueToken();
		var attrName = GenerateAttributeName("HTTPGERR");

		await Parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain($"@http/get me/{attrName}={PostmanEchoBase}/get,{token}"));

		// GET with a body is refused before the task is queued — error message is immediate.
		await Assert.That(Announced(player.DbRef, msg => msg == "GET requests cannot have a body.")).IsEqualTo(1);
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
			MarkupText.Plain($"@http #1/{attrName}={PostmanEchoBase}/get?testid={token}"));

		// The callback should emit "{token} 200" because postman-echo returns 200 OK.
		await WaitForNotify(executor, msg =>
			msg.Contains(token) &&
			msg.Contains("200"));

		await Assert.That(Announced(executor, msg => msg == $"{token} 200")).IsEqualTo(1);
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
			MarkupText.Plain($"@http #1/{attrName}={PostmanEchoBase}/status/404"));

		// The callback should emit "{token} 404".
		await WaitForNotify(executor, msg =>
			msg.Contains(token) &&
			msg.Contains("404"));

		await Assert.That(Announced(executor, msg => msg == $"{token} 404")).IsEqualTo(1);
	}
}

