using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using Mediator;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Authentication;
using SharpMUSH.Server.Mcp;

namespace SharpMUSH.Tests.Integration.Mcp;

/// <summary>
/// Plain (non-container) unit tests for the MCP server internals: the document-session store's
/// bounding/validation and the auth handler's timing-parity mitigation. Addresses PR #674 review.
/// </summary>
public class McpDocumentStoreTests
{
	[Test]
	public async Task Open_NullText_Throws()
	{
		var store = new McpDocumentStore();
		await Assert.That(() => store.Open(null!)).Throws<ArgumentNullException>();
	}

	[Test]
	public async Task Open_RoundTripsById()
	{
		var store = new McpDocumentStore();
		var id = store.Open("add(1,2)");

		await Assert.That(store.TryGet(id, out var text)).IsTrue();
		await Assert.That(text).IsEqualTo("add(1,2)");
	}

	[Test]
	public async Task Open_BeyondCapacity_EvictsOldestSoTheStoreStaysBounded()
	{
		var store = new McpDocumentStore();

		// Capacity is 1024; opening well past it must evict the oldest ids rather than grow forever.
		var firstId = store.Open("first");
		string lastId = firstId;
		for (var i = 0; i < 1200; i++)
		{
			lastId = store.Open($"doc {i}");
		}

		await Assert.That(store.TryGet(firstId, out _)).IsFalse();
		await Assert.That(store.TryGet(lastId, out _)).IsTrue();
	}
}

public class MushBasicAuthenticationHandlerTests
{
	private static MushBasicAuthenticationHandler CreateHandler(IMediator mediator, IPasswordService passwordService)
	{
		var options = Substitute.For<IOptionsMonitor<AuthenticationSchemeOptions>>();
		options.Get(Arg.Any<string>()).Returns(new AuthenticationSchemeOptions());
		return new MushBasicAuthenticationHandler(
			options, NullLoggerFactory.Instance, UrlEncoder.Default, mediator, passwordService);
	}

	private static async Task<AuthenticateResult> AuthenticateAsync(
		MushBasicAuthenticationHandler handler, string authorizationHeader)
	{
		var context = new DefaultHttpContext();
		context.Request.Headers["Authorization"] = authorizationHeader;
		await handler.InitializeAsync(
			new AuthenticationScheme(MushBasicAuthenticationHandler.SchemeName, null, typeof(MushBasicAuthenticationHandler)),
			context);
		return await handler.AuthenticateAsync();
	}

	private static string Basic(string user, string pw)
		=> "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{pw}"));

	[Test]
	public async Task UnknownCharacter_StillRunsPasswordVerification_ForTimingParity()
	{
		var mediator = Substitute.For<IMediator>();
		mediator.CreateStream(Arg.Any<GetPlayerQuery>()).Returns(AsyncEnumerable.Empty<SharpPlayer>());
		var passwordService = Substitute.For<IPasswordService>();

		var handler = CreateHandler(mediator, passwordService);
		var result = await AuthenticateAsync(handler, Basic("NoSuchCharacter", "guessed-password"));

		// Unknown character must fail — but only after a verification runs, so the response
		// latency doesn't reveal that the character does not exist.
		await Assert.That(result.Succeeded).IsFalse();
		passwordService.Received().PasswordIsValid(Arg.Any<string>(), "guessed-password", Arg.Any<string>());
	}

	[Test]
	public async Task MissingAuthorizationHeader_ReturnsNoResult()
	{
		var mediator = Substitute.For<IMediator>();
		var passwordService = Substitute.For<IPasswordService>();

		var handler = CreateHandler(mediator, passwordService);

		var context = new DefaultHttpContext();
		await handler.InitializeAsync(
			new AuthenticationScheme(MushBasicAuthenticationHandler.SchemeName, null, typeof(MushBasicAuthenticationHandler)),
			context);
		var result = await handler.AuthenticateAsync();

		await Assert.That(result.None).IsTrue();
	}
}
