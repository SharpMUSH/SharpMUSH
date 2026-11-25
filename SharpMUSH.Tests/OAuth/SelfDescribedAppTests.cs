using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Applications;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Portal;
using SharpMUSH.Library.OAuth;
using SharpMUSH.Library.Plugins;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests.Applications;
using SharpMUSH.Tests.Authentication;
using SharpMUSH.Tests.Plugins;

namespace SharpMUSH.Tests.OAuth;

/// <summary>
/// Apps that describe themselves at an https <c>client_id</c>, against a real LMDB world: each policy, staff allowing
/// and blocking, the block list, and how often the description is read.
/// </summary>
public class SelfDescribedAppTests
{
	private const string ClientId = "https://valyria.example.com/oauth/client.json";
	private const string Redirect = "http://127.0.0.1/callback";
	private const string Account = "node_accounts/7";
	private const string Verifier = "a-verifier-of-at-least-forty-three-characters-0123456789";
	private static readonly DBRef Character = new(42, 1_700_000_000_000);

	private string _path = null!;
	private LightningDatabase _db = null!;
	private PluginDirectories _directories = null!;
	private ManualTime _time = null!;
	private FakeFetcher _fetcher = null!;
	private Settings _settings = null!;

	/// <summary>The game's OAuth options as a test changes them.</summary>
	private sealed class Settings
	{
		public OAuthOptions Policy { get; set; } = new();
	}
	private OAuthService _oauth = null!;

	private sealed class ManualTime : TimeProvider
	{
		public DateTimeOffset Now { get; set; } = new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);
		public override DateTimeOffset GetUtcNow() => Now;
	}

	/// <summary>Serves each address's document from a dictionary, counting the reads.</summary>
	private sealed class FakeFetcher : IClientMetadataFetcher
	{
		public Dictionary<string, string> Documents { get; } = new(StringComparer.Ordinal);
		public int Reads { get; private set; }

		/// <summary>Runs during a read, as something else happening while the app's site answers.</summary>
		public Func<Task>? During { get; set; }

		public async ValueTask<Result<string>> FetchAsync(string clientId, CancellationToken ct = default)
		{
			Reads++;
			if (During is { } during) await during();
			return Documents.TryGetValue(clientId, out var json) ? json : new Error<string>("The address answered 404.");
		}
	}

	private static string Document(string clientId = ClientId, string name = "Valyria", string scope = "profile scenes.read")
		=> $$"""{"client_id":"{{clientId}}","client_name":"{{name}}","redirect_uris":["{{Redirect}}"],"scope":"{{scope}}"}""";

	[Before(Test)]
	public void Setup()
	{
		_path = Path.Join(Path.GetTempPath(), "sharpmush-cimd-" + Guid.NewGuid().ToString("N"));
		_db = new LightningDatabase(NullLogger<LightningDatabase>.Instance,
			new LightningStoreOptions { Path = _path, MapSize = 64L << 20 }, Substitute.For<IPasswordService>(), relations: null);
		_directories = PluginPackageFixture.ScratchDirectories();
		_time = new ManualTime();
		_fetcher = new FakeFetcher();
		_fetcher.Documents[ClientId] = Document();
		var capabilities = Substitute.For<IAdministrativeCapabilityService>();
		capabilities.GetGrantedScopesAsync(Arg.Any<CapabilityActor>(), Arg.Any<CancellationToken>())
			.Returns(new HashSet<string>(StringComparer.OrdinalIgnoreCase));
		var options = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
		_settings = new Settings();
		var settings = _settings;
		options.CurrentValue.Returns(_ => SharpMUSHOptions.Default() with { OAuth = settings.Policy });
		_oauth = new OAuthService(_db, new UiPackageStore(_directories), InMemoryRoleRegistry.Seeded(),
			new Lazy<IAdministrativeCapabilityService>(capabilities), _time, _fetcher, options);
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

	private static OAuthAuthorizeRequest Ask(string clientId = ClientId, string scope = "profile scenes.read")
		=> new("code", clientId, Redirect, scope, "xyz",
			Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(Verifier))), "S256");

	private ValueTask<AuthorizationCheck> CheckAsync(string clientId = ClientId)
		=> _oauth.CheckAuthorizationAsync(Ask(clientId), Account, Character);

	private async Task<IssuedTokens> TokensAsync()
	{
		var decision = (await _oauth.DecideAsync(Ask(), Account, Character, approved: true)).Expect<AuthorizationRedirect>();
		var code = new Uri(decision.Location).Query.TrimStart('?').Split('&').Select(pair => pair.Split('=', 2))
			.First(pair => pair[0] == "code")[1];
		return (await _oauth.TokenAsync(new Dictionary<string, string>
		{
			["grant_type"] = "authorization_code",
			["code"] = Uri.UnescapeDataString(code),
			["redirect_uri"] = Redirect,
			["code_verifier"] = Verifier,
			["client_id"] = ClientId,
		}, new ClientCredentials(null, null))).Expect<IssuedTokens>();
	}

	[Test]
	public async Task UnderReview_ANewAppWaitsForStaff_ThenAsksOnceAllowed()
	{
		var waiting = (await CheckAsync()).Expect<AuthorizationRefused>();
		await Assert.That(waiting.Message).Contains("waiting for staff");
		var stored = (await _db.GetOAuthClientAsync(ClientId)).Expect<OAuthClient>();
		await Assert.That(stored.Status).IsEqualTo(OAuthClientStatus.Unreviewed);
		await Assert.That(stored.Name).IsEqualTo("Valyria");

		(await _oauth.SetClientStatusAsync(ClientId, OAuthClientStatus.Allowed)).Expect<OAuthClient>();
		var prompt = (await CheckAsync()).Expect<AuthorizationPrompt>();
		await Assert.That(prompt.App.Status).IsEqualTo(OAuthClientStatus.Allowed);
		await Assert.That(prompt.App.DescribedAt).IsEqualTo("valyria.example.com");
		await Assert.That(prompt.Scopes).Contains(DelegatedScopes.ScenesRead);

		var tokens = await TokensAsync();
		await Assert.That(await _oauth.ValidateAccessTokenAsync(tokens.AccessToken) is DelegatedAccess).IsTrue();
	}

	[Test]
	public async Task Open_AcceptsAtOnce_MarkedNotReviewed()
	{
		_settings.Policy = new() { AppPolicy = OAuthOptions.Open };
		var prompt = (await CheckAsync()).Expect<AuthorizationPrompt>();
		await Assert.That(prompt.App.Status).IsEqualTo(OAuthClientStatus.Unreviewed);
		var tokens = await TokensAsync();
		await Assert.That(await _oauth.ValidateAccessTokenAsync(tokens.AccessToken) is DelegatedAccess).IsTrue();

		// Moving to review stops the tokens of an app staff never allowed.
		_settings.Policy = new() { AppPolicy = OAuthOptions.Review };
		await Assert.That(await _oauth.ValidateAccessTokenAsync(tokens.AccessToken) is NotFound).IsTrue();
	}

	[Test]
	public async Task Registered_ReadsNothing_AndRefuses()
	{
		_settings.Policy = new() { AppPolicy = OAuthOptions.Registered };
		var refused = (await CheckAsync()).Expect<AuthorizationRefused>();
		await Assert.That(refused.Message).Contains("only apps its staff registered");
		await Assert.That(_fetcher.Reads).IsEqualTo(0);
		await Assert.That(await _db.GetOAuthClientAsync(ClientId) is NotFound).IsTrue();
	}

	[Test]
	public async Task Blocking_RefusesTheApp_AndEndsItsTokens()
	{
		_settings.Policy = new() { AppPolicy = OAuthOptions.Open };
		var tokens = await TokensAsync();

		(await _oauth.SetClientStatusAsync(ClientId, OAuthClientStatus.Blocked)).Expect<OAuthClient>();
		await Assert.That(await _oauth.ValidateAccessTokenAsync(tokens.AccessToken) is NotFound).IsTrue();
		var refresh = (await _oauth.TokenAsync(new Dictionary<string, string>
		{
			["grant_type"] = "refresh_token",
			["refresh_token"] = tokens.RefreshToken!,
			["client_id"] = ClientId,
		}, new ClientCredentials(null, null))).Expect<TokenError>();
		await Assert.That(refresh.Code).IsEqualTo(TokenError.InvalidClient);

		var reads = _fetcher.Reads;
		var refused = (await CheckAsync()).Expect<AuthorizationRefused>();
		await Assert.That(refused.Message).Contains("blocked Valyria");
		await Assert.That(_fetcher.Reads).IsEqualTo(reads);
		await Assert.That(await _oauth.GetGrantsAsync(Account)).IsEmpty();
	}

	[Test]
	public async Task ABlockedDomain_RefusesWithoutReading_AndEndsTokens()
	{
		_settings.Policy = new() { AppPolicy = OAuthOptions.Open };
		var tokens = await TokensAsync();
		var reads = _fetcher.Reads;

		_settings.Policy = new() { AppPolicy = OAuthOptions.Open, BlockedDomains = ["example.com"] };
		var refused = (await CheckAsync()).Expect<AuthorizationRefused>();
		await Assert.That(refused.Message).Contains("valyria.example.com");
		await Assert.That(_fetcher.Reads).IsEqualTo(reads);
		await Assert.That(await _oauth.ValidateAccessTokenAsync(tokens.AccessToken) is NotFound).IsTrue();
	}

	[Test]
	public async Task TheDescriptionIsReadAtMostHourly_AndAFailedReadKeepsTheCopy()
	{
		_settings.Policy = new() { AppPolicy = OAuthOptions.Open };
		await CheckAsync();
		await CheckAsync();
		await Assert.That(_fetcher.Reads).IsEqualTo(1);

		_time.Now += ClientIdMetadata.RefreshAfter;
		_fetcher.Documents[ClientId] = Document(name: "Valyria 2");
		var renamed = (await CheckAsync()).Expect<AuthorizationPrompt>();
		await Assert.That(renamed.App.Name).IsEqualTo("Valyria 2");
		await Assert.That(_fetcher.Reads).IsEqualTo(2);

		_time.Now += ClientIdMetadata.RefreshAfter;
		_fetcher.Documents.Remove(ClientId);
		var kept = (await CheckAsync()).Expect<AuthorizationPrompt>();
		await Assert.That(kept.App.Name).IsEqualTo("Valyria 2");
	}

	[Test]
	public async Task ARereadKeepsStaffsDecision()
	{
		await CheckAsync();
		(await _oauth.SetClientStatusAsync(ClientId, OAuthClientStatus.Allowed)).Expect<OAuthClient>();
		_time.Now += ClientIdMetadata.RefreshAfter;
		await Assert.That((await CheckAsync()).Expect<AuthorizationPrompt>().App.Status).IsEqualTo(OAuthClientStatus.Allowed);
	}

	[Test]
	public async Task ADocumentThatCannotBeUsed_IsRefusedAndNotKept()
	{
		_fetcher.Documents[ClientId] = Document(clientId: "https://valyria.example.com/other.json");
		var refused = (await CheckAsync()).Expect<AuthorizationRefused>();
		await Assert.That(refused.Message).Contains("could not be used");
		await Assert.That(await _db.GetOAuthClientAsync(ClientId) is NotFound).IsTrue();
	}

	[Test]
	public async Task PastTheWaitingLimit_ANewAppIsNotRead()
	{
		for (var i = 0; i < ClientIdMetadata.MaxWaiting; i++)
		{
			var id = $"https://app{i}.example.net/client.json";
			_fetcher.Documents[id] = Document(clientId: id);
			await CheckAsync(id);
		}

		var reads = _fetcher.Reads;
		var refused = (await CheckAsync()).Expect<AuthorizationRefused>();
		await Assert.That(refused.Message).Contains("Too many apps");
		await Assert.That(_fetcher.Reads).IsEqualTo(reads);
	}

	[Test]
	public async Task StaffCannotEditOrGiveASecretToASelfDescribedApp()
	{
		await CheckAsync();
		await Assert.That(await _oauth.ResetClientSecretAsync(ClientId) is NotFound).IsTrue();
		var draft = new OAuthClientDraft("Renamed", null, null, ["https://evil.example/cb"], [DelegatedScopes.Profile]);
		await Assert.That(await _oauth.UpdateClientAsync(ClientId, draft) is NotFound).IsTrue();
	}

	[Test]
	public async Task AnAllowedAppThatAsksForMore_WaitsForStaffAgain()
	{
		await CheckAsync();
		(await _oauth.SetClientStatusAsync(ClientId, OAuthClientStatus.Allowed)).Expect<OAuthClient>();

		// Asking for less keeps staff's decision.
		_time.Now += ClientIdMetadata.RefreshAfter;
		_fetcher.Documents[ClientId] = Document(scope: "profile");
		var narrower = await _oauth.CheckAuthorizationAsync(Ask(scope: "profile"), Account, Character);
		await Assert.That(narrower.Expect<AuthorizationPrompt>().App.Status).IsEqualTo(OAuthClientStatus.Allowed);

		_time.Now += ClientIdMetadata.RefreshAfter;
		_fetcher.Documents[ClientId] = Document(scope: "profile commands");
		var waiting = (await CheckAsync()).Expect<AuthorizationRefused>();
		await Assert.That(waiting.Message).Contains("waiting for staff");
		await Assert.That((await _db.GetOAuthClientAsync(ClientId)).Expect<OAuthClient>().Status).IsEqualTo(OAuthClientStatus.Unreviewed);
	}

	[Test]
	public async Task AnAllowedAppThatRenamesItself_WaitsForStaffAgain()
	{
		await CheckAsync();
		(await _oauth.SetClientStatusAsync(ClientId, OAuthClientStatus.Allowed)).Expect<OAuthClient>();
		_time.Now += ClientIdMetadata.RefreshAfter;
		_fetcher.Documents[ClientId] = Document(name: "Official Game Portal");
		await Assert.That((await CheckAsync()).Expect<AuthorizationRefused>().Message).Contains("waiting for staff");
	}

	[Test]
	public async Task UnderOpen_AnUnreviewedAppMayAskOnlyToReadScenes()
	{
		_settings.Policy = new() { AppPolicy = OAuthOptions.Open };
		_fetcher.Documents[ClientId] = Document(scope: "profile scenes.read commands");
		var redirect = (await _oauth.CheckAuthorizationAsync(Ask(scope: "profile commands"), Account, Character)).Expect<AuthorizationRedirect>();
		await Assert.That(Uri.UnescapeDataString(redirect.Location)).Contains("only once staff on this game allow it");
		await Assert.That((await CheckAsync()).Expect<AuthorizationPrompt>().Scopes).DoesNotContain(DelegatedScopes.Commands);
	}

	[Test]
	public async Task AnAppBlockedWhileItsDescriptionIsRead_StaysBlocked()
	{
		await CheckAsync();
		(await _oauth.SetClientStatusAsync(ClientId, OAuthClientStatus.Allowed)).Expect<OAuthClient>();
		_time.Now += ClientIdMetadata.RefreshAfter;
		_fetcher.During = async () => await _oauth.SetClientStatusAsync(ClientId, OAuthClientStatus.Blocked);

		await CheckAsync();
		await Assert.That((await _db.GetOAuthClientAsync(ClientId)).Expect<OAuthClient>().Status).IsEqualTo(OAuthClientStatus.Blocked);
	}

	[Test]
	public async Task AnAppForgottenWhileItsDescriptionIsRead_IsNotStoredAgain()
	{
		await CheckAsync();
		_time.Now += ClientIdMetadata.RefreshAfter;
		_fetcher.During = async () => await _oauth.DeleteClientAsync(ClientId);

		await Assert.That(await CheckAsync() is AuthorizationRefused).IsTrue();
		await Assert.That(await _db.GetOAuthClientAsync(ClientId) is NotFound).IsTrue();
	}

	[Test]
	public async Task AFailedReadIsNotTriedAgainForAWhile()
	{
		_fetcher.Documents.Remove(ClientId);
		await CheckAsync();
		await CheckAsync();
		await Assert.That(_fetcher.Reads).IsEqualTo(1);
		_time.Now += OAuthService.RetryAfterFailure;
		await CheckAsync();
		await Assert.That(_fetcher.Reads).IsEqualTo(2);
	}

	[Test]
	public async Task OneHostCannotFillTheWaitingList()
	{
		for (var i = 0; i < OAuthService.MaxWaitingPerHost; i++)
		{
			var id = $"https://spam.example.org/app{i}.json";
			_fetcher.Documents[id] = Document(clientId: id);
			await CheckAsync(id);
		}

		var next = "https://spam.example.org/one-more.json";
		_fetcher.Documents[next] = Document(clientId: next);
		await Assert.That((await CheckAsync(next)).Expect<AuthorizationRefused>().Message).Contains("Too many apps from spam.example.org");
		// Another host is still read.
		await Assert.That((await CheckAsync()).Expect<AuthorizationRefused>().Message).Contains("waiting for staff");

		// Blocking the domain frees its places.
		_settings.Policy = new() { BlockedDomains = ["spam.example.org"] };
		var other = "https://spam2.example.org/app.json";
		_fetcher.Documents[other] = Document(clientId: other);
		await Assert.That((await CheckAsync(other)).Expect<AuthorizationRefused>().Message).Contains("waiting for staff");
	}

	[Test]
	public async Task UnderRegistered_AnAllowedAppKeepsTheCopyStaffSaw()
	{
		await CheckAsync();
		(await _oauth.SetClientStatusAsync(ClientId, OAuthClientStatus.Allowed)).Expect<OAuthClient>();
		_settings.Policy = new() { AppPolicy = OAuthOptions.Registered };
		_time.Now += ClientIdMetadata.RefreshAfter;
		var reads = _fetcher.Reads;
		await Assert.That((await CheckAsync()).Expect<AuthorizationPrompt>().App.Name).IsEqualTo("Valyria");
		await Assert.That(_fetcher.Reads).IsEqualTo(reads);
	}
}
