using System.Collections.Immutable;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using OneOf.Types;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Authentication;
using SharpMUSH.Server.Hubs;
using ZiggyCreatures.Caching.Fusion;

namespace SharpMUSH.Tests.Authentication;

/// <summary>
/// Unit tests for <see cref="AccountSessionAuthenticationHandler"/> using NSubstitute doubles
/// and a bare <see cref="DefaultHttpContext"/> driven directly through the
/// <see cref="AuthenticationHandler{TOptions}"/> InitializeAsync/AuthenticateAsync pipeline
/// (mirrors the construction style in <c>AuthControllerDebugOttTests</c>).
/// </summary>
public class AccountSessionAuthHandlerTests
{
	private static SharpAccount MakeAccount(string id = "node_accounts/1", string username = "Alice",
		bool isDisabled = false)
		=> new()
		{
			Id = id,
			Username = username,
			Email = null,
			PasswordHash = "hash",
			CreatedAt = 1_000_000,
			IsDisabled = isDisabled,
		};

	private static SharpPlayer MakePlayer(int key, string name)
	{
		var obj = new SharpObject
		{
			Key = key,
			CreationTime = 0L,
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
			PasswordHash = string.Empty,
			PasswordSalt = null,
			Quota = 20,
		};
	}

	/// <summary>
	/// Builds a real <see cref="AccountClaimsService"/> over substituted lower-level dependencies,
	/// pre-wired so <c>ComputeAccountRoleAsync</c> returns <paramref name="role"/> and
	/// <c>ComputeGrantedScopesAsync</c> returns <paramref name="scopes"/> regardless of the
	/// account's actual character/role data.
	/// </summary>
	private static AccountClaimsService MakeAccountClaims(IAccountService accountServiceForClaims,
		PortalRole role, params string[] scopes)
	{
		var roleDerivation = Substitute.For<IRoleDerivationService>();
		var roleRegistry = Substitute.For<IRoleRegistryService>();
		var permissionResolver = Substitute.For<IPermissionResolver>();

		roleDerivation.DeriveAccountRole(Arg.Any<IEnumerable<(int DbrefNumber, IEnumerable<SharpObjectFlag> Flags)>>())
			.Returns(role);
		roleRegistry.GetRolesAsync().Returns(Task.FromResult<IReadOnlyList<SharpRole>>([]));
		roleRegistry.GetRolesForAccountAsync(Arg.Any<string>()).Returns(Task.FromResult<IReadOnlyList<SharpRole>>([]));
		permissionResolver.Resolve(Arg.Any<IEnumerable<SharpRole>>()).Returns(new HashSet<string>(scopes));

		return new AccountClaimsService(accountServiceForClaims, roleDerivation, roleRegistry, permissionResolver,
			new FusionCache(new Microsoft.Extensions.Options.OptionsWrapper<FusionCacheOptions>(new FusionCacheOptions())),
			NullLogger<AccountClaimsService>.Instance);
	}

	private static async Task<AccountSessionAuthenticationHandler> CreateHandlerWithHeaderAsync(
		IAccountSessionStore sessionStore,
		IAccountService accountService,
		AccountClaimsService accountClaims,
		string? authorizationHeader = null,
		string? accessTokenQuery = null)
	{
		var optionsMonitor = Substitute.For<IOptionsMonitor<AuthenticationSchemeOptions>>();
		optionsMonitor.Get(Arg.Any<string>()).Returns(new AuthenticationSchemeOptions());
		optionsMonitor.CurrentValue.Returns(new AuthenticationSchemeOptions());

		var handler = new AccountSessionAuthenticationHandler(
			optionsMonitor,
			NullLoggerFactory.Instance,
			UrlEncoder.Default,
			sessionStore,
			accountService,
			accountClaims);

		var httpContext = new DefaultHttpContext
		{
			RequestServices = new ServiceCollection().BuildServiceProvider(),
		};
		if (authorizationHeader is not null)
			httpContext.Request.Headers.Authorization = authorizationHeader;
		if (accessTokenQuery is not null)
			httpContext.Request.QueryString = new QueryString($"?access_token={accessTokenQuery}");

		var scheme = new AuthenticationScheme(
			AccountSessionAuthenticationHandler.SchemeName,
			AccountSessionAuthenticationHandler.SchemeName,
			typeof(AccountSessionAuthenticationHandler));

		await handler.InitializeAsync(scheme, httpContext);
		return handler;
	}

	[Test]
	public async Task ValidToken_Authenticates_WithRoleAndDbrefClaims()
	{
		var sessionStore = Substitute.For<IAccountSessionStore>();
		var accountService = Substitute.For<IAccountService>();
		var accountServiceForClaims = Substitute.For<IAccountService>();

		sessionStore.ValidateAsync("good").Returns(Task.FromResult<string?>("node_accounts/1"));
		accountService.GetByIdAsync("node_accounts/1")
			.Returns(new ValueTask<SharpAccount?>(MakeAccount()));
		accountService.GetCharactersAsync("node_accounts/1")
			.Returns(new ValueTask<IReadOnlyList<SharpPlayer>>((IReadOnlyList<SharpPlayer>)[MakePlayer(1, "Alice")]));
		// AccountClaimsService.ComputeAccountRoleAsync only calls DeriveAccountRole (mocked below
		// to return Wizard) when the account has at least one character; an empty list short-
		// circuits to the Guest floor regardless of the mock.
		accountServiceForClaims.GetCharactersAsync("node_accounts/1")
			.Returns(new ValueTask<IReadOnlyList<SharpPlayer>>((IReadOnlyList<SharpPlayer>)[MakePlayer(1, "Alice")]));

		var accountClaims = MakeAccountClaims(accountServiceForClaims, PortalRole.Wizard, "players.view");

		var handler = await CreateHandlerWithHeaderAsync(sessionStore, accountService, accountClaims,
			authorizationHeader: "Bearer good");

		var result = await handler.AuthenticateAsync();

		await Assert.That(result.Succeeded).IsTrue();
		await Assert.That(result.Principal!.FindFirst(GameHub.CharacterDbrefClaim)!.Value).IsEqualTo("#1");
		await Assert.That(result.Principal!.IsInRole("Wizard")).IsTrue();
		await Assert.That(result.Principal!.FindAll(PortalPermission.ClaimType).Select(c => c.Value))
			.Contains("players.view");
		await Assert.That(result.Principal!.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value)
			.IsEqualTo("node_accounts/1");
	}

	[Test]
	public async Task UnknownToken_Fails()
	{
		var sessionStore = Substitute.For<IAccountSessionStore>();
		var accountService = Substitute.For<IAccountService>();
		var accountClaims = MakeAccountClaims(Substitute.For<IAccountService>(), PortalRole.Guest);

		sessionStore.ValidateAsync("bad").Returns(Task.FromResult<string?>(null));

		var handler = await CreateHandlerWithHeaderAsync(sessionStore, accountService, accountClaims,
			authorizationHeader: "Bearer bad");

		var result = await handler.AuthenticateAsync();

		await Assert.That(result.Succeeded).IsFalse();
		await Assert.That(result.Failure).IsNotNull();
	}

	[Test]
	public async Task DisabledAccount_Fails()
	{
		var sessionStore = Substitute.For<IAccountSessionStore>();
		var accountService = Substitute.For<IAccountService>();
		var accountClaims = MakeAccountClaims(Substitute.For<IAccountService>(), PortalRole.Guest);

		sessionStore.ValidateAsync("disabled-token").Returns(Task.FromResult<string?>("node_accounts/2"));
		accountService.GetByIdAsync("node_accounts/2")
			.Returns(new ValueTask<SharpAccount?>(MakeAccount(id: "node_accounts/2", isDisabled: true)));

		var handler = await CreateHandlerWithHeaderAsync(sessionStore, accountService, accountClaims,
			authorizationHeader: "Bearer disabled-token");

		var result = await handler.AuthenticateAsync();

		await Assert.That(result.Succeeded).IsFalse();
	}

	[Test]
	public async Task NoHeaderOrQuery_ReturnsNoResult()
	{
		var sessionStore = Substitute.For<IAccountSessionStore>();
		var accountService = Substitute.For<IAccountService>();
		var accountClaims = MakeAccountClaims(Substitute.For<IAccountService>(), PortalRole.Guest);

		var handler = await CreateHandlerWithHeaderAsync(sessionStore, accountService, accountClaims);

		var result = await handler.AuthenticateAsync();

		await Assert.That(result.None).IsTrue();
		await Assert.That(result.Succeeded).IsFalse();
	}

	[Test]
	public async Task ValidToken_ViaQueryParam_Authenticates()
	{
		var sessionStore = Substitute.For<IAccountSessionStore>();
		var accountService = Substitute.For<IAccountService>();

		sessionStore.ValidateAsync("qs-token").Returns(Task.FromResult<string?>("node_accounts/3"));
		accountService.GetByIdAsync("node_accounts/3")
			.Returns(new ValueTask<SharpAccount?>(MakeAccount(id: "node_accounts/3")));
		accountService.GetCharactersAsync("node_accounts/3")
			.Returns(new ValueTask<IReadOnlyList<SharpPlayer>>((IReadOnlyList<SharpPlayer>)[]));

		var accountClaims = MakeAccountClaims(Substitute.For<IAccountService>(), PortalRole.Player);

		var handler = await CreateHandlerWithHeaderAsync(sessionStore, accountService, accountClaims,
			accessTokenQuery: "qs-token");

		var result = await handler.AuthenticateAsync();

		await Assert.That(result.Succeeded).IsTrue();
		await Assert.That(result.Principal!.FindFirst(GameHub.CharacterDbrefClaim)).IsNull();
	}
}
