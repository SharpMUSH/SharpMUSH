using System.Collections.Immutable;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using OneOf.Types;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
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
			Status = isDisabled ? AccountStatus.Disabled : AccountStatus.Active,
		};

	private static SharpPlayer MakePlayer(int key, string name, long creationTime = 0L)
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

		var cache = new FusionCache(
			new Microsoft.Extensions.Options.OptionsWrapper<FusionCacheOptions>(new FusionCacheOptions()));
		return new AccountClaimsService(accountServiceForClaims, roleDerivation, roleRegistry, permissionResolver,
			cache, new AccountClaimsInvalidator(cache), NullLogger<AccountClaimsService>.Instance);
	}

	/// <summary>A guard with no rules, so the sitelock re-check never fires for tests about other things.</summary>
	private static SitelockGuard PermissiveSitelockGuard() => SitelockGuardFor([]);

	private static SitelockGuard SitelockGuardFor(Dictionary<string, string[]> rules)
	{
		var options = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
		options.CurrentValue.Returns(
			ReadPennMushConfig.Create("Configuration/Testfile/mushcnf.dst") with
			{
				SitelockRules = new SitelockRulesOptions(rules)
			});
		return new SitelockGuard(options);
	}

	private static async Task<AccountSessionAuthenticationHandler> CreateHandlerWithHeaderAsync(
		IAccountSessionStore sessionStore,
		IAccountService accountService,
		AccountClaimsService accountClaims,
		string? authorizationHeader = null,
		string? accessTokenQuery = null,
		string? actingCharacterHeader = null,
		string? actingCharacterQuery = null,
		SitelockGuard? sitelockGuard = null,
		string? remoteIp = null)
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
			accountClaims,
			sitelockGuard ?? PermissiveSitelockGuard());

		var httpContext = new DefaultHttpContext
		{
			RequestServices = new ServiceCollection().BuildServiceProvider(),
		};
		if (remoteIp is not null)
			httpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(remoteIp);
		if (authorizationHeader is not null)
			httpContext.Request.Headers.Authorization = authorizationHeader;
		if (actingCharacterHeader is not null)
			httpContext.Request.Headers["X-Acting-Character"] = actingCharacterHeader;
		var query = accessTokenQuery is not null ? $"access_token={accessTokenQuery}" : null;
		if (actingCharacterQuery is not null)
			query = query is null ? $"character={actingCharacterQuery}" : $"{query}&character={actingCharacterQuery}";
		if (query is not null)
			httpContext.Request.QueryString = new QueryString($"?{query}");

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

		sessionStore.ValidateAsync("good").Returns(Task.FromResult<IAccountSessionStore.SessionIdentity?>(
			new IAccountSessionStore.SessionIdentity("node_accounts/1", 1, MakePlayer(1, "Alice").Object.CreationTime)));
		accountService.GetByIdAsync("node_accounts/1")
			.Returns(new ValueTask<SharpAccount?>(MakeAccount()));
		accountService.GetCharactersAsync("node_accounts/1")
			.Returns(new ValueTask<IReadOnlyList<SharpPlayer>>((IReadOnlyList<SharpPlayer>)[MakePlayer(1, "Alice")]));
		// AccountClaimsService.ComputeAccountRoleAsync only calls DeriveAccountRole (mocked below
		// to return Wizard) when the account has at least one character; an empty list short-
		// circuits to the Guest floor regardless of the mock.
		accountServiceForClaims.GetCharactersAsync("node_accounts/1", Arg.Any<CancellationToken>())
			.Returns(new ValueTask<IReadOnlyList<SharpPlayer>>((IReadOnlyList<SharpPlayer>)[MakePlayer(1, "Alice")]));

		var accountClaims = MakeAccountClaims(accountServiceForClaims, PortalRole.Wizard, "players.view");

		var handler = await CreateHandlerWithHeaderAsync(sessionStore, accountService, accountClaims,
			authorizationHeader: "Bearer good");

		var result = await handler.AuthenticateAsync();

		await Assert.That(result.Succeeded).IsTrue();
		await Assert.That(result.Principal!.FindFirst(GameHub.CharacterDbrefClaim)!.Value).IsEqualTo("#1:0");
		await Assert.That(result.Principal!.IsInRole("Wizard")).IsTrue();
		await Assert.That(result.Principal!.FindAll(PortalPermission.ClaimType).Select(c => c.Value))
			.Contains("players.view");
		await Assert.That(result.Principal!.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value)
			.IsEqualTo("node_accounts/1");
	}

	/// <summary>
	/// Regression pin (post-#691): the AccountSession scheme must emit the SAME character claim set as
	/// <c>DebugAuthenticationHandler</c> — <c>character_key</c>, <c>character_creation_time</c>,
	/// <c>character_name</c>, and <c>character_dbref</c> for the primary character. Before this, only
	/// <c>character_dbref</c> was emitted, so <c>ApiControllerBase.CurrentCharacterKey</c> was populated
	/// under DebugAuth (dev) but null under AccountSession (production), silently diverging per environment.
	/// </summary>
	[Test]
	public async Task ValidToken_EmitsFullCharacterClaimSet_ForPrimaryCharacter()
	{
		var sessionStore = Substitute.For<IAccountSessionStore>();
		var accountService = Substitute.For<IAccountService>();
		var accountServiceForClaims = Substitute.For<IAccountService>();

		sessionStore.ValidateAsync("good").Returns(Task.FromResult<IAccountSessionStore.SessionIdentity?>(
			new IAccountSessionStore.SessionIdentity("node_accounts/1", 42, 987654321L)));
		accountService.GetByIdAsync("node_accounts/1")
			.Returns(new ValueTask<SharpAccount?>(MakeAccount()));
		accountService.GetCharactersAsync("node_accounts/1")
			.Returns(new ValueTask<IReadOnlyList<SharpPlayer>>((IReadOnlyList<SharpPlayer>)[MakePlayer(42, "Alice", 987654321L)]));
		accountServiceForClaims.GetCharactersAsync("node_accounts/1", Arg.Any<CancellationToken>())
			.Returns(new ValueTask<IReadOnlyList<SharpPlayer>>((IReadOnlyList<SharpPlayer>)[MakePlayer(42, "Alice", 987654321L)]));

		var accountClaims = MakeAccountClaims(accountServiceForClaims, PortalRole.Wizard, "players.view");

		var handler = await CreateHandlerWithHeaderAsync(sessionStore, accountService, accountClaims,
			authorizationHeader: "Bearer good");

		var result = await handler.AuthenticateAsync();

		await Assert.That(result.Succeeded).IsTrue();
		await Assert.That(result.Principal!.FindFirst("character_key")!.Value).IsEqualTo("42");
		await Assert.That(result.Principal!.FindFirst("character_creation_time")!.Value).IsEqualTo("987654321");
		await Assert.That(result.Principal!.FindFirst("character_name")!.Value).IsEqualTo("Alice");
		await Assert.That(result.Principal!.FindFirst(GameHub.CharacterDbrefClaim)!.Value).IsEqualTo("#42:987654321");
	}

	private static (IAccountSessionStore, IAccountService, AccountClaimsService) TwoCharacterAccount()
	{
		var sessionStore = Substitute.For<IAccountSessionStore>();
		var accountService = Substitute.For<IAccountService>();
		var accountServiceForClaims = Substitute.For<IAccountService>();

		IReadOnlyList<SharpPlayer> roster = [MakePlayer(1, "Alice", 111L), MakePlayer(7, "Bob", 777L)];
		// Bound to Bob (#7), the NON-primary — the binding lives in the session, so this is what an
		// account that switched characters looks like on its next request.
		sessionStore.ValidateAsync("good").Returns(Task.FromResult<IAccountSessionStore.SessionIdentity?>(
			new IAccountSessionStore.SessionIdentity("node_accounts/1", 7, 777L)));
		accountService.GetByIdAsync("node_accounts/1").Returns(new ValueTask<SharpAccount?>(MakeAccount()));
		accountService.GetCharactersAsync("node_accounts/1").Returns(new ValueTask<IReadOnlyList<SharpPlayer>>(roster));
		accountServiceForClaims.GetCharactersAsync("node_accounts/1", Arg.Any<CancellationToken>()).Returns(new ValueTask<IReadOnlyList<SharpPlayer>>(roster));

		return (sessionStore, accountService, MakeAccountClaims(accountServiceForClaims, PortalRole.Wizard, "players.view"));
	}

	[Test]
	public async Task SessionBoundToANonPrimaryCharacter_ActsAsIt()
	{
		var (sessionStore, accountService, accountClaims) = TwoCharacterAccount();

		// No hint header: the acting character comes from the token alone.
		var handler = await CreateHandlerWithHeaderAsync(sessionStore, accountService, accountClaims,
			authorizationHeader: "Bearer good");

		var result = await handler.AuthenticateAsync();

		await Assert.That(result.Succeeded).IsTrue();
		await Assert.That(result.Principal!.FindFirst(GameHub.CharacterDbrefClaim)!.Value).IsEqualTo("#7:777");
		await Assert.That(result.Principal!.FindFirst("character_key")!.Value).IsEqualTo("7");
		await Assert.That(result.Principal!.FindFirst("character_name")!.Value).IsEqualTo("Bob");
	}

	[Test]
	public async Task AClientSuppliedHint_CannotChangeTheActingCharacter()
	{
		var (sessionStore, accountService, accountClaims) = TwoCharacterAccount();

		// The token is bound to Bob (#7); the request asks to act as Alice (#1). The header is not read
		// at all any more — this is the property that makes the acting identity unspoofable.
		var handler = await CreateHandlerWithHeaderAsync(sessionStore, accountService, accountClaims,
			authorizationHeader: "Bearer good", actingCharacterHeader: "#1");

		var result = await handler.AuthenticateAsync();

		await Assert.That(result.Succeeded).IsTrue();
		await Assert.That(result.Principal!.FindFirst(GameHub.CharacterDbrefClaim)!.Value).IsEqualTo("#7:777");
	}

	[Test]
	public async Task SessionBoundToACharacterTheAccountNoLongerOwns_ActsAsNobody()
	{
		var sessionStore = Substitute.For<IAccountSessionStore>();
		var accountService = Substitute.For<IAccountService>();
		var accountServiceForClaims = Substitute.For<IAccountService>();

		// Bound to #7, but the live roster no longer contains it (unlinked after the token was minted).
		IReadOnlyList<SharpPlayer> roster = [MakePlayer(1, "Alice", 111L)];
		sessionStore.ValidateAsync("good").Returns(Task.FromResult<IAccountSessionStore.SessionIdentity?>(
			new IAccountSessionStore.SessionIdentity("node_accounts/1", 7, 777L)));
		accountService.GetByIdAsync("node_accounts/1").Returns(new ValueTask<SharpAccount?>(MakeAccount()));
		accountService.GetCharactersAsync("node_accounts/1").Returns(new ValueTask<IReadOnlyList<SharpPlayer>>(roster));
		accountServiceForClaims.GetCharactersAsync("node_accounts/1", Arg.Any<CancellationToken>()).Returns(new ValueTask<IReadOnlyList<SharpPlayer>>(roster));

		var handler = await CreateHandlerWithHeaderAsync(sessionStore, accountService,
			MakeAccountClaims(accountServiceForClaims, PortalRole.Player), authorizationHeader: "Bearer good");

		var result = await handler.AuthenticateAsync();

		// Membership is re-checked per request, and there is deliberately no fallback to the primary:
		// acting as someone the account no longer owns must stop at once, not silently redirect.
		await Assert.That(result.Succeeded).IsTrue();
		await Assert.That(result.Principal!.FindFirst(GameHub.CharacterDbrefClaim)).IsNull();
	}

	/// <summary>
	/// N-02 regression: the session minted at REGISTRATION names no character, because the account
	/// owned none yet. Creating one afterwards never rebound it, so that session acted as nobody
	/// forever — the roster reported the account's only character as not acting, and every write
	/// needing a character identity answered "Missing character identity." until the player logged
	/// out and back in.
	/// </summary>
	[Test]
	public async Task SessionWithNoBoundCharacter_ActsAsTheAccountsOnlyCharacter()
	{
		var sessionStore = Substitute.For<IAccountSessionStore>();
		var accountService = Substitute.For<IAccountService>();
		var accountServiceForClaims = Substitute.For<IAccountService>();

		// Minted before the account had any character; the character arrived afterwards.
		sessionStore.ValidateAsync("registration-token").Returns(Task.FromResult<IAccountSessionStore.SessionIdentity?>(
			new IAccountSessionStore.SessionIdentity("node_accounts/1", null, null)));
		IReadOnlyList<SharpPlayer> roster = [MakePlayer(12, "Gwendolyn", 555L)];
		accountService.GetByIdAsync("node_accounts/1").Returns(new ValueTask<SharpAccount?>(MakeAccount()));
		accountService.GetCharactersAsync("node_accounts/1").Returns(new ValueTask<IReadOnlyList<SharpPlayer>>(roster));
		accountServiceForClaims.GetCharactersAsync("node_accounts/1", Arg.Any<CancellationToken>()).Returns(new ValueTask<IReadOnlyList<SharpPlayer>>(roster));

		var handler = await CreateHandlerWithHeaderAsync(sessionStore, accountService,
			MakeAccountClaims(accountServiceForClaims, PortalRole.Player), authorizationHeader: "Bearer registration-token");

		var result = await handler.AuthenticateAsync();

		await Assert.That(result.Succeeded).IsTrue();
		await Assert.That(result.Principal!.FindFirst(GameHub.CharacterDbrefClaim)!.Value).IsEqualTo("#12:555");
		await Assert.That(result.Principal!.FindFirst("character_name")!.Value).IsEqualTo("Gwendolyn");
	}

	/// <summary>
	/// The multi-character rule for an unbound session: lowest dbref, the same character login binds.
	/// The roster is deliberately supplied out of order — <c>GetCharactersAsync</c> passes the backend
	/// query through unsorted, so an unordered pick would name a different character per request.
	/// </summary>
	[Test]
	public async Task SessionWithNoBoundCharacter_MultipleCharacters_ActsAsTheLowestDbref()
	{
		var sessionStore = Substitute.For<IAccountSessionStore>();
		var accountService = Substitute.For<IAccountService>();
		var accountServiceForClaims = Substitute.For<IAccountService>();

		sessionStore.ValidateAsync("unbound").Returns(Task.FromResult<IAccountSessionStore.SessionIdentity?>(
			new IAccountSessionStore.SessionIdentity("node_accounts/1", null, null)));
		IReadOnlyList<SharpPlayer> roster = [MakePlayer(9, "Zed", 999L), MakePlayer(3, "Alice", 333L)];
		accountService.GetByIdAsync("node_accounts/1").Returns(new ValueTask<SharpAccount?>(MakeAccount()));
		accountService.GetCharactersAsync("node_accounts/1").Returns(new ValueTask<IReadOnlyList<SharpPlayer>>(roster));
		accountServiceForClaims.GetCharactersAsync("node_accounts/1", Arg.Any<CancellationToken>()).Returns(new ValueTask<IReadOnlyList<SharpPlayer>>(roster));

		var handler = await CreateHandlerWithHeaderAsync(sessionStore, accountService,
			MakeAccountClaims(accountServiceForClaims, PortalRole.Player), authorizationHeader: "Bearer unbound");

		var result = await handler.AuthenticateAsync();

		await Assert.That(result.Succeeded).IsTrue();
		await Assert.That(result.Principal!.FindFirst(GameHub.CharacterDbrefClaim)!.Value).IsEqualTo("#3:333");
	}

	/// <summary>
	/// The implicit fallback must never override an explicit choice: a session bound by
	/// <c>AuthController.SwitchCharacter</c> keeps acting as the character it names even though a
	/// lower-dbref one exists. Sibling of <see cref="SessionBoundToANonPrimaryCharacter_ActsAsIt"/>,
	/// pinned separately because the fallback added in N-02 is what could regress it.
	/// </summary>
	[Test]
	public async Task AnExplicitlyChosenCharacter_BeatsTheImplicitFallback()
	{
		var (sessionStore, accountService, accountClaims) = TwoCharacterAccount();

		var handler = await CreateHandlerWithHeaderAsync(sessionStore, accountService, accountClaims,
			authorizationHeader: "Bearer good");

		var result = await handler.AuthenticateAsync();

		// Roster is [#1 Alice, #7 Bob]; the session names Bob. Falling back to the lowest dbref here
		// would silently undo a character switch.
		await Assert.That(result.Principal!.FindFirst(GameHub.CharacterDbrefClaim)!.Value).IsEqualTo("#7:777");
	}

	[Test]
	public async Task UnknownToken_Fails()
	{
		var sessionStore = Substitute.For<IAccountSessionStore>();
		var accountService = Substitute.For<IAccountService>();
		var accountClaims = MakeAccountClaims(Substitute.For<IAccountService>(), PortalRole.Guest);

		sessionStore.ValidateAsync("bad").Returns(Task.FromResult<IAccountSessionStore.SessionIdentity?>(null));

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

		sessionStore.ValidateAsync("disabled-token").Returns(Task.FromResult<IAccountSessionStore.SessionIdentity?>(new IAccountSessionStore.SessionIdentity("node_accounts/2", null, null)));
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

		sessionStore.ValidateAsync("qs-token").Returns(Task.FromResult<IAccountSessionStore.SessionIdentity?>(new IAccountSessionStore.SessionIdentity("node_accounts/3", null, null)));
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

	/// <summary>
	/// A session records the one address it was created at; sitelock rules are patterns judged against
	/// the address a request is actually coming from. Revoking at ban time can only ever reach the
	/// former. This is the case it cannot reach: a live, valid session presented from an address inside
	/// a banned range that it was never created at.
	/// </summary>
	[Test]
	public async Task ValidToken_PresentedFromASitelockedAddress_DoesNotAuthenticate()
	{
		var sessionStore = Substitute.For<IAccountSessionStore>();
		var accountService = Substitute.For<IAccountService>();

		sessionStore.ValidateAsync("good").Returns(Task.FromResult<IAccountSessionStore.SessionIdentity?>(
			new IAccountSessionStore.SessionIdentity("node_accounts/1", null, null)));
		accountService.GetByIdAsync("node_accounts/1").Returns(new ValueTask<SharpAccount?>(MakeAccount()));
		accountService.GetCharactersAsync("node_accounts/1")
			.Returns(new ValueTask<IReadOnlyList<SharpPlayer>>((IReadOnlyList<SharpPlayer>)[]));

		var accountClaims = MakeAccountClaims(Substitute.For<IAccountService>(), PortalRole.Player);
		var guard = SitelockGuardFor(new Dictionary<string, string[]> { ["10.0.0.0/8"] = [SitelockGuard.Connect] });

		var handler = await CreateHandlerWithHeaderAsync(sessionStore, accountService, accountClaims,
			authorizationHeader: "Bearer good", sitelockGuard: guard, remoteIp: "10.11.12.13");

		var result = await handler.AuthenticateAsync();

		await Assert.That(result.Succeeded)
			.IsFalse()
			.Because("sitelock is a rule about the address a request comes from, not about where a session was minted");
		// The session store is never consulted, so a banned address costs less than an allowed one.
		await sessionStore.DidNotReceive().ValidateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
	}

	/// <summary>The control: the same session from an address the rule does not cover still works.</summary>
	[Test]
	public async Task ValidToken_PresentedFromAnAddressOutsideTheRule_StillAuthenticates()
	{
		var sessionStore = Substitute.For<IAccountSessionStore>();
		var accountService = Substitute.For<IAccountService>();

		sessionStore.ValidateAsync("good").Returns(Task.FromResult<IAccountSessionStore.SessionIdentity?>(
			new IAccountSessionStore.SessionIdentity("node_accounts/1", null, null)));
		accountService.GetByIdAsync("node_accounts/1").Returns(new ValueTask<SharpAccount?>(MakeAccount()));
		accountService.GetCharactersAsync("node_accounts/1")
			.Returns(new ValueTask<IReadOnlyList<SharpPlayer>>((IReadOnlyList<SharpPlayer>)[]));

		var accountClaims = MakeAccountClaims(Substitute.For<IAccountService>(), PortalRole.Player);
		var guard = SitelockGuardFor(new Dictionary<string, string[]> { ["10.0.0.0/8"] = [SitelockGuard.Connect] });

		var handler = await CreateHandlerWithHeaderAsync(sessionStore, accountService, accountClaims,
			authorizationHeader: "Bearer good", sitelockGuard: guard, remoteIp: "198.51.100.7");

		var result = await handler.AuthenticateAsync();

		await Assert.That(result.Succeeded).IsTrue();
	}

	/// <summary>
	/// A rule carrying only <c>!create</c> gates registration, not an existing session. Re-checking on
	/// every authenticated request must not quietly widen what a rule means.
	/// </summary>
	[Test]
	public async Task ValidToken_FromAnAddressBannedOnlyForCreate_StillAuthenticates()
	{
		var sessionStore = Substitute.For<IAccountSessionStore>();
		var accountService = Substitute.For<IAccountService>();

		sessionStore.ValidateAsync("good").Returns(Task.FromResult<IAccountSessionStore.SessionIdentity?>(
			new IAccountSessionStore.SessionIdentity("node_accounts/1", null, null)));
		accountService.GetByIdAsync("node_accounts/1").Returns(new ValueTask<SharpAccount?>(MakeAccount()));
		accountService.GetCharactersAsync("node_accounts/1")
			.Returns(new ValueTask<IReadOnlyList<SharpPlayer>>((IReadOnlyList<SharpPlayer>)[]));

		var accountClaims = MakeAccountClaims(Substitute.For<IAccountService>(), PortalRole.Player);
		var guard = SitelockGuardFor(new Dictionary<string, string[]> { ["10.0.0.0/8"] = [SitelockGuard.Create] });

		var handler = await CreateHandlerWithHeaderAsync(sessionStore, accountService, accountClaims,
			authorizationHeader: "Bearer good", sitelockGuard: guard, remoteIp: "10.11.12.13");

		var result = await handler.AuthenticateAsync();

		await Assert.That(result.Succeeded).IsTrue();
	}
}
