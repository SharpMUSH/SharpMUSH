using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests.Infrastructure;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace SharpMUSH.Tests.Integration.Auth;

/// <summary>
/// HTTP-level integration tests verifying that a <c>MustChangePassword</c>-flagged account
/// session is only accepted by <c>PUT api/account/password</c> and <c>POST api/account/logout</c> —
/// every other <c>api/account/*</c> endpoint, plus the account-session paths of
/// <c>api/auth/mush-token</c> and <c>api/auth/jwt-switch-character</c>, must return 403.
/// </summary>
[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
public class MustChangePasswordTests(ServerWebAppFactory factory)
{
	private record CharacterSummary(int DbrefNumber, long CreationTime, string Name, string Flags);
	private record AccountLoginResponse(string AccountId, string Username, List<CharacterSummary> Characters, string AccountSessionToken, bool MustChangePassword);
	private record CreatedCharacterResponse(int DbrefNumber, long CreationTime);
	private record AccountRegisterRequest(string Username, string? Email, string Password);
	private record CreateCharacterRequest(string Name, string Password);
	private record ChangePasswordRequest(string OldPassword, string NewPassword);
	private record MushTokenWithAccountRequest(string? AccountSessionToken, int? CharacterKey, long? CharacterCreationTime);

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
	public async Task FlaggedAccount_CannotListCharacters_ButCanChangePassword()
	{
		var (http, account) = await RegisterAccountAsync();
		var accountService = factory.Services.GetRequiredService<IAccountService>();
		await accountService.ForcePasswordChangeAsync(account.AccountId);

		// Blocked endpoint
		var listRequest = new HttpRequestMessage(HttpMethod.Get, "api/account/characters");
		listRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account.AccountSessionToken);
		var listResponse = await http.SendAsync(listRequest);
		await Assert.That(listResponse.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);

		// Allowed endpoint
		var changeRequest = new HttpRequestMessage(HttpMethod.Put, "api/account/password")
		{
			Content = JsonContent.Create(new ChangePasswordRequest(Password, "brand-new-pass-1"))
		};
		changeRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account.AccountSessionToken);
		var changeResponse = await http.SendAsync(changeRequest);
		await Assert.That(changeResponse.StatusCode).IsEqualTo(HttpStatusCode.NoContent);

		// Unblocked afterwards
		var listAgain = new HttpRequestMessage(HttpMethod.Get, "api/account/characters");
		listAgain.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account.AccountSessionToken);
		await Assert.That((await http.SendAsync(listAgain)).StatusCode).IsEqualTo(HttpStatusCode.OK);
	}

	[Test]
	public async Task FlaggedAccount_CannotMintOtt()
	{
		var (http, account) = await RegisterAccountAsync();
		var charName = UniqueName("McpChar");
		var character = await CreateCharacterAsync(http, account.AccountSessionToken, charName, Password);
		var accountService = factory.Services.GetRequiredService<IAccountService>();
		await accountService.ForcePasswordChangeAsync(account.AccountId);

		var response = await http.PostAsJsonAsync("api/auth/mush-token",
			new MushTokenWithAccountRequest(account.AccountSessionToken, character.DbrefNumber, character.CreationTime));
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
	}

	[Test]
	public async Task FlaggedAccount_CannotSwitchCharacterViaJwt()
	{
		var (http, account) = await RegisterAccountAsync();
		var charName = UniqueName("JswChar");
		var character = await CreateCharacterAsync(http, account.AccountSessionToken, charName, Password);
		var accountService = factory.Services.GetRequiredService<IAccountService>();
		await accountService.ForcePasswordChangeAsync(account.AccountId);

		var response = await http.PostAsJsonAsync("api/auth/jwt-switch-character",
			new { AccountSessionToken = account.AccountSessionToken, CharacterKey = character.DbrefNumber, CharacterCreationTime = character.CreationTime });
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
	}

	[Test]
	public async Task FlaggedAccount_CanLogout()
	{
		var (http, account) = await RegisterAccountAsync();
		var accountService = factory.Services.GetRequiredService<IAccountService>();
		await accountService.ForcePasswordChangeAsync(account.AccountId);

		var logoutRequest = new HttpRequestMessage(HttpMethod.Post, "api/account/logout");
		logoutRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account.AccountSessionToken);
		var logoutResponse = await http.SendAsync(logoutRequest);
		await Assert.That(logoutResponse.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
	}
}
