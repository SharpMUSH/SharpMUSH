using SharpMUSH.Tests.Infrastructure;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace SharpMUSH.Tests.Integration.Auth;

/// <summary>
/// HTTP-level integration tests for the login matrix in <c>AccountService.AuthenticateAsync</c>:
/// username/email + account password, username/email + any linked character's password, and
/// character-name identifier + that character's password resolving to the owning account.
/// </summary>
[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
public class LoginMatrixTests(ServerWebAppFactory factory)
{
	private record CharacterSummary(int DbrefNumber, long CreationTime, string Name, string Flags);
	private record AccountLoginResponse(string AccountId, string Username, List<CharacterSummary> Characters, string AccountSessionToken, bool MustChangePassword);

	private record AccountRegisterRequest(string Username, string? Email, string Password);
	private record AccountLoginRequest(string UsernameOrEmail, string Password);
	private record CreateCharacterRequest(string Name, string Password);
	private record CreatedCharacterResponse(int DbrefNumber, long CreationTime);

	private const string AccountPassword = "account-pass-123";
	private const string CharPassword = "char-pass-456";

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
			new AccountRegisterRequest(UniqueName("acct"), null, AccountPassword));
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
	public async Task Login_WithLinkedCharacterPassword_Succeeds()
	{
		var (http, account) = await RegisterAccountAsync(); // registers with AccountPassword
		var charName = UniqueName("MatrixChar");
		await CreateCharacterAsync(http, account.AccountSessionToken, charName, CharPassword);

		var response = await http.PostAsJsonAsync("api/auth/account-login",
			new AccountLoginRequest(account.Username, CharPassword));

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
	}

	[Test]
	public async Task Login_WithCharacterNameAndPassword_ResolvesOwningAccount()
	{
		var (http, account) = await RegisterAccountAsync();
		var charName = UniqueName("MatrixChar");
		await CreateCharacterAsync(http, account.AccountSessionToken, charName, CharPassword);

		var response = await http.PostAsJsonAsync("api/auth/account-login",
			new AccountLoginRequest(charName, CharPassword));

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var login = await response.Content.ReadFromJsonAsync<AccountLoginResponse>();
		await Assert.That(login!.Username).IsEqualTo(account.Username);
	}

	[Test]
	public async Task Login_CharacterName_WrongPassword_Fails()
	{
		var (http, account) = await RegisterAccountAsync();
		var charName = UniqueName("MatrixChar");
		await CreateCharacterAsync(http, account.AccountSessionToken, charName, CharPassword);

		var response = await http.PostAsJsonAsync("api/auth/account-login",
			new AccountLoginRequest(charName, AccountPassword)); // account pw is NOT valid via char-name identifier

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
	}

	[Test]
	[Skip("Enabled by Task 6")]
	public async Task Login_EmptyHashAccount_NeverMatches()
	{
		// The bootstrap admin account has an empty hash; empty-string password must not open it.
		var http = CreateClient();
		var response = await http.PostAsJsonAsync("api/auth/account-login",
			new AccountLoginRequest("admin", ""));
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest); // empty pw rejected by validation

		var response2 = await http.PostAsJsonAsync("api/auth/account-login",
			new AccountLoginRequest("admin", "anything"));
		await Assert.That(response2.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
	}
}
