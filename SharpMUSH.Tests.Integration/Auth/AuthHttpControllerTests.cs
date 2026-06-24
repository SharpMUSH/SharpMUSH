using SharpMUSH.Tests.Infrastructure;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace SharpMUSH.Tests.Integration.Auth;

/// <summary>
/// HTTP-level integration tests for <c>AuthController</c> and <c>AccountController</c>:
/// registration, login, character creation, character switching (JWT), token refresh
/// (rotation + httpOnly cookie), and the link-existing-character flow.
///
/// The test server runs in the Development environment, where Startup generates an
/// ephemeral JWT signing key — so the jwt-* endpoints are fully operational.
/// </summary>
[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
public class AuthHttpControllerTests(ServerWebAppFactory factory)
{
	private record CharacterSummary(int DbrefNumber, long CreationTime, string Name, string Flags);
	private record AccountLoginResponse(string AccountId, string Username, List<CharacterSummary> Characters, string AccountSessionToken, bool MustChangePassword);
	private record JwtTokenResponse(string AccessToken, string RefreshToken, int ExpiresIn, string Role);
	private record CreatedCharacterResponse(int DbrefNumber, long CreationTime);
	private record MushTokenResponse(string Token, int ExpiresIn);

	private record AccountRegisterRequest(string Username, string? Email, string Password);
	private record AccountLoginRequest(string UsernameOrEmail, string Password);
	private record CreateCharacterRequest(string Name, string Password);
	private record LinkCharacterRequest(string CharacterName, string CharacterPassword);
	private record JwtSwitchCharacterRequest(string AccountSessionToken, int CharacterKey, long CharacterCreationTime);
	private record JwtRefreshRequest(string? RefreshToken);
	private record MushTokenRequest(string? PlayerName, string? Password, string? AccountSessionToken, int? CharacterKey, long? CharacterCreationTime);

	private const string Password = "Integration-Test-Pw-1!";

	/// <summary>
	/// Test client pinned to the https base address. The server uses UseHttpsRedirection;
	/// following the 307 from http→https makes HttpClient drop the Authorization header,
	/// which breaks every bearer-authenticated endpoint in these tests.
	/// </summary>
	private HttpClient CreateClient()
	{
		var http = factory.CreateHttpClient();
		http.BaseAddress = new Uri("https://localhost/");
		return http;
	}

	private static string UniqueName(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

	private async Task<(HttpClient Http, AccountLoginResponse Account)> RegisterAccountAsync()
	{
		var http = CreateClient();
		var response = await http.PostAsJsonAsync(
			"api/auth/account-register",
			new AccountRegisterRequest(UniqueName("acct"), null, Password));
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var account = await response.Content.ReadFromJsonAsync<AccountLoginResponse>();
		await Assert.That(account).IsNotNull();
		return (http, account!);
	}

	private static async Task<CreatedCharacterResponse> CreateCharacterAsync(
		HttpClient http, string sessionToken, string name, string password)
	{
		var request = new HttpRequestMessage(HttpMethod.Post, "api/account/characters")
		{
			Content = JsonContent.Create(new CreateCharacterRequest(name, password)),
		};
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessionToken);
		var response = await http.SendAsync(request);
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		return (await response.Content.ReadFromJsonAsync<CreatedCharacterResponse>())!;
	}

	[Test]
	public async Task AccountRegister_NewAccount_ReturnsSessionTokenAndEmptyCharacterList()
	{
		var (_, account) = await RegisterAccountAsync();

		await Assert.That(account.AccountSessionToken).IsNotEmpty();
		await Assert.That(account.Characters.Count).IsEqualTo(0);
	}

	[Test]
	public async Task AccountRegister_DuplicateUsername_Returns409()
	{
		var http = CreateClient();
		var username = UniqueName("dup");

		var first = await http.PostAsJsonAsync("api/auth/account-register",
			new AccountRegisterRequest(username, null, Password));
		await Assert.That(first.StatusCode).IsEqualTo(HttpStatusCode.OK);

		var second = await http.PostAsJsonAsync("api/auth/account-register",
			new AccountRegisterRequest(username, null, Password));
		await Assert.That(second.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
	}

	[Test]
	public async Task AccountLogin_WrongPassword_Returns401()
	{
		var (http, account) = await RegisterAccountAsync();

		var response = await http.PostAsJsonAsync("api/auth/account-login",
			new AccountLoginRequest(account.Username, "not-the-password"));

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
	}

	[Test]
	public async Task AccountLogin_CorrectPassword_ReturnsSessionAndCharacters()
	{
		var (http, account) = await RegisterAccountAsync();
		await CreateCharacterAsync(http, account.AccountSessionToken, UniqueName("Char"), Password);

		var response = await http.PostAsJsonAsync("api/auth/account-login",
			new AccountLoginRequest(account.Username, Password));

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var login = await response.Content.ReadFromJsonAsync<AccountLoginResponse>();
		await Assert.That(login!.AccountSessionToken).IsNotEmpty();
		// Character created above is visible without re-entering its password.
		await Assert.That(login.Characters.Count).IsEqualTo(1);
	}

	[Test]
	public async Task JwtSwitchCharacter_LinkedCharacter_IssuesTokenPairAndRefreshCookie()
	{
		var (http, account) = await RegisterAccountAsync();
		var character = await CreateCharacterAsync(http, account.AccountSessionToken, UniqueName("Switch"), Password);

		var response = await http.PostAsJsonAsync("api/auth/jwt-switch-character",
			new JwtSwitchCharacterRequest(account.AccountSessionToken, character.DbrefNumber, character.CreationTime));

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var tokens = await response.Content.ReadFromJsonAsync<JwtTokenResponse>();
		await Assert.That(tokens!.AccessToken).IsNotEmpty();
		await Assert.That(tokens.RefreshToken).IsNotEmpty();
		await Assert.That(tokens.Role).IsEqualTo("Player");

		var setCookies = response.Headers.GetValues("Set-Cookie").ToList();
		await Assert.That(setCookies.Any(c => c.StartsWith("sharpmush_refresh="))).IsTrue();
		await Assert.That(setCookies.First(c => c.StartsWith("sharpmush_refresh="))).Contains("httponly");
	}

	[Test]
	public async Task JwtSwitchCharacter_UnlinkedCharacter_Returns401()
	{
		var (http, account) = await RegisterAccountAsync();

		var response = await http.PostAsJsonAsync("api/auth/jwt-switch-character",
			new JwtSwitchCharacterRequest(account.AccountSessionToken, 999999, 0));

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
	}

	[Test]
	public async Task JwtRefresh_ValidToken_RotatesAndRejectsReuse()
	{
		var (http, account) = await RegisterAccountAsync();
		var character = await CreateCharacterAsync(http, account.AccountSessionToken, UniqueName("Refresh"), Password);

		var login = await http.PostAsJsonAsync("api/auth/jwt-switch-character",
			new JwtSwitchCharacterRequest(account.AccountSessionToken, character.DbrefNumber, character.CreationTime));
		var tokens = await login.Content.ReadFromJsonAsync<JwtTokenResponse>();

		var refresh = await http.PostAsJsonAsync("api/auth/jwt-refresh", new JwtRefreshRequest(tokens!.RefreshToken));
		await Assert.That(refresh.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var refreshed = await refresh.Content.ReadFromJsonAsync<JwtTokenResponse>();
		await Assert.That(refreshed!.RefreshToken).IsNotEqualTo(tokens.RefreshToken);

		// Reusing the consumed token must fail (single-use rotation).
		var reuse = await http.PostAsJsonAsync("api/auth/jwt-refresh", new JwtRefreshRequest(tokens.RefreshToken));
		await Assert.That(reuse.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
	}

	[Test]
	public async Task JwtRefresh_GarbageToken_Returns401()
	{
		var http = CreateClient();

		var response = await http.PostAsJsonAsync("api/auth/jwt-refresh",
			new JwtRefreshRequest("not-a-real-token"));

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
	}

	[Test]
	public async Task JwtRefresh_NoTokenNoCookie_Returns400()
	{
		var http = CreateClient();

		var response = await http.PostAsJsonAsync("api/auth/jwt-refresh", new JwtRefreshRequest((string?)null));

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
	}

	[Test]
	public async Task MushToken_ViaAccountSession_IssuesOttWithoutCharacterPassword()
	{
		var (http, account) = await RegisterAccountAsync();
		var character = await CreateCharacterAsync(http, account.AccountSessionToken, UniqueName("Ott"), Password);

		var response = await http.PostAsJsonAsync("api/auth/mush-token",
			new MushTokenRequest(null, null, account.AccountSessionToken, character.DbrefNumber, character.CreationTime));

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var ott = await response.Content.ReadFromJsonAsync<MushTokenResponse>();
		await Assert.That(ott!.Token).IsNotEmpty();
	}

	[Test]
	public async Task LinkCharacter_WrongPassword_Returns401()
	{
		var (http, accountA) = await RegisterAccountAsync();
		var name = UniqueName("LinkA");
		await CreateCharacterAsync(http, accountA.AccountSessionToken, name, Password);

		var (_, accountB) = await RegisterAccountAsync();
		var request = new HttpRequestMessage(HttpMethod.Post, "api/account/link-character")
		{
			Content = JsonContent.Create(new LinkCharacterRequest(name, "wrong-password")),
		};
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accountB.AccountSessionToken);

		var response = await http.SendAsync(request);
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
	}

	[Test]
	public async Task LinkCharacter_LinkedToAnotherAccount_Returns409()
	{
		var (http, accountA) = await RegisterAccountAsync();
		var name = UniqueName("LinkB");
		await CreateCharacterAsync(http, accountA.AccountSessionToken, name, Password);

		var (_, accountB) = await RegisterAccountAsync();
		var request = new HttpRequestMessage(HttpMethod.Post, "api/account/link-character")
		{
			Content = JsonContent.Create(new LinkCharacterRequest(name, Password)),
		};
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accountB.AccountSessionToken);

		var response = await http.SendAsync(request);
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
	}

	[Test]
	public async Task LinkCharacter_AfterUnlink_LinksToNewAccount()
	{
		var (http, accountA) = await RegisterAccountAsync();
		var name = UniqueName("LinkC");
		var character = await CreateCharacterAsync(http, accountA.AccountSessionToken, name, Password);

		var unlink = new HttpRequestMessage(HttpMethod.Delete, $"api/account/characters/{character.DbrefNumber}");
		unlink.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accountA.AccountSessionToken);
		var unlinkResponse = await http.SendAsync(unlink);
		await Assert.That(unlinkResponse.StatusCode).IsEqualTo(HttpStatusCode.NoContent);

		var (_, accountB) = await RegisterAccountAsync();
		var link = new HttpRequestMessage(HttpMethod.Post, "api/account/link-character")
		{
			Content = JsonContent.Create(new LinkCharacterRequest(name, Password)),
		};
		link.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accountB.AccountSessionToken);
		var linkResponse = await http.SendAsync(link);
		await Assert.That(linkResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);

		var list = new HttpRequestMessage(HttpMethod.Get, "api/account/characters");
		list.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accountB.AccountSessionToken);
		var listResponse = await http.SendAsync(list);
		var characters = await listResponse.Content.ReadFromJsonAsync<List<CharacterSummary>>();
		await Assert.That(characters!.Any(c => c.Name == name)).IsTrue();
	}
}
