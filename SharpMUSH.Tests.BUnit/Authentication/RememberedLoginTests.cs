using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Bunit;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using NSubstitute;
using SharpMUSH.Client.Services;
using SharpMUSH.Tests.Shared;

namespace SharpMUSH.Tests.BUnit.Authentication;

/// <summary>
/// Fakes <c>POST api/auth/account-resume</c> (the browser's remembered login; answers 401 when
/// <see cref="Remembered"/> is false) and <c>GET api/account/session</c> (refuses every token but the
/// resumed one), and records what each resume asked for.
/// </summary>
internal sealed class RememberedLoginHandler : HttpMessageHandler
{
	public bool Remembered { get; set; } = true;
	public int Resumes { get; private set; }
	public string? LastResumeBody { get; private set; }
	public AuthenticationHeaderValue? LastResumeAuthorization { get; private set; }

	protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		switch (request.RequestUri!.AbsolutePath)
		{
			case "/api/auth/account-resume":
				Resumes++;
				LastResumeBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
				LastResumeAuthorization = request.Headers.Authorization;
				if (!Remembered) return new HttpResponseMessage(HttpStatusCode.Unauthorized);
				return new HttpResponseMessage(HttpStatusCode.OK)
				{
					Content = JsonContent.Create(new
					{
						accountId = "acct-1",
						username = "headwiz",
						characters = new[] { new { dbrefNumber = 7, creationTime = 70L, name = "Bob", flags = "PLAYER", isActing = true } },
						accountSessionToken = "resumed-token",
						mustChangePassword = false,
						role = "Player",
						permissions = Array.Empty<string>(),
					})
				};
			case "/api/account/session" when request.Headers.Authorization?.Parameter == "resumed-token":
				return new HttpResponseMessage(HttpStatusCode.OK)
				{
					Content = JsonContent.Create(new { username = "headwiz", mustChangePassword = false, role = "Player", permissions = Array.Empty<string>() })
				};
			case "/api/account/session":
				return new HttpResponseMessage(HttpStatusCode.Unauthorized);
			default:
				return new HttpResponseMessage(HttpStatusCode.NotFound);
		}
	}
}

/// <summary>
/// "Remember me": a tab with no session of its own, or one whose session ran out, signs in from the
/// browser's remembered login instead of showing the login page.
/// </summary>
public class RememberedLoginTests : TrackingBunitContext
{
	private AccountAuthService Create(RememberedLoginHandler handler, string? storedToken, bool loggedOut = false)
	{
		JSInterop.Mode = JSRuntimeMode.Loose;
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.loggedOut").SetResult(loggedOut ? bool.TrueString : null);
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.sessionToken").SetResult(storedToken);
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.username").SetResult(storedToken is null ? null : "headwiz");

		var http = Track(new HttpClient(handler) { BaseAddress = new Uri("https://localhost:8081/") });
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(http);
		return new AccountAuthService(factory, JSInterop.JSRuntime, NullLogger<AccountAuthService>.Instance, []);
	}

	[Test]
	public async Task ANewTab_SignsInFromTheRememberedLogin()
	{
		var handler = new RememberedLoginHandler();
		var service = Create(handler, storedToken: null);

		await service.InitAsync();

		await Assert.That(service.IsLoggedIn).IsTrue();
		await Assert.That(service.AccountSessionToken).IsEqualTo("resumed-token");
		await Assert.That(service.Username).IsEqualTo("headwiz");
		await Assert.That(service.ActiveCharacter?.Name).IsEqualTo("Bob");
		await Assert.That(handler.LastResumeAuthorization).IsNull();
		await Assert.That(JSInterop.Invocations.Any(i => i.Identifier == "sessionStorage.setItem"
			&& i.Arguments.SequenceEqual(new object?[] { "sharpmush.account.sessionToken", "resumed-token" }))).IsTrue()
			.Because("the tab keeps its new session like any sign-in, so a reload does not ask again");
	}

	[Test]
	public async Task ATabWhoseSessionRanOut_SignsInFromTheRememberedLogin()
	{
		var handler = new RememberedLoginHandler();
		var service = Create(handler, storedToken: "expired-token");

		await service.InitAsync();

		await Assert.That(service.AccountSessionToken).IsEqualTo("resumed-token");
		await Assert.That(service.Role).IsEqualTo("Player");
	}

	[Test]
	public async Task WithNoRememberedLogin_ANewTabStaysSignedOut()
	{
		var handler = new RememberedLoginHandler { Remembered = false };
		var service = Create(handler, storedToken: null);

		await service.InitAsync();

		await Assert.That(service.IsLoggedIn).IsFalse();
		await Assert.That(handler.Resumes).IsEqualTo(1);
	}

	/// <summary>An explicit sign-out in this tab is not undone by a cookie another tab may still hold.</summary>
	[Test]
	public async Task AfterAnExplicitSignOut_TheTabDoesNotResume()
	{
		var handler = new RememberedLoginHandler();
		var service = Create(handler, storedToken: null, loggedOut: true);

		await service.InitAsync();

		await Assert.That(service.IsLoggedIn).IsFalse();
		await Assert.That(handler.Resumes).IsEqualTo(0);
	}

	/// <summary>
	/// A tab left idle past its session keeps its character: the renewal asks for the one it was playing,
	/// and every request that failed on the old token shares the one renewal.
	/// </summary>
	[Test]
	public async Task Renewing_AsksForTheTabsCharacter_OnceForEveryoneRefused()
	{
		var handler = new RememberedLoginHandler();
		var service = Create(handler, storedToken: null);
		await service.InitAsync();
		var refused = service.AccountSessionToken!;
		var resumesBefore = handler.Resumes;

		var renewals = await Task.WhenAll(service.RenewSessionAsync(refused), service.RenewSessionAsync(refused));

		await Assert.That(handler.Resumes - resumesBefore).IsEqualTo(1);
		await Assert.That(renewals[0]).IsEqualTo("resumed-token");
		await Assert.That(renewals[1]).IsEqualTo("resumed-token");
		await Assert.That(handler.LastResumeBody!).Contains("\"characterKey\":7");
		await Assert.That(handler.LastResumeBody!).Contains("\"characterCreationTime\":70");
	}

	[Test]
	public async Task Renewing_ATokenTheTabAlreadyReplaced_AnswersWithTheCurrentOne()
	{
		var handler = new RememberedLoginHandler();
		var service = Create(handler, storedToken: null);
		await service.InitAsync();
		var resumesBefore = handler.Resumes;

		await Assert.That(await service.RenewSessionAsync("some-older-token")).IsEqualTo("resumed-token");
		await Assert.That(handler.Resumes).IsEqualTo(resumesBefore);
	}

	/// <summary>Without a remembered login every refused request would ask again; one refusal is enough.</summary>
	[Test]
	public async Task Renewing_WhenTheRememberedLoginIsGone_IsNotTriedAgainForThatToken()
	{
		var handler = new RememberedLoginHandler();
		var service = Create(handler, storedToken: null);
		await service.InitAsync();
		var refused = service.AccountSessionToken!;
		handler.Remembered = false;
		var resumesBefore = handler.Resumes;

		await Assert.That(await service.RenewSessionAsync(refused)).IsNull();
		await Assert.That(await service.RenewSessionAsync(refused)).IsNull();

		await Assert.That(handler.Resumes - resumesBefore).IsEqualTo(1);
	}
}

/// <summary>
/// The bearer handler sends a request refused on an expired session once more, with the renewed bearer
/// and the same body.
/// </summary>
public class BearerRenewalTests
{
	private sealed class RefusesStaleBearer : HttpMessageHandler
	{
		public List<(string? Bearer, string? Body)> Seen { get; } = [];
		public List<HttpRequestMessage> Requests { get; } = [];

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			Requests.Add(request);
			var bearer = request.Headers.Authorization?.Parameter;
			Seen.Add((bearer, request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));
			return new HttpResponseMessage(bearer == "fresh" ? HttpStatusCode.OK : HttpStatusCode.Unauthorized);
		}
	}

	private static HttpClient Client(FakeAccountAuthState auth, HttpMessageHandler server) =>
		new(new AccountSessionBearerHandler(auth) { InnerHandler = server }) { BaseAddress = new Uri("https://localhost/") };

	[Test]
	public async Task ARefusedRequest_IsSentAgainWithTheRenewedBearerAndTheSameBody()
	{
		var auth = new FakeAccountAuthState { AccountSessionToken = "stale", RenewedToken = "fresh" };
		var server = new RefusesStaleBearer();
		using var http = Client(auth, server);

		var response = await http.PostAsJsonAsync("api/commands", new { command = "look" });

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		await Assert.That(server.Seen.Select(s => s.Bearer)).IsEquivalentTo(new[] { "stale", "fresh" });
		await Assert.That(server.Seen[1].Body).IsEqualTo(server.Seen[0].Body);
		await Assert.That(server.Seen[1].Body!).Contains("look");
	}

	[Test]
	public async Task WithNothingToRenewTo_TheRefusalStands()
	{
		var auth = new FakeAccountAuthState { AccountSessionToken = "stale", RenewedToken = null };
		var server = new RefusesStaleBearer();
		using var http = Client(auth, server);

		var response = await http.GetAsync("api/account/characters");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
		await Assert.That(server.Seen.Count).IsEqualTo(1);
	}

	[Test]
	public async Task AnAnonymousRequest_CarriesNoBearerAndIsNotRetried()
	{
		var auth = new FakeAccountAuthState { AccountSessionToken = "stale", RenewedToken = "fresh" };
		var server = new RefusesStaleBearer();
		using var http = Client(auth, server);
		using var request = new HttpRequestMessage(HttpMethod.Post, "api/auth/account-resume");
		request.Options.Set(AccountSessionBearerHandler.Anonymous, true);

		var response = await http.SendAsync(request);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
		await Assert.That(server.Seen.Select(s => s.Bearer)).IsEquivalentTo(new string?[] { null });
	}

	/// <summary>
	/// The remembered login is a cookie; a fetch left at its same-origin default neither keeps nor sends it
	/// when the API is on another origin than the page, as under the standalone client dev server.
	/// </summary>
	[Test]
	public async Task EveryRequest_AsksTheBrowserToIncludeCookies_TheRetryToo()
	{
		var auth = new FakeAccountAuthState { AccountSessionToken = "stale", RenewedToken = "fresh" };
		var server = new RefusesStaleBearer();
		using var http = Client(auth, server);
		using var anonymous = new HttpRequestMessage(HttpMethod.Post, "api/auth/account-resume");
		anonymous.Options.Set(AccountSessionBearerHandler.Anonymous, true);

		await http.SendAsync(anonymous);
		await http.GetAsync("api/account/characters");

		await Assert.That(server.Requests.Count).IsEqualTo(3);
		foreach (var request in server.Requests)
			await Assert.That(FetchCredentials(request)).IsEqualTo("include");
	}

	private static object? FetchCredentials(HttpRequestMessage request) =>
		request.Options.TryGetValue(new HttpRequestOptionsKey<IDictionary<string, object>>("WebAssemblyFetchOptions"), out var fetch)
		&& fetch.TryGetValue("credentials", out var credentials)
			? credentials
			: null;
}

/// <summary>The passkey button signs in with the same Remember me choice as the password one.</summary>
public class PasskeyRememberMeTests
{
	/// <summary>Empty storage, and a passkey prompt that answers with a credential.</summary>
	private sealed class PasskeyBrowser : IJSRuntime
	{
		public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
			InvokeAsync<TValue>(identifier, CancellationToken.None, args);

		public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
			ValueTask.FromResult(identifier == "SharpMUSH.Passkeys.get"
				? JsonSerializer.Deserialize<TValue>("""{"credential":"{\"id\":\"cred\"}","error":null,"cancelled":false}""",
					JsonSerializerOptions.Web)!
				: default!);
	}

	private sealed class PasskeyServer : HttpMessageHandler
	{
		public string? LoginBody { get; private set; }

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			switch (request.RequestUri!.AbsolutePath)
			{
				case "/api/auth/passkey-login/options":
					return new HttpResponseMessage(HttpStatusCode.OK)
					{
						Content = JsonContent.Create(new { ceremonyId = "ceremony-1", options = new { challenge = "abc" } })
					};
				case "/api/auth/passkey-login":
					LoginBody = await request.Content!.ReadAsStringAsync(cancellationToken);
					return new HttpResponseMessage(HttpStatusCode.OK)
					{
						Content = JsonContent.Create(new
						{
							accountId = "acct-1",
							username = "headwiz",
							characters = Array.Empty<object>(),
							accountSessionToken = "passkey-token",
							mustChangePassword = false,
							role = "Player",
							permissions = Array.Empty<string>(),
						})
					};
				default:
					return new HttpResponseMessage(HttpStatusCode.Unauthorized);
			}
		}
	}

	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async Task APasskeySignIn_CarriesTheRememberMeChoice(bool rememberMe)
	{
		var server = new PasskeyServer();
		using var http = new HttpClient(server) { BaseAddress = new Uri("https://localhost:8081/") };
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(http);
		var service = new AccountAuthService(factory, new PasskeyBrowser(), NullLogger<AccountAuthService>.Instance, []);

		var outcome = await service.LoginWithPasskeyAsync(rememberMe);

		await Assert.That(outcome is IReadOnlyList<AccountAuthService.CharacterSummary>).IsTrue();
		await Assert.That(server.LoginBody!).Contains($"\"rememberMe\":{(rememberMe ? "true" : "false")}");
	}
}
