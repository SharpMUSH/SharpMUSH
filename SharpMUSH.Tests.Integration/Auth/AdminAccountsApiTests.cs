using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests.Infrastructure;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace SharpMUSH.Tests.Integration.Auth;

/// <summary>
/// HTTP-level integration tests for <c>AdminAccountsController</c> (Wizard-only account
/// management) and the account-login/register role/permissions payload.
///
/// <c>GodAccount_CanListAndResetPassword</c> claims the God-linked bootstrap admin's password
/// directly (bypassing <c>SetupController</c>) and forces <c>ServerState.SetupCompleted = true</c>.
/// Because it mutates the same shared, session-wide game state as <c>SetupFlowTests</c>
/// (ServerState.SetupCompleted, and the #1-linked account's password), it shares that class's
/// <c>NotInParallel("SetupFlow", Order = N)</c> constraint group rather than a separate
/// "AdminAccounts" group — two different NotInParallel group names are independent constraint
/// domains in TUnit and would still be free to interleave with each other, which would race the
/// SetupCompleted flag (SetupFlowTests flips it false/true repeatedly) and, worse, would race
/// which suite is first to touch the bootstrap admin's empty password hash.
///
/// Order = 6 places these tests strictly after SetupFlowTests' Order = 1..5 (and after
/// <c>BootstrapTests</c>' Order = 0, which asserts the admin's password hash is still empty pre
/// claim — the one assertion in this suite of classes that genuinely needs to see the pristine
/// state). SetupFlowTests' last ordered test (Order = 5) always leaves the game claimed
/// (<c>SetServerSetupCompletedAsync(true)</c>) with SetupFlowTests' own credentials on the admin
/// account, so <c>LoginAsGodAccountAsync</c> below unconditionally resets the password to a known
/// value before logging in. TUnit's <c>ConstraintKeyScheduler</c> sorts tests sharing a
/// constraint key by <c>Order</c> (ties broken by discovery order, which is what made Order = 0
/// here collide non-deterministically with <c>BootstrapTests</c>' Order = 0 previously), so
/// distinct Order values across the whole "SetupFlow" group are required for determinism.
/// </summary>
[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
public class AdminAccountsApiTests(ServerWebAppFactory factory)
{
	private record CharacterSummary(int DbrefNumber, long CreationTime, string Name, string Flags);
	private record AccountLoginResponse(string AccountId, string Username, List<CharacterSummary> Characters,
		string AccountSessionToken, bool MustChangePassword, string Role, IReadOnlyList<string> Permissions);

	private record AccountRegisterRequest(string Username, string? Email, string Password);
	private record AccountLoginRequest(string UsernameOrEmail, string Password);
	private record AdminAccountRow(string Id, string Username, string? Email, bool IsDisabled, bool MustChangePassword);
	private record CreateCharacterRequest(string Name, string Password);
	private record CreatedCharacterResponse(int DbrefNumber, long CreationTime);

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

	private async Task<(HttpClient Http, string SessionToken)> LoginAsGodAccountAsync()
	{
		var http = CreateClient();
		var db = factory.Services.GetRequiredService<ISharpDatabase>();
		var accountService = factory.Services.GetRequiredService<IAccountService>();
		var admin = await accountService.GetAccountForCharacterAsync(new DBRef(1));
		// Always (re)set the password: these tests run at Order = 6, after SetupFlowTests has
		// claimed the admin account with its own credentials (headwiz/racer-*), so a
		// "set only if unclaimed" check would leave us logging in with the wrong password.
		await accountService.SetPasswordAsync(admin!.Id!, "god-admin-pass-1", mustChangePassword: false);
		await db.SetServerSetupCompletedAsync(true);

		var login = await http.PostAsJsonAsync("api/auth/account-login",
			new AccountLoginRequest(admin.Username, "god-admin-pass-1"));
		await Assert.That(login.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var body = await login.Content.ReadFromJsonAsync<AccountLoginResponse>();
		return (http, body!.AccountSessionToken);
	}

	/// <summary>Looks up an account's admin-route key (God session required) by username.</summary>
	private static async Task<string> GetAdminKeyAsync(HttpClient godHttp, string godSessionToken, string username)
	{
		using var request = new HttpRequestMessage(HttpMethod.Get, $"api/admin/accounts?search={username}");
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", godSessionToken);
		using var response = await godHttp.SendAsync(request);
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var rows = await response.Content.ReadFromJsonAsync<List<AdminAccountRow>>();
		return rows!.Single(r => r.Username == username).Id;
	}

	private static HttpRequestMessage AsGod(HttpRequestMessage request, string godSessionToken)
	{
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", godSessionToken);
		return request;
	}

	private async Task<CreatedCharacterResponse> CreateCharacterAsync(HttpClient http, string sessionToken, string name)
	{
		using var request = new HttpRequestMessage(HttpMethod.Post, "api/account/characters")
		{
			Content = JsonContent.Create(new CreateCharacterRequest(name, Password)),
		};
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessionToken);
		using var response = await http.SendAsync(request);
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		return (await response.Content.ReadFromJsonAsync<CreatedCharacterResponse>())!;
	}

	[Test, NotInParallel("AdminAccounts")]
	public async Task List_RequiresWizardRole()
	{
		var (http, account) = await RegisterAccountAsync(); // plain player account
		using var request = new HttpRequestMessage(HttpMethod.Get, "api/admin/accounts");
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account.AccountSessionToken);
		using var response = await http.SendAsync(request);
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
	}

	[Test, NotInParallel("SetupFlow", Order = 6)]
	public async Task GodAccount_CanListAndResetPassword()
	{
		var (http, sessionToken) = await LoginAsGodAccountAsync();
		var (_, target) = await RegisterAccountAsync();

		using var listRequest = new HttpRequestMessage(HttpMethod.Get, "api/admin/accounts");
		listRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessionToken);
		using var listResponse = await http.SendAsync(listRequest);
		await Assert.That(listResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var rows = await listResponse.Content.ReadFromJsonAsync<List<AdminAccountRow>>();
		var targetRow = rows!.Single(r => r.Username == target.Username);

		using var resetRequest = new HttpRequestMessage(HttpMethod.Post, $"api/admin/accounts/{targetRow.Id}/reset-password")
		{
			Content = JsonContent.Create(new { NewPassword = "admin-reset-pass-1" })
		};
		resetRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessionToken);
		using var resetResponse = await http.SendAsync(resetRequest);
		await Assert.That(resetResponse.StatusCode).IsEqualTo(HttpStatusCode.NoContent);

		var accountService = factory.Services.GetRequiredService<IAccountService>();
		var authenticated = await accountService.AuthenticateAsync(target.Username, "admin-reset-pass-1");
		await Assert.That(authenticated!.MustChangePassword).IsTrue();
	}

	[Test]
	public async Task AccountLogin_ReturnsRoleAndPermissions()
	{
		var (http, account) = await RegisterAccountAsync();
		using var response = await http.PostAsJsonAsync("api/auth/account-login",
			new AccountLoginRequest(account.Username, Password));
		var body = await response.Content.ReadFromJsonAsync<AccountLoginResponse>();
		await Assert.That(body!.Role).IsEqualTo("Guest"); // no characters yet → Guest
	}

	/// <summary>
	/// Shares <c>GodAccount_CanListAndResetPassword</c>'s <c>NotInParallel("SetupFlow", Order = 6)</c>
	/// group for the same reason documented on the class: it logs in as the God-linked bootstrap
	/// admin, which mutates the shared #1-linked account's password/SetupCompleted state.
	/// </summary>
	[Test, NotInParallel("SetupFlow", Order = 6)]
	public async Task Disable_Enable_RoundTrip_BlocksThenRestoresLogin()
	{
		var (godHttp, godSessionToken) = await LoginAsGodAccountAsync();
		var (_, target) = await RegisterAccountAsync();
		var key = await GetAdminKeyAsync(godHttp, godSessionToken, target.Username);

		using var disableRequest = AsGod(new HttpRequestMessage(HttpMethod.Post, $"api/admin/accounts/{key}/disable"), godSessionToken);
		using var disableResponse = await godHttp.SendAsync(disableRequest);
		await Assert.That(disableResponse.StatusCode).IsEqualTo(HttpStatusCode.NoContent);

		using var blockedLogin = await CreateClient().PostAsJsonAsync("api/auth/account-login",
			new AccountLoginRequest(target.Username, Password));
		await Assert.That(blockedLogin.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);

		using var enableRequest = AsGod(new HttpRequestMessage(HttpMethod.Post, $"api/admin/accounts/{key}/enable"), godSessionToken);
		using var enableResponse = await godHttp.SendAsync(enableRequest);
		await Assert.That(enableResponse.StatusCode).IsEqualTo(HttpStatusCode.NoContent);

		using var restoredLogin = await CreateClient().PostAsJsonAsync("api/auth/account-login",
			new AccountLoginRequest(target.Username, Password));
		await Assert.That(restoredLogin.StatusCode).IsEqualTo(HttpStatusCode.OK);
	}

	[Test, NotInParallel("SetupFlow", Order = 6)]
	public async Task Disable_RevokesTargetsExistingSession()
	{
		var (godHttp, godSessionToken) = await LoginAsGodAccountAsync();
		var (targetHttp, target) = await RegisterAccountAsync();

		using var targetLogin = await targetHttp.PostAsJsonAsync("api/auth/account-login",
			new AccountLoginRequest(target.Username, Password));
		await Assert.That(targetLogin.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var targetSessionToken = (await targetLogin.Content.ReadFromJsonAsync<AccountLoginResponse>())!.AccountSessionToken;

		var key = await GetAdminKeyAsync(godHttp, godSessionToken, target.Username);
		using var disableRequest = AsGod(new HttpRequestMessage(HttpMethod.Post, $"api/admin/accounts/{key}/disable"), godSessionToken);
		using var disableResponse = await godHttp.SendAsync(disableRequest);
		await Assert.That(disableResponse.StatusCode).IsEqualTo(HttpStatusCode.NoContent);

		using var charsRequest = new HttpRequestMessage(HttpMethod.Get, "api/account/characters");
		charsRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", targetSessionToken);
		using var charsResponse = await targetHttp.SendAsync(charsRequest);
		await Assert.That(charsResponse.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
	}

	[Test, NotInParallel("SetupFlow", Order = 6)]
	public async Task ResetPassword_RevokesTargetsExistingSession()
	{
		var (godHttp, godSessionToken) = await LoginAsGodAccountAsync();
		var (targetHttp, target) = await RegisterAccountAsync();

		using var targetLogin = await targetHttp.PostAsJsonAsync("api/auth/account-login",
			new AccountLoginRequest(target.Username, Password));
		await Assert.That(targetLogin.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var targetSessionToken = (await targetLogin.Content.ReadFromJsonAsync<AccountLoginResponse>())!.AccountSessionToken;

		var key = await GetAdminKeyAsync(godHttp, godSessionToken, target.Username);
		using var resetRequest = new HttpRequestMessage(HttpMethod.Post, $"api/admin/accounts/{key}/reset-password")
		{
			Content = JsonContent.Create(new { NewPassword = "admin-reset-pass-2" })
		};
		AsGod(resetRequest, godSessionToken);
		using var resetResponse = await godHttp.SendAsync(resetRequest);
		await Assert.That(resetResponse.StatusCode).IsEqualTo(HttpStatusCode.NoContent);

		// Revoked, not merely flagged: the old session token itself must now be rejected (401),
		// not accepted-but-forbidden pending a password change (403).
		using var charsRequest = new HttpRequestMessage(HttpMethod.Get, "api/account/characters");
		charsRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", targetSessionToken);
		using var charsResponse = await targetHttp.SendAsync(charsRequest);
		await Assert.That(charsResponse.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
	}

	[Test, NotInParallel("SetupFlow", Order = 6)]
	public async Task UnlinkCharacter_RemovesItFromTargetsCharacterList()
	{
		var (godHttp, godSessionToken) = await LoginAsGodAccountAsync();
		var (targetHttp, target) = await RegisterAccountAsync();
		var character = await CreateCharacterAsync(targetHttp, target.AccountSessionToken, UniqueName("Unlink"));

		var key = await GetAdminKeyAsync(godHttp, godSessionToken, target.Username);
		using var unlinkRequest = AsGod(
			new HttpRequestMessage(HttpMethod.Delete, $"api/admin/accounts/{key}/characters/{character.DbrefNumber}"),
			godSessionToken);
		using var unlinkResponse = await godHttp.SendAsync(unlinkRequest);
		await Assert.That(unlinkResponse.StatusCode).IsEqualTo(HttpStatusCode.NoContent);

		// Re-authenticate (fresh session) rather than reusing the registration token, so the
		// character list reflects the account's current state end-to-end through account-login.
		using var freshLogin = await targetHttp.PostAsJsonAsync("api/auth/account-login",
			new AccountLoginRequest(target.Username, Password));
		await Assert.That(freshLogin.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var body = await freshLogin.Content.ReadFromJsonAsync<AccountLoginResponse>();
		await Assert.That(body!.Characters.Any(c => c.DbrefNumber == character.DbrefNumber)).IsFalse();
	}
}
