using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Services;
using SharpMUSH.Tests.Infrastructure;
using System.Net;
using System.Net.Http.Json;

namespace SharpMUSH.Tests.Integration.Auth;

/// <summary>
/// End-to-end proof that <see cref="BanEnforcementService"/> is wired correctly through the real
/// DI container: banning an account revokes its session token against the real
/// <see cref="IAccountSessionStore"/> backing the running server, so the next request with that
/// token is unauthenticated. The remaining fan-outs (publish per matching handle, SignalR abort)
/// are covered precisely with NSubstitute doubles in
/// <c>SharpMUSH.Tests.Services.BanEnforcementServiceTests</c>, since substituting
/// <c>IMessageBus</c> in this shared, session-wide test host would leak into every other test that
/// shares it.
/// </summary>
[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
public class BanEnforcementTests(ServerWebAppFactory factory)
{
	private record CharacterSummary(int DbrefNumber, long CreationTime, string Name, string Flags);
	private record AccountLoginResponse(string AccountId, string Username, List<CharacterSummary> Characters, string AccountSessionToken, bool MustChangePassword);
	private record AccountRegisterRequest(string Username, string? Email, string Password);

	private const string Password = "Integration-Test-Pw-1!";

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

	[Test]
	public async Task EnforceAccountBan_RevokesSession()
	{
		var (_, account) = await RegisterAccountAsync();
		var enforcement = factory.Services.GetRequiredService<BanEnforcementService>();
		var sessions = factory.Services.GetRequiredService<IAccountSessionStore>();

		// The token authenticates before enforcement.
		await Assert.That(await sessions.ValidateAsync(account.AccountSessionToken)).IsEqualTo(account.AccountId);

		await enforcement.EnforceAccountBanAsync(account.AccountId);

		// The session token no longer authenticates.
		await Assert.That(await sessions.ValidateAsync(account.AccountSessionToken)).IsNull();
	}
}
