using SharpMUSH.Tests.Infrastructure;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace SharpMUSH.Tests.Integration.Auth;

/// <summary>
/// "Remember me": a sign-in that asks for it leaves an HttpOnly cookie, and a tab with no session of its
/// own (opened after the last one closed, or idle past its session) trades it for one through
/// <c>api/auth/account-resume</c>. Signing out ends it.
/// </summary>
[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
public class RememberedLoginTests(ServerWebAppFactory factory)
{
	private record CharacterSummary(int DbrefNumber, long CreationTime, string Name, string Flags, bool IsActing);
	private record AccountLoginResponse(string AccountId, string Username, List<CharacterSummary> Characters, string AccountSessionToken, bool MustChangePassword);
	private record CreatedCharacterResponse(int DbrefNumber, long CreationTime, string Flags);

	private record AccountRegisterRequest(string Username, string? Email, string Password, bool RememberMe = false);
	private record AccountLoginRequest(string UsernameOrEmail, string Password, bool RememberMe);
	private record AccountResumeRequest(int? CharacterKey, long? CharacterCreationTime);
	private record CreateCharacterRequest(string Name, string Password);

	private const string Password = "Integration-Test-Pw-1!";
	private const string CookieName = "sharpmush_remember";

	private HttpClient CreateClient()
	{
		var http = factory.CreateHttpClient();
		http.BaseAddress = new Uri("https://localhost/");
		return http;
	}

	private static string UniqueName(string prefix) => $"{prefix[..Math.Min(prefix.Length, 4)]}{Guid.NewGuid():N}"[..15];

	private async Task<(HttpClient Http, AccountLoginResponse Account)> RegisterAccountAsync()
	{
		var http = CreateClient();
		var response = await http.PostAsJsonAsync("api/auth/account-register",
			new AccountRegisterRequest(UniqueName("rmbr"), null, Password));
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		return (http, (await response.Content.ReadFromJsonAsync<AccountLoginResponse>())!);
	}

	private static async Task<CreatedCharacterResponse> CreateCharacterAsync(HttpClient http, string sessionToken, string name)
	{
		using var request = new HttpRequestMessage(HttpMethod.Post, "api/account/characters")
		{
			Content = JsonContent.Create(new CreateCharacterRequest(name, Password)),
		};
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessionToken);
		var response = await http.SendAsync(request);
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		return (await response.Content.ReadFromJsonAsync<CreatedCharacterResponse>())!;
	}

	private static async Task<HttpResponseMessage> LoginAsync(HttpClient http, string username, bool rememberMe) =>
		await http.PostAsJsonAsync("api/auth/account-login", new AccountLoginRequest(username, Password, rememberMe));

	/// <summary>The remember-me <c>Set-Cookie</c> header on <paramref name="response"/>, or null.</summary>
	private static string? RememberCookieHeader(HttpResponseMessage response) =>
		response.Headers.TryGetValues("Set-Cookie", out var values)
			? values.FirstOrDefault(v => v.StartsWith(CookieName + "=", StringComparison.Ordinal))
			: null;

	private static string CookieValue(string setCookie) => setCookie[(CookieName.Length + 1)..].Split(';')[0];

	private static async Task<HttpResponseMessage> ResumeAsync(HttpClient http, string? cookie,
		AccountResumeRequest? body = null, string? bearer = null)
	{
		using var request = new HttpRequestMessage(HttpMethod.Post, "api/auth/account-resume")
		{
			Content = JsonContent.Create(body ?? new AccountResumeRequest(null, null)),
		};
		if (cookie is not null) request.Headers.Add("Cookie", $"{CookieName}={cookie}");
		if (bearer is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
		return await http.SendAsync(request);
	}

	private static async Task<HttpStatusCode> CharactersStatusAsync(HttpClient http, string bearer)
	{
		using var request = new HttpRequestMessage(HttpMethod.Get, "api/account/characters");
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
		return (await http.SendAsync(request)).StatusCode;
	}

	[Test]
	public async Task SigningInWithRememberMe_SetsAnHttpOnlyCookieForNinetyDays()
	{
		var (http, account) = await RegisterAccountAsync();

		var header = RememberCookieHeader(await LoginAsync(http, account.Username, rememberMe: true));

		await Assert.That(header).IsNotNull();
		var attributes = header!.ToLowerInvariant();
		await Assert.That(attributes).Contains("httponly");
		await Assert.That(attributes).Contains("secure");
		await Assert.That(attributes).Contains("samesite=strict");
		await Assert.That(attributes).Contains("path=/api");
		await Assert.That(attributes).Contains($"max-age={(int)TimeSpan.FromDays(90).TotalSeconds}");
	}

	[Test]
	public async Task SigningInWithoutRememberMe_SetsNoCookie()
	{
		var (http, account) = await RegisterAccountAsync();

		var response = await LoginAsync(http, account.Username, rememberMe: false);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		await Assert.That(RememberCookieHeader(response)).IsNull();
	}

	[Test]
	public async Task RegisteringWithRememberMe_SetsTheCookie()
	{
		var http = CreateClient();
		var response = await http.PostAsJsonAsync("api/auth/account-register",
			new AccountRegisterRequest(UniqueName("rmbr"), null, Password, RememberMe: true));

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		await Assert.That(RememberCookieHeader(response)).IsNotNull();
	}

	/// <summary>
	/// The tab session the cookie trades for is an ordinary bearer; the cookie's own token never is one,
	/// so a script that somehow learned it still could not call the API with it.
	/// </summary>
	[Test]
	public async Task Resume_TradesTheCookieForATabSession_AndTheCookieIsNotABearer()
	{
		var (http, account) = await RegisterAccountAsync();
		var cookie = CookieValue(RememberCookieHeader(await LoginAsync(http, account.Username, rememberMe: true))!);

		var response = await ResumeAsync(http, cookie);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var resumed = (await response.Content.ReadFromJsonAsync<AccountLoginResponse>())!;
		await Assert.That(resumed.AccountId).IsEqualTo(account.AccountId);
		await Assert.That(resumed.AccountSessionToken).IsNotEqualTo(cookie);
		await Assert.That(await CharactersStatusAsync(http, resumed.AccountSessionToken)).IsEqualTo(HttpStatusCode.OK);
		await Assert.That(await CharactersStatusAsync(http, cookie)).IsEqualTo(HttpStatusCode.Unauthorized);
		// Renewed for another 90 days, same token.
		await Assert.That(CookieValue(RememberCookieHeader(response)!)).IsEqualTo(cookie);
	}

	/// <summary>A tab session is not a remembered login either: only the cookie a sign-in set resumes.</summary>
	[Test]
	public async Task Resume_RefusesATabSessionInTheCookie()
	{
		var (http, account) = await RegisterAccountAsync();

		var response = await ResumeAsync(http, account.AccountSessionToken);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
	}

	[Test]
	public async Task Resume_WithoutTheCookie_IsRefused()
	{
		var http = CreateClient();

		await Assert.That((await ResumeAsync(http, cookie: null)).StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
	}

	/// <summary>
	/// A tab whose session ran out while it played its second character comes back as that character, not
	/// the account's first.
	/// </summary>
	[Test]
	public async Task Resume_BindsTheCharacterTheTabAskedFor()
	{
		var (http, account) = await RegisterAccountAsync();
		await CreateCharacterAsync(http, account.AccountSessionToken, UniqueName("One"));
		var second = await CreateCharacterAsync(http, account.AccountSessionToken, UniqueName("Two"));
		var cookie = CookieValue(RememberCookieHeader(await LoginAsync(http, account.Username, rememberMe: true))!);

		var response = await ResumeAsync(http, cookie, new AccountResumeRequest(second.DbrefNumber, second.CreationTime));

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var resumed = (await response.Content.ReadFromJsonAsync<AccountLoginResponse>())!;
		var acting = resumed.Characters.Single(c => c.IsActing);
		await Assert.That(acting.DbrefNumber).IsEqualTo(second.DbrefNumber);
	}

	[Test]
	public async Task SigningOut_EndsTheRememberedLogin()
	{
		var (http, account) = await RegisterAccountAsync();
		var login = await LoginAsync(http, account.Username, rememberMe: true);
		var cookie = CookieValue(RememberCookieHeader(login)!);
		var session = (await login.Content.ReadFromJsonAsync<AccountLoginResponse>())!.AccountSessionToken;

		using var logout = new HttpRequestMessage(HttpMethod.Post, "api/account/logout");
		logout.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session);
		logout.Headers.Add("Cookie", $"{CookieName}={cookie}");
		var loggedOut = await http.SendAsync(logout);

		await Assert.That(loggedOut.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
		await Assert.That(RememberCookieHeader(loggedOut)!.ToLowerInvariant()).Contains("expires=thu, 01 jan 1970");
		await Assert.That((await ResumeAsync(http, cookie)).StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
	}
}
