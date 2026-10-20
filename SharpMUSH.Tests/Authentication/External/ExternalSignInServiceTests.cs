using System.Buffers.Text;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.API;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Authentication.External;
using SharpMUSH.Server.Controllers;

namespace SharpMUSH.Tests.Authentication.External;

/// <summary>
/// Signing in through another service, end to end against a real world file, with a stand-in for the service's
/// token and profile addresses.
/// </summary>
public class ExternalSignInServiceTests
{
	private const string Origin = "https://play.example.test";
	private const string Binding = "tab-binding-0123456789abcdef";

	private string _path = null!;
	private LightningDatabase _db = null!;
	private ManualTime _time = null!;
	private FakeService _service = null!;
	private ExternalSignInProviders _providers = null!;
	private ExternalSignInService _signIn = null!;
	private ISecretStore _secrets = null!;

	private sealed class ManualTime : TimeProvider
	{
		public DateTimeOffset Now { get; set; } = new(2026, 10, 16, 3, 0, 0, TimeSpan.Zero);
		public override DateTimeOffset GetUtcNow() => Now;
	}

	/// <summary>Server data kept as JSON per type, as the database keeps it.</summary>
	private sealed class InMemoryServerData : IExpandedObjectDataService
	{
		private readonly Dictionary<Type, string> _json = [];

		public ValueTask<T?> GetExpandedDataAsync<T>(SharpObject obj) where T : class => throw new NotSupportedException();

		public ValueTask SetExpandedDataAsync<T>(T data, SharpObject obj, bool ignoreNull = false) where T : class
			=> throw new NotSupportedException();

		public ValueTask<T?> GetExpandedServerDataAsync<T>() where T : class
			=> ValueTask.FromResult(_json.TryGetValue(typeof(T), out var json) ? JsonSerializer.Deserialize<T>(json) : null);

		public ValueTask SetExpandedServerDataAsync<T>(T data, bool ignoreNull = false) where T : class
		{
			_json[typeof(T)] = JsonSerializer.Serialize(data);
			return ValueTask.CompletedTask;
		}
	}

	/// <summary>
	/// The service's token and profile addresses. It hands out a code per person, checks every token request the
	/// way a service would (secret, redirect address, PKCE verifier), and answers the profile of whoever the code
	/// was for.
	/// </summary>
	private sealed class FakeService : HttpMessageHandler
	{
		public string Secret { get; set; } = "client-secret";
		public Dictionary<string, (string Challenge, string RedirectUri, string Profile)> Codes { get; } = [];
		public List<string> Refusals { get; } = [];

		/// <summary>The code the service sends back after <paramref name="profile"/> signs in on its page.</summary>
		public string Authorize(string authorizeUrl, string profile)
		{
			var query = QueryOf(authorizeUrl);
			var code = Guid.NewGuid().ToString("N");
			Codes[code] = (query["code_challenge"], query["redirect_uri"], profile);
			return code;
		}

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			if (request.RequestUri!.AbsolutePath == "/token")
			{
				var form = QueryOf("?" + await request.Content!.ReadAsStringAsync(cancellationToken));
				if (!Codes.Remove(form["code"], out var issued)) return Refuse("unknown code");
				if (form["client_secret"] != Secret) return Refuse("wrong secret");
				if (form["redirect_uri"] != issued.RedirectUri) return Refuse("wrong redirect");
				var challenge = Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(form["code_verifier"])));
				if (challenge != issued.Challenge) return Refuse("wrong verifier");
				return Json($$"""{"access_token":"{{Convert.ToBase64String(Encoding.UTF8.GetBytes(issued.Profile))}}","token_type":"Bearer"}""");
			}

			if (request.RequestUri.AbsolutePath == "/me" && request.Headers.Authorization is { Scheme: "Bearer" } bearer)
				return Json(Encoding.UTF8.GetString(Convert.FromBase64String(bearer.Parameter!)));

			return Refuse("unknown address");
		}

		private HttpResponseMessage Refuse(string why)
		{
			Refusals.Add(why);
			return new HttpResponseMessage(HttpStatusCode.BadRequest);
		}

		private static HttpResponseMessage Json(string body)
			=> new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
	}

	private static Dictionary<string, string> QueryOf(string url)
		=> url[(url.IndexOf('?') + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries)
			.Select(pair => pair.Split('=', 2))
			.ToDictionary(pair => pair[0], pair => Uri.UnescapeDataString(pair[1].Replace('+', ' ')));

	private static readonly SignInProviderEndpoints Endpoints = new("https://id.example.test/authorize",
		"https://id.example.test/token", "https://id.example.test/me", "identify email", "id", "global_name username",
		"email", "verified");

	[Before(Test)]
	public async Task Setup()
	{
		_path = Path.Combine(Path.GetTempPath(), "sharpmush-lmdb-" + Guid.NewGuid().ToString("N"));
		_db = new LightningDatabase(NullLogger<LightningDatabase>.Instance,
			new LightningStoreOptions { Path = _path, MapSize = 256L << 20 }, Substitute.For<IPasswordService>(), relations: null);
		await _db.Migrate();

		_time = new ManualTime();
		_service = new FakeService();
		_secrets = Substitute.For<ISecretStore>();
		_secrets.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns(call => ValueTask.FromResult<FoundResult<string>>(call.Arg<string>().StartsWith("signin.")
				? "client-secret"
				: new NotFound()));
		_providers = new ExternalSignInProviders(new InMemoryServerData(), _secrets, _db);

		var http = Substitute.For<IHttpClientFactory>();
		http.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(_service, disposeHandler: false));
		_signIn = new ExternalSignInService(_providers, new ExternalSignInFlows(_time), _db, http,
			new ConfigurationBuilder().Build(), _time, NullLogger<ExternalSignInService>.Instance);

		(await _providers.SaveAsync("chat", new SignInProviderUpdate(ExternalSignInKinds.Custom, "Chat", "client-id", true,
			Endpoints: Endpoints))).Expect<ExternalSignInProvider>();
	}

	[After(Test)]
	public async Task Cleanup()
	{
		_service.Dispose();
		await _db.DisposeAsync();
		try
		{
			if (Directory.Exists(_path)) Directory.Delete(_path, recursive: true);
		}
		catch (IOException)
		{
			// Best-effort: the lock file can outlive the writer briefly.
		}
	}

	private static HttpRequest Request(string originHeader = Origin)
	{
		var uri = new Uri(Origin);
		var context = new DefaultHttpContext();
		context.Request.Scheme = "http";
		context.Request.Host = new HostString(uri.Authority);
		context.Request.Headers.Origin = originHeader;
		return context.Request;
	}

	private static string Profile(string id, string name, string? email = null, bool verified = true)
		=> JsonSerializer.Serialize(new { id, global_name = name, email, verified });

	/// <summary>The whole round trip to the service, ending at the flow the portal finishes.</summary>
	private async Task<Result<ExternalSignInService.ReturnedFlow>> SignInAsync(string profile, string? linkAccountId = null,
		string binding = Binding)
	{
		var authorize = (await _signIn.BeginAsync("chat", Binding, linkAccountId, Request())).Expect<string>();
		var code = _service.Authorize(authorize, profile);
		var back = await _signIn.CallbackAsync("chat", QueryOf(authorize)["state"], code, error: null);
		return await _signIn.ReturnedAsync(QueryOf(back).GetValueOrDefault("flow"), binding);
	}

	[Test]
	public async Task The_sign_in_page_address_carries_the_client_the_callback_and_a_pkce_challenge()
	{
		var authorize = (await _signIn.BeginAsync("chat", Binding, null, Request())).Expect<string>();
		var query = QueryOf(authorize);

		await Assert.That(authorize).StartsWith("https://id.example.test/authorize?");
		await Assert.That(query["client_id"]).IsEqualTo("client-id");
		// The page's own origin, as the browser says it: the request alone read as http.
		await Assert.That(query["redirect_uri"]).IsEqualTo($"{Origin}/api/auth/external/chat/callback");
		await Assert.That(query["code_challenge_method"]).IsEqualTo("S256");
		await Assert.That(query["scope"]).IsEqualTo("identify email");
	}

	[Test]
	public async Task A_new_identity_makes_a_passwordless_account_that_it_then_signs_in_to()
	{
		var first = (await SignInAsync(Profile("1001", "Ada", "ada@example.test"))).Expect<ExternalSignInService.ReturnedFlow>();
		await Assert.That(await _signIn.LinkedAccountAsync(first) is NotFound).IsTrue();
		await Assert.That(first.Identity.Email).IsEqualTo("ada@example.test");

		var made = (await _signIn.CreateAccountAsync(first, "Ada", first.Identity.Email)).Expect<SharpAccount>();
		await Assert.That(made.PasswordHash).IsEmpty();
		await Assert.That(made.Email).IsEqualTo("ada@example.test");

		_time.Now += TimeSpan.FromDays(1);
		var again = (await SignInAsync(Profile("1001", "Ada L."))).Expect<ExternalSignInService.ReturnedFlow>();
		var account = (await _signIn.LinkedAccountAsync(again)).Expect<SharpAccount>();

		await Assert.That(account.Id).IsEqualTo(made.Id);
		var link = (await _db.GetAccountExternalLoginsAsync(made.Id!)).Single();
		await Assert.That(link.Name).IsEqualTo("Ada L.");
		await Assert.That(link.LastUsedAt).IsEqualTo(_time.Now);
	}

	[Test]
	public async Task Only_the_tab_that_started_a_flow_can_finish_it()
	{
		var stolen = await SignInAsync(Profile("1002", "Bea"), binding: "someone-elses-binding-value");

		await Assert.That(stolen is Error<string>).IsTrue();
	}

	[Test]
	public async Task A_return_from_the_service_is_answered_once()
	{
		var authorize = (await _signIn.BeginAsync("chat", Binding, null, Request())).Expect<string>();
		var state = QueryOf(authorize)["state"];
		var code = _service.Authorize(authorize, Profile("1003", "Cy"));

		await Assert.That(await _signIn.CallbackAsync("chat", state, code, null)).Contains("?flow=");
		await Assert.That(await _signIn.CallbackAsync("chat", state, code, null)).IsEqualTo("/login/external?error=expired");
	}

	[Test]
	public async Task An_identity_on_one_account_cannot_be_linked_to_another()
	{
		var alice = await _db.CreateAccountAsync("alice", null, "hash");
		var bob = await _db.CreateAccountAsync("bob", null, "hash");

		var forAlice = (await SignInAsync(Profile("1004", "Dee"), alice.Id)).Expect<ExternalSignInService.ReturnedFlow>();
		await Assert.That(await _signIn.LinkAsync(forAlice, alice.Id!)).IsEqualTo(ExternalLoginAddOutcome.Added);

		var forBob = (await SignInAsync(Profile("1004", "Dee"), bob.Id)).Expect<ExternalSignInService.ReturnedFlow>();
		await Assert.That(await _signIn.LinkAsync(forBob, bob.Id!)).IsEqualTo(ExternalLoginAddOutcome.LinkedElsewhere);

		var second = (await SignInAsync(Profile("1005", "Dee two"), alice.Id)).Expect<ExternalSignInService.ReturnedFlow>();
		await Assert.That(await _signIn.LinkAsync(second, alice.Id!)).IsEqualTo(ExternalLoginAddOutcome.ProviderAlreadyLinked);
	}

	[Test]
	public async Task A_link_left_on_a_deleted_account_moves_to_a_new_one()
	{
		var flow = (await SignInAsync(Profile("1006", "Eve"))).Expect<ExternalSignInService.ReturnedFlow>();
		var old = (await _signIn.CreateAccountAsync(flow, "eve", null)).Expect<SharpAccount>();
		await _db.UpdateAccountStatusAsync(old.Id!, AccountStatus.Deleted);

		var again = (await SignInAsync(Profile("1006", "Eve"))).Expect<ExternalSignInService.ReturnedFlow>();
		await Assert.That(await _signIn.LinkedAccountAsync(again) is NotFound).IsTrue();
		var made = (await _signIn.CreateAccountAsync(again, "eve2", null)).Expect<SharpAccount>();

		await Assert.That((await _signIn.LinkedAccountAsync(again)).Expect<SharpAccount>().Id).IsEqualTo(made.Id);
		await Assert.That(await _db.GetAccountExternalLoginsAsync(old.Id!)).IsEmpty();
	}

	[Test]
	public async Task Two_sign_ups_with_one_identity_make_one_account()
	{
		var flow = (await SignInAsync(Profile("1007", "Fay"))).Expect<ExternalSignInService.ReturnedFlow>();

		await Assert.That(await _signIn.CreateAccountAsync(flow, "fay", null) is SharpAccount).IsTrue();
		await Assert.That(await _signIn.CreateAccountAsync(flow, "fay2", null) is ExternalLoginTaken).IsTrue();
		await Assert.That(await _db.GetAccountByUsernameAsync("fay2")).IsNull();
	}

	[Test]
	public async Task An_email_the_service_has_not_verified_is_not_offered()
	{
		var flow = (await SignInAsync(Profile("1008", "Gus", "gus@example.test", verified: false)))
			.Expect<ExternalSignInService.ReturnedFlow>();

		await Assert.That(flow.Identity.Email).IsNull();
	}

	[Test]
	public async Task A_numeric_id_and_a_missing_name_still_identify_someone()
	{
		var identity = ExternalSignInService.Identity(JsonDocument.Parse("""{"id":5649138,"login":"grave"}""").RootElement,
			ExternalSignInProviders.Preset(ExternalSignInKinds.GitHub)!);

		await Assert.That(identity!.Subject).IsEqualTo("5649138");
		await Assert.That(identity.Name).IsEqualTo("grave");
		await Assert.That(identity.Email).IsNull();
	}

	[Test]
	public async Task A_wrong_client_secret_signs_no_one_in()
	{
		_service.Secret = "the service changed it";

		var flow = await SignInAsync(Profile("1009", "Hal"));

		await Assert.That(flow is Error<string>).IsTrue();
		await Assert.That(_service.Refusals).Contains("wrong secret");
	}

	[Test]
	public async Task Cancelling_on_the_service_page_says_so()
	{
		var authorize = (await _signIn.BeginAsync("chat", Binding, null, Request())).Expect<string>();
		var back = await _signIn.CallbackAsync("chat", QueryOf(authorize)["state"], code: null, error: "access_denied");

		var flow = await _signIn.ReturnedAsync(QueryOf(back)["flow"], Binding);

		await Assert.That(flow.Expect<Error<string>>().Value).IsEqualTo("Signing in with Chat was cancelled.");
	}

	[Test]
	public async Task A_provider_without_its_secret_is_not_offered()
	{
		_secrets.GetAsync("signin.chat.secret", Arg.Any<CancellationToken>())
			.Returns(ValueTask.FromResult<FoundResult<string>>(new NotFound()));

		await Assert.That(await _providers.ReadyAsync()).IsEmpty();
		await Assert.That(await _signIn.BeginAsync("chat", Binding, null, Request()) is Error<string>).IsTrue();
	}

	[Test]
	public async Task A_page_on_another_site_cannot_start_a_sign_in()
	{
		var started = await _signIn.BeginAsync("chat", Binding, null, Request(originHeader: "https://evil.example.test"));

		await Assert.That(started is Error<string>).IsTrue();
	}

	[Test]
	public async Task The_only_way_into_a_passwordless_account_cannot_be_removed()
	{
		var flow = (await SignInAsync(Profile("1010", "Ivy"))).Expect<ExternalSignInService.ReturnedFlow>();
		var account = (await _signIn.CreateAccountAsync(flow, "ivy", null)).Expect<SharpAccount>();

		await Assert.That(await _signIn.KeepsAWayInAsync(account, withoutProvider: "chat")).IsFalse();

		(await _providers.SaveAsync("other", new SignInProviderUpdate(ExternalSignInKinds.Discord, null, "id", true)))
			.Expect<ExternalSignInProvider>();
		await _db.AddAccountExternalLoginAsync(new AccountExternalLogin(account.Id!, "other", "x1", "Ivy", _time.Now, null));

		await Assert.That(await _signIn.KeepsAWayInAsync(account, withoutProvider: "chat")).IsTrue();
	}

	[Test]
	public async Task A_custom_provider_must_use_https()
	{
		var saved = await _providers.SaveAsync("plain", new SignInProviderUpdate(ExternalSignInKinds.Custom, null, "id", true,
			Endpoints: Endpoints with { TokenUrl = "http://id.example.test/token" }));

		await Assert.That(saved is Error<string>).IsTrue();
	}

	[Test]
	public async Task Two_removals_at_once_cannot_take_the_last_way_in()
	{
		var flow = (await SignInAsync(Profile("1111", "Jun"))).Expect<ExternalSignInService.ReturnedFlow>();
		var account = (await _signIn.CreateAccountAsync(flow, "jun", null)).Expect<SharpAccount>();
		(await _providers.SaveAsync("other", new SignInProviderUpdate(ExternalSignInKinds.Discord, null, "id", true)))
			.Expect<ExternalSignInProvider>();
		await _db.AddAccountExternalLoginAsync(new AccountExternalLogin(account.Id!, "other", "x2", "Jun", _time.Now, null));

		// The first removal holds its turn until the second has been asked for.
		var held = new TaskCompletionSource();
		var first = _signIn.RemoveWayInAsync(account, async () =>
		{
			await held.Task;
			return await _signIn.UnlinkAsync(account.Id!, "chat");
		}, withoutProvider: "chat").AsTask();
		var second = _signIn.RemoveWayInAsync(account, () => _signIn.UnlinkAsync(account.Id!, "other"),
			withoutProvider: "other").AsTask();
		held.SetResult();

		await Assert.That(await first).IsEqualTo(WayInRemoval.Removed);
		await Assert.That(await second).IsEqualTo(WayInRemoval.OnlyWayIn);
		await Assert.That(await _db.GetAccountExternalLoginsAsync(account.Id!)).Count().IsEqualTo(1);
	}

	[Test]
	public async Task Removing_a_provider_removes_the_links_to_it()
	{
		var flow = (await SignInAsync(Profile("1212", "Kit"))).Expect<ExternalSignInService.ReturnedFlow>();
		var account = (await _signIn.CreateAccountAsync(flow, "kit", null)).Expect<SharpAccount>();
		(await _providers.SaveAsync("chat2", new SignInProviderUpdate(ExternalSignInKinds.Discord, null, "id", true)))
			.Expect<ExternalSignInProvider>();
		await _db.AddAccountExternalLoginAsync(new AccountExternalLogin(account.Id!, "chat2", "1212", "Kit", _time.Now, null));

		await Assert.That(await _providers.RemoveAsync("chat")).IsTrue();

		// A service set up again under the id is a new one: the person its ids name now is not the one linked before.
		await Assert.That(await _db.GetAccountExternalLoginAsync("chat", "1212")).IsNull();
		await Assert.That((await _db.GetAccountExternalLoginsAsync(account.Id!)).Select(link => link.Provider))
			.IsEquivalentTo(["chat2"]);
	}

	[Test]
	public async Task A_saved_provider_cannot_be_pointed_at_another_service()
	{
		var kind = await _providers.SaveAsync("chat", new SignInProviderUpdate(ExternalSignInKinds.Discord, null, "client-id", true));
		var profile = await _providers.SaveAsync("chat", new SignInProviderUpdate(ExternalSignInKinds.Custom, "Chat", "client-id", true,
			Endpoints: Endpoints with { UserInfoUrl = "https://other.example.test/me" }));
		var field = await _providers.SaveAsync("chat", new SignInProviderUpdate(ExternalSignInKinds.Custom, "Chat", "client-id", true,
			Endpoints: Endpoints with { SubjectField = "sub" }));
		var renamed = await _providers.SaveAsync("chat", new SignInProviderUpdate(ExternalSignInKinds.Custom, "Chat room", "client-2", true,
			Endpoints: Endpoints with { Scopes = "identify" }));

		await Assert.That(kind is Error<string>).IsTrue();
		await Assert.That(profile is Error<string>).IsTrue();
		await Assert.That(field is Error<string>).IsTrue();
		await Assert.That(renamed.Expect<ExternalSignInProvider>().Name).IsEqualTo("Chat room");
	}

	private SignInProvidersController Controller() => new(_providers, _signIn, _secrets, Substitute.For<IAuditLog>())
	{
		ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
	};

	[Test]
	public async Task A_new_provider_can_use_a_secret_already_stored()
	{
		var saved = await Controller().Save("fresh", new SignInProviderUpdate(ExternalSignInKinds.Discord, null, "id", true));

		await Assert.That(saved.Result).IsTypeOf<OkObjectResult>();
		await _secrets.DidNotReceive().SetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<SecretAuthor>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task A_secret_is_not_stored_for_settings_that_are_refused()
	{
		var saved = await Controller().Save("chat", new SignInProviderUpdate(ExternalSignInKinds.Discord, null, "id", true,
			ClientSecret: "new-secret"));

		await Assert.That(saved.Result).IsTypeOf<BadRequestObjectResult>();
		await _secrets.DidNotReceive().SetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<SecretAuthor>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task Settings_are_not_saved_when_the_secret_is_refused()
	{
		_secrets.SetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<SecretAuthor>(), Arg.Any<CancellationToken>())
			.Returns(ValueTask.FromResult<Result<SecretStatus>>(new Error<string>("refused")));

		var saved = await Controller().Save("chat", new SignInProviderUpdate(ExternalSignInKinds.Custom, "Renamed", "client-id", true,
			Endpoints: Endpoints, ClientSecret: "new-secret"));

		await Assert.That(saved.Result).IsTypeOf<BadRequestObjectResult>();
		await Assert.That((await _providers.GetAsync("chat")).Expect<ExternalSignInProvider>().Name).IsEqualTo("Chat");
	}
}
