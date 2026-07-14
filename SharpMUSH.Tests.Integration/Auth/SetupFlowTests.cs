using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests.Infrastructure;
using System.Net;
using System.Net.Http.Json;

namespace SharpMUSH.Tests.Integration.Auth;

/// <summary>
/// HTTP-level integration tests for the first-run setup wizard (<c>SetupController</c>).
///
/// All tests share and mutate the single game-wide ServerState document — strict ordering
/// via <c>NotInParallel("SetupFlow", Order = N)</c> is required so they don't race each other
/// (or other suites that also flip <c>SetupCompleted</c>).
/// </summary>
[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
public class SetupFlowTests(ServerWebAppFactory factory)
{
	private record CharacterSummary(int DbrefNumber, long CreationTime, string Name, string Flags);
	private record AccountLoginResponse(string AccountId, string Username, List<CharacterSummary> Characters, string AccountSessionToken, bool MustChangePassword);

	private record AccountRegisterRequest(string Username, string? Email, string Password);
	private record AccountLoginRequest(string UsernameOrEmail, string Password);

	private record SetupStatusResponse(bool NeedsSetup);
	private record SetupCompleteRequest(string Username, string Password);

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

	// All setup tests share and mutate the single ServerState doc — strict ordering.

	[Test, NotInParallel("SetupFlow", Order = 1)]
	public async Task Status_FreshGame_NeedsSetup()
	{
		var db = factory.Services.GetRequiredService<ISharpDatabase>();
		await db.SetServerSetupCompletedAsync(false);

		var http = CreateClient();
		var status = await http.GetFromJsonAsync<SetupStatusResponse>("api/setup/status");
		await Assert.That(status!.NeedsSetup).IsTrue();
	}

	[Test, NotInParallel("SetupFlow", Order = 2)]
	public async Task Complete_ClaimsAdminAccount_AndLoginWorks()
	{
		var http = CreateClient();
		var response = await http.PostAsJsonAsync("api/setup/complete",
			new SetupCompleteRequest("headwiz", "claimed-password-1"));
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

		var status = await http.GetFromJsonAsync<SetupStatusResponse>("api/setup/status");
		await Assert.That(status!.NeedsSetup).IsFalse();

		var login = await http.PostAsJsonAsync("api/auth/account-login",
			new AccountLoginRequest("headwiz", "claimed-password-1"));
		await Assert.That(login.StatusCode).IsEqualTo(HttpStatusCode.OK);
	}

	[Test, NotInParallel("SetupFlow", Order = 3)]
	public async Task Complete_SecondClaim_Returns409()
	{
		var http = CreateClient();
		var response = await http.PostAsJsonAsync("api/setup/complete",
			new SetupCompleteRequest("sneaky", "other-password-2"));
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
	}

	[Test, NotInParallel("SetupFlow", Order = 4)]
	public async Task Complete_ConcurrentClaims_ExactlyOneWins()
	{
		var db = factory.Services.GetRequiredService<ISharpDatabase>();
		await db.SetServerSetupCompletedAsync(false);

		var http = CreateClient();
		var first = http.PostAsJsonAsync("api/setup/complete", new SetupCompleteRequest("racer-one", "race-password-1"));
		var second = http.PostAsJsonAsync("api/setup/complete", new SetupCompleteRequest("racer-two", "race-password-2"));
		var responses = await Task.WhenAll(first, second);

		var statuses = responses.Select(r => r.StatusCode).OrderBy(s => s).ToArray();
		await Assert.That(statuses.Count(s => s == HttpStatusCode.OK)).IsEqualTo(1);
		await Assert.That(statuses.Count(s => s == HttpStatusCode.Conflict)).IsEqualTo(1);
	}

	[Test, NotInParallel("SetupFlow", Order = 5)]
	public async Task Complete_UsernameCollision_Returns409_WithoutConsumingClaim()
	{
		var db = factory.Services.GetRequiredService<ISharpDatabase>();
		await db.SetServerSetupCompletedAsync(false);
		var (_, existing) = await RegisterAccountAsync(); // takes a username

		var accountService = factory.Services.GetRequiredService<IAccountService>();
		var linkedBefore = await accountService.GetAccountForCharacterAsync(new DBRef(1));
		await Assert.That(linkedBefore).IsNotNull();
		var usernameBefore = linkedBefore!.Username;

		var http = CreateClient();
		var response = await http.PostAsJsonAsync("api/setup/complete",
			new SetupCompleteRequest(existing.Username, "whatever-pass-3"));
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Conflict);

		var status = await http.GetFromJsonAsync<SetupStatusResponse>("api/setup/status");
		await Assert.That(status!.NeedsSetup).IsTrue(); // claim not consumed

		// Finding 1's invariant: a rejected collision must not have mutated the #1-linked
		// account's username before the claim failed.
		var linkedAfter = await accountService.GetAccountForCharacterAsync(new DBRef(1));
		await Assert.That(linkedAfter).IsNotNull();
		await Assert.That(linkedAfter!.Username).IsEqualTo(usernameBefore);

		await db.SetServerSetupCompletedAsync(true); // leave the shared game claimed for other suites
	}
}
