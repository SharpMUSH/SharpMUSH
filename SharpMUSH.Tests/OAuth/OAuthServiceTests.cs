using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Library.Applications;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Portal;
using SharpMUSH.Library.OAuth;
using SharpMUSH.Library.Plugins;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Tests.Applications;
using SharpMUSH.Tests.Authentication;
using SharpMUSH.Tests.Plugins;

namespace SharpMUSH.Tests.OAuth;

/// <summary>
/// The authorization server against a real LMDB world: the code flow with PKCE, single-use codes and refresh tokens,
/// the scopes an app may ask for (permission categories among them), revocation, and a hosted package's grant.
/// </summary>
public class OAuthServiceTests
{
	private const string Redirect = "https://builder.example.test/callback";
	private const string Account = "node_accounts/7";
	private const string Verifier = "a-verifier-of-at-least-forty-three-characters-0123456789";
	private static readonly DBRef Character = new(42, 1_700_000_000_000);

	private string _path = null!;
	private LightningDatabase _db = null!;
	private PluginDirectories _directories = null!;
	private ManualTime _time = null!;
	private InMemoryRoleRegistry _registry = null!;
	private OAuthService _oauth = null!;

	/// <summary>The permissions the character holds, as the capability service reads them.</summary>
	private HashSet<string> _held = null!;

	private sealed class ManualTime : TimeProvider
	{
		public DateTimeOffset Now { get; set; } = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);
		public override DateTimeOffset GetUtcNow() => Now;
	}

	[Before(Test)]
	public void Setup()
	{
		_path = Path.Join(Path.GetTempPath(), "sharpmush-oauth-" + Guid.NewGuid().ToString("N"));
		_db = new LightningDatabase(NullLogger<LightningDatabase>.Instance,
			new LightningStoreOptions { Path = _path, MapSize = 64L << 20 }, Substitute.For<IPasswordService>(), relations: null);
		_directories = PluginPackageFixture.ScratchDirectories();
		_time = new ManualTime();
		_registry = InMemoryRoleRegistry.Seeded();
		_held = new(StringComparer.OrdinalIgnoreCase) { PortalPermission.GamePower("Builder") };
		var capabilities = Substitute.For<IAdministrativeCapabilityService>();
		capabilities.GetGrantedScopesAsync(Arg.Any<CapabilityActor>(), Arg.Any<CancellationToken>()).Returns(_ => _held);
		_oauth = new OAuthService(_db, new UiPackageStore(_directories), _registry, new Lazy<IAdministrativeCapabilityService>(capabilities), _time);
	}

	[After(Test)]
	public async Task Teardown()
	{
		await _db.DisposeAsync();
		PluginPackageFixture.Delete(_directories);
		try
		{
			if (Directory.Exists(_path)) Directory.Delete(_path, recursive: true);
		}
		catch (IOException)
		{
			// Best-effort: a lingering mdb.lck can outlive the writer thread's join.
		}
	}

	private static string Challenge(string verifier) => Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

	private static OAuthAuthorizeRequest Ask(string clientId, string scope = "commands build", string redirect = Redirect,
		string verifier = Verifier)
		=> new("code", clientId, redirect, scope, "xyz", Challenge(verifier), "S256");

	private async Task<OAuthClient> RegisterAsync(bool confidential = false, params string[] scopes)
		=> (await _oauth.RegisterClientAsync(new OAuthClientDraft("Grid Builder", null, null, [Redirect],
			scopes.Length == 0 ? [DelegatedScopes.Commands, DelegatedScopes.Build] : scopes), confidential)).Expect<RegisteredOAuthClient>().Client;

	private static Dictionary<string, string> Query(string location)
		=> new Uri(location).Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
			.Select(pair => pair.Split('=', 2))
			.ToDictionary(pair => pair[0], pair => Uri.UnescapeDataString(pair[1]));

	private async Task<string> CodeAsync(OAuthAuthorizeRequest request)
	{
		var decision = await _oauth.DecideAsync(request, Account, Character, approved: true);
		return Query(decision.Expect<AuthorizationRedirect>().Location)["code"];
	}

	private ValueTask<TokenOutcome> ExchangeAsync(string clientId, string code, string verifier = Verifier, string? secret = null)
		=> _oauth.TokenAsync(new Dictionary<string, string>
		{
			["grant_type"] = "authorization_code",
			["code"] = code,
			["redirect_uri"] = Redirect,
			["code_verifier"] = verifier,
			["client_id"] = clientId,
		}, new ClientCredentials(null, secret));

	private ValueTask<TokenOutcome> RefreshAsync(string clientId, string refresh)
		=> _oauth.TokenAsync(new Dictionary<string, string>
		{
			["grant_type"] = "refresh_token",
			["refresh_token"] = refresh,
			["client_id"] = clientId,
		}, new ClientCredentials(null, null));

	[Test]
	public async Task ApprovedCode_TradesOnceForTokensActingAsTheCharacter()
	{
		var client = await RegisterAsync();

		var prompt = (await _oauth.CheckAuthorizationAsync(Ask(client.Id), Account, Character)).Expect<AuthorizationPrompt>();
		await Assert.That(prompt.Scopes).IsEquivalentTo([DelegatedScopes.Profile, DelegatedScopes.Commands, DelegatedScopes.Build]);
		await Assert.That(prompt.AlreadyAllowed).IsFalse();

		var decision = (await _oauth.DecideAsync(Ask(client.Id), Account, Character, approved: true)).Expect<AuthorizationRedirect>();
		var query = Query(decision.Location);
		await Assert.That(decision.Location).StartsWith(Redirect + "?");
		await Assert.That(query["state"]).IsEqualTo("xyz");

		var tokens = (await ExchangeAsync(client.Id, query["code"])).Expect<IssuedTokens>();
		await Assert.That(tokens.RefreshToken).IsNotNull();
		var access = (await _oauth.ValidateAccessTokenAsync(tokens.AccessToken)).Expect<DelegatedAccess>();
		await Assert.That(access.Token.Character.Number).IsEqualTo(Character.Number);
		await Assert.That(access.Token.Scopes).IsEquivalentTo(prompt.Scopes);

		// The same code a second time is refused.
		var again = (await ExchangeAsync(client.Id, query["code"])).Expect<TokenError>();
		await Assert.That(again.Code).IsEqualTo(TokenError.InvalidGrant);

		// Asked again for nothing new, the player is not asked.
		var repeat = (await _oauth.CheckAuthorizationAsync(Ask(client.Id), Account, Character)).Expect<AuthorizationPrompt>();
		await Assert.That(repeat.AlreadyAllowed).IsTrue();
	}

	[Test]
	public async Task WrongVerifierOrExpiredCode_IsRefused()
	{
		var client = await RegisterAsync();

		var wrong = (await ExchangeAsync(client.Id, await CodeAsync(Ask(client.Id)), verifier: Verifier + "x")).Expect<TokenError>();
		await Assert.That(wrong.Code).IsEqualTo(TokenError.InvalidGrant);

		var code = await CodeAsync(Ask(client.Id));
		_time.Now += OAuthService.CodeLifetime + TimeSpan.FromSeconds(1);
		var expired = (await ExchangeAsync(client.Id, code)).Expect<TokenError>();
		await Assert.That(expired.Code).IsEqualTo(TokenError.InvalidGrant);
	}

	[Test]
	public async Task RefreshToken_IsGoodOnce_AndAccessTokensRunOut()
	{
		var client = await RegisterAsync();
		var first = (await ExchangeAsync(client.Id, await CodeAsync(Ask(client.Id)))).Expect<IssuedTokens>();

		var second = (await RefreshAsync(client.Id, first.RefreshToken!)).Expect<IssuedTokens>();
		await Assert.That(second.RefreshToken).IsNotEqualTo(first.RefreshToken);
		var reused = (await RefreshAsync(client.Id, first.RefreshToken!)).Expect<TokenError>();
		await Assert.That(reused.Code).IsEqualTo(TokenError.InvalidGrant);

		_time.Now += OAuthService.AccessTokenLifetime + TimeSpan.FromSeconds(1);
		await Assert.That(await _oauth.ValidateAccessTokenAsync(second.AccessToken) is NotFound).IsTrue();
		await Assert.That(await RefreshAsync(client.Id, second.RefreshToken!) is IssuedTokens).IsTrue();
	}

	[Test]
	public async Task RevokingTheGrant_StopsEveryTokenUnderIt()
	{
		var client = await RegisterAsync();
		var tokens = (await ExchangeAsync(client.Id, await CodeAsync(Ask(client.Id)))).Expect<IssuedTokens>();

		var grant = (await _oauth.GetGrantsAsync(Account)).Single();
		await Assert.That(await _oauth.RevokeGrantAsync(grant.Id, "node_accounts/8")).IsFalse();
		await Assert.That(await _oauth.RevokeGrantAsync(grant.Id, Account)).IsTrue();

		await Assert.That(await _oauth.ValidateAccessTokenAsync(tokens.AccessToken) is NotFound).IsTrue();
		await Assert.That(await RefreshAsync(client.Id, tokens.RefreshToken!) is TokenError).IsTrue();
		await Assert.That(await _oauth.GetGrantsAsync(Account)).IsEmpty();
	}

	[Test]
	public async Task AConfidentialApp_MustSendItsSecret()
	{
		var registered = (await _oauth.RegisterClientAsync(new OAuthClientDraft("Server App", null, null, [Redirect],
			[DelegatedScopes.Commands]), confidential: true)).Expect<RegisteredOAuthClient>();
		var code = await CodeAsync(Ask(registered.Client.Id, "commands"));

		var missing = (await ExchangeAsync(registered.Client.Id, code)).Expect<TokenError>();
		await Assert.That(missing.Code).IsEqualTo(TokenError.InvalidClient);
		await Assert.That(await ExchangeAsync(registered.Client.Id, code, secret: registered.Secret) is IssuedTokens).IsTrue();
	}

	[Test]
	public async Task UnregisteredAddress_IsShownToThePlayer_NeverRedirected()
	{
		var client = await RegisterAsync();
		var check = await _oauth.CheckAuthorizationAsync(Ask(client.Id, redirect: "https://evil.example.test/cb"), Account, Character);
		await Assert.That(check is AuthorizationRefused).IsTrue();
	}

	[Test]
	public async Task ScopesBeyondTheApp_OrNamingNoCategory_GoBackAsInvalidScope()
	{
		var client = await RegisterAsync(false, DelegatedScopes.Commands, "scenes", "nothing-here");
		await _registry.UpsertCategoryAsync(CategoryKind.Permission, new RoleCategory("Scenes", "Scene running.", 0));
		await _registry.UpsertCustomPermissionAsync(new CustomPermission("scene.close", "Scenes", "Close any scene.", 0));
		_held.Add("scene.close");

		var beyond = (await _oauth.CheckAuthorizationAsync(Ask(client.Id, "commands game"), Account, Character)).Expect<AuthorizationRedirect>();
		await Assert.That(Query(beyond.Location)["error"]).IsEqualTo("invalid_scope");

		var unknown = (await _oauth.CheckAuthorizationAsync(Ask(client.Id, "nothing-here"), Account, Character)).Expect<AuthorizationRedirect>();
		await Assert.That(Query(unknown.Location)["error"]).IsEqualTo("invalid_scope");

		var category = (await _oauth.CheckAuthorizationAsync(Ask(client.Id, "commands scenes"), Account, Character)).Expect<AuthorizationPrompt>();
		await Assert.That(category.Scopes).IsEquivalentTo([DelegatedScopes.Profile, DelegatedScopes.Commands, "scenes"],
			TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That((await _oauth.GetScopesAsync()).Any(scope => scope is { Scope: "scenes", Category: "Scenes" })).IsTrue();
	}

	[Test]
	public async Task AHostedPackage_AsksOnce_ThenGetsTokensWithoutAsking()
	{
		var folder = Path.Combine(_directories.UiPackages, "grid-builder");
		Directory.CreateDirectory(folder);
		var json = UiDefinitionTests.DefinitionJson.Replace("\"id\":\"graph-proof\"", "\"id\":\"grid-builder\",\"scopes\":[\"commands\",\"build\"]");
		var bytes = Encoding.UTF8.GetBytes(json);
		await File.WriteAllBytesAsync(Path.Combine(folder, "ui.json"), bytes);
		await File.WriteAllTextAsync(Path.Combine(folder, PluginUiBinaryManifest.FileName),
			new PluginUiBinaryManifest(new Dictionary<string, string> { ["ui.json"] = PluginPackageFixture.Sha256Of(bytes) }).ToJson());

		var asked = (await _oauth.IssueHostedTokenAsync(Account, Character, "grid-builder")).Expect<ConsentRequired>();
		await Assert.That(asked.Scopes).IsEquivalentTo([DelegatedScopes.Profile, DelegatedScopes.Commands, DelegatedScopes.Build]);

		await Assert.That(await _oauth.AllowHostedAsync(Account, Character, "grid-builder") is OAuthGrant).IsTrue();
		var tokens = (await _oauth.IssueHostedTokenAsync(Account, Character, "grid-builder")).Expect<IssuedTokens>();
		await Assert.That(tokens.RefreshToken).IsNull();
		await Assert.That(await _oauth.ValidateAccessTokenAsync(tokens.AccessToken) is DelegatedAccess).IsTrue();

		// A hosted package cannot use the code flow: it has no address to send a code to.
		await Assert.That(await _oauth.CheckAuthorizationAsync(Ask("package:grid-builder"), Account, Character) is AuthorizationRefused).IsTrue();
	}

	[Test]
	public async Task AGroupTheCharacterHoldsNothingIn_IsNeitherAskedNorGranted()
	{
		var client = await RegisterAsync();
		_held.Clear();

		var prompt = (await _oauth.CheckAuthorizationAsync(Ask(client.Id), Account, Character)).Expect<AuthorizationPrompt>();
		await Assert.That(prompt.Scopes).IsEquivalentTo([DelegatedScopes.Profile, DelegatedScopes.Commands],
			TUnit.Assertions.Enums.CollectionOrdering.Matching);
		var tokens = (await ExchangeAsync(client.Id, await CodeAsync(Ask(client.Id)))).Expect<IssuedTokens>();
		await Assert.That(tokens.Scopes).DoesNotContain(DelegatedScopes.Build);

		// Made a builder later, the character is asked about building before the app gets it.
		_held.Add(PortalPermission.GamePower("Builder"));
		var later = (await _oauth.CheckAuthorizationAsync(Ask(client.Id), Account, Character)).Expect<AuthorizationPrompt>();
		await Assert.That(later.AlreadyAllowed).IsFalse();
		await Assert.That(later.Scopes).Contains(DelegatedScopes.Build);
	}
}
