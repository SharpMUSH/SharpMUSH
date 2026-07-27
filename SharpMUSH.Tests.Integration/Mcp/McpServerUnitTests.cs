using OneOf.Types;
using System.Collections.Immutable;
using System.Security.Claims;
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
	private static MushBasicAuthenticationHandler CreateHandler(
		IMediator mediator, IPasswordService passwordService, IAccountService? accountService = null)
	{
		var options = Substitute.For<IOptionsMonitor<AuthenticationSchemeOptions>>();
		options.Get(Arg.Any<string>()).Returns(new AuthenticationSchemeOptions());
		accountService ??= Substitute.For<IAccountService>();
		return new MushBasicAuthenticationHandler(
			options, NullLoggerFactory.Instance, UrlEncoder.Default, mediator, passwordService, accountService);
	}

	private static SharpPlayer MakePlayer(int key, string name, long creationTime, string passwordHash)
	{
		var obj = new SharpObject
		{
			Key = key,
			CreationTime = creationTime,
			Name = name,
			Type = "Player",
			Locks = ImmutableDictionary<string, SharpLockData>.Empty,
			Owner = new(async ct => { await ValueTask.CompletedTask; return null!; }),
			Powers = new(() => AsyncEnumerable.Empty<SharpPower>()),
			Attributes = new(() => AsyncEnumerable.Empty<SharpAttribute>()),
			LazyAttributes = new(() => AsyncEnumerable.Empty<LazySharpAttribute>()),
			AllAttributes = new(() => AsyncEnumerable.Empty<SharpAttribute>()),
			LazyAllAttributes = new(() => AsyncEnumerable.Empty<LazySharpAttribute>()),
			Flags = new(() => AsyncEnumerable.Empty<SharpObjectFlag>()),
			Parent = new(async ct => { await ValueTask.CompletedTask; return new None(); }),
			Zone = new(async ct => { await ValueTask.CompletedTask; return new None(); }),
			Children = new(() => AsyncEnumerable.Empty<SharpObject>()),
		};

		return new SharpPlayer
		{
			Object = obj,
			Aliases = [],
			Location = new(async ct => { await ValueTask.CompletedTask; return null!; }),
			Home = new(async ct => { await ValueTask.CompletedTask; return null!; }),
			PasswordHash = passwordHash,
			PasswordSalt = null,
			Quota = 20,
		};
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
	public async Task ValidCharacter_WithOwningAccount_PutsAccountIdInNameIdentifier()
	{
		var player = MakePlayer(42, "TestChar", 1700000000L, "stored-hash");
		var mediator = Substitute.For<IMediator>();
		mediator.CreateStream(Arg.Any<GetPlayerQuery>()).Returns(new[] { player }.ToAsyncEnumerable());
		var passwordService = Substitute.For<IPasswordService>();
		passwordService.PasswordIsValid(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns(true);
		var accountService = Substitute.For<IAccountService>();
		accountService.GetAccountForCharacterAsync(Arg.Any<DBRef>(), Arg.Any<CancellationToken>())
			.Returns(new SharpAccount { Id = "node_accounts/7", Username = "owner", PasswordHash = "h" });

		var handler = CreateHandler(mediator, passwordService, accountService);
		var result = await AuthenticateAsync(handler, Basic("TestChar", "correct-password"));

		await Assert.That(result.Succeeded).IsTrue();
		await Assert.That(result.Principal!.FindFirstValue(ClaimTypes.NameIdentifier)).IsEqualTo("node_accounts/7");
		await Assert.That(result.Principal!.GetActingCharacter()!.Value.Number).IsEqualTo(42);
	}

	[Test]
	public async Task ValidCharacter_WithoutOwningAccount_SucceedsWithNoAccountId()
	{
		var player = MakePlayer(42, "TestChar", 1700000000L, "stored-hash");
		var mediator = Substitute.For<IMediator>();
		mediator.CreateStream(Arg.Any<GetPlayerQuery>()).Returns(new[] { player }.ToAsyncEnumerable());
		var passwordService = Substitute.For<IPasswordService>();
		passwordService.PasswordIsValid(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns(true);
		var accountService = Substitute.For<IAccountService>();
		accountService.GetAccountForCharacterAsync(Arg.Any<DBRef>(), Arg.Any<CancellationToken>())
			.Returns((SharpAccount?)null);

		var handler = CreateHandler(mediator, passwordService, accountService);
		var result = await AuthenticateAsync(handler, Basic("TestChar", "correct-password"));

		// Character-password basic auth still authenticates; it just carries no account, so
		// account-anchored writes reject it instead of acting as somebody.
		await Assert.That(result.Succeeded).IsTrue();
		await Assert.That(result.Principal!.FindFirstValue(ClaimTypes.NameIdentifier)).IsNull();
		await Assert.That(result.Principal!.GetActingCharacter()!.Value.Number).IsEqualTo(42);
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
