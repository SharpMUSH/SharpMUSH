using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Services;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace SharpMUSH.Tests.Integration.Auth;

/// <summary>
/// Regression cover for the session-store write storm: <see cref="DatabaseAccountSessionStore.ValidateAsync"/>
/// slides the rolling window on every authenticated request, so a single portal page load — several
/// concurrent API calls carrying the same bearer — used to have every one of them upsert the same session
/// document at once. ArangoDB answered with 409 ("write-write conflict" / "timeout waiting to lock key"),
/// nothing caught it, and the request became a 500.
/// </summary>
[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
public class SessionConcurrencyTests(ServerWebAppFactory factory)
{
	private record CharacterSummary(int DbrefNumber, long CreationTime, string Name, string Flags);

	private record AccountLoginResponse(string AccountId, string Username, List<CharacterSummary> Characters,
		string AccountSessionToken, bool MustChangePassword);

	private record AccountRegisterRequest(string Username, string? Email, string Password);

	private const string Password = "Integration-Test-Pw-1!";

	/// <summary>How many simultaneous authenticated requests one bearer fans out to. A portal page load
	/// issues roughly half a dozen; this exceeds that so the race is reproduced reliably rather than
	/// occasionally.</summary>
	private const int ConcurrentRequests = 24;

	private ISharpDatabase Db => factory.Services.GetRequiredService<ISharpDatabase>();

	/// <summary>
	/// Pinned to https for the same reason as <see cref="SwitchCharacterTests"/>: UseHttpsRedirection
	/// answers http with a 307, and following it makes HttpClient drop the Authorization header.
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
		using var response = await http.PostAsJsonAsync("api/auth/account-register",
			new AccountRegisterRequest(UniqueName("conc"), null, Password));
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var account = await response.Content.ReadFromJsonAsync<AccountLoginResponse>();
		await Assert.That(account).IsNotNull();
		return (http, account!);
	}

	[Test]
	public async Task ConcurrentAuthenticatedRequests_OnOneSessionToken_AllSucceed()
	{
		var (http, account) = await RegisterAccountAsync();

		var gate = new SemaphoreSlim(0, ConcurrentRequests);
		var calls = Enumerable.Range(0, ConcurrentRequests).Select(async _ =>
		{
			await gate.WaitAsync();
			using var request = new HttpRequestMessage(HttpMethod.Get, "api/account/characters");
			request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account.AccountSessionToken);
			using var response = await http.SendAsync(request);
			return ((int)response.StatusCode, await response.Content.ReadAsStringAsync());
		}).ToArray();

		gate.Release(ConcurrentRequests);
		var results = await Task.WhenAll(calls);

		var failures = results.Where(r => r.Item1 is < 200 or > 299).ToArray();
		await Assert.That(failures).IsEmpty()
			.Because($"every concurrent request on one session token must succeed; got: "
				+ string.Join(" | ", failures.Select(f => $"{f.Item1}: {f.Item2}")));
	}

	[Test]
	public async Task RollingWindow_StillSlides_PastTheOriginalExpiry()
	{
		var store = new DatabaseAccountSessionStore(Db);
		var ttl = TimeSpan.FromSeconds(2);
		var token = await store.CreateTokenAsync("node_accounts/1", ttl, "203.0.113.71");
		var originalExpiry = (await Db.GetSessionAsync(token))!.ExpiryUnixMs;

		// Steady use across the whole original window, then past its end.
		for (var i = 0; i < 12; i++)
		{
			await Task.Delay(250);
			await Assert.That((await store.ValidateAsync(token))?.AccountId).IsEqualTo("node_accounts/1");
		}

		await Assert.That(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()).IsGreaterThan(originalExpiry)
			.Because("the test must actually outlive the session's original expiry for this to prove anything");
		await Assert.That((await Db.GetSessionAsync(token))!.ExpiryUnixMs).IsGreaterThan(originalExpiry);

		await store.RevokeAsync(token);
	}

	[Test]
	public async Task ExpiredSession_IsRejectedAndDeleted()
	{
		var store = new DatabaseAccountSessionStore(Db);
		var token = await store.CreateTokenAsync("node_accounts/1", TimeSpan.FromMilliseconds(150), "203.0.113.72");
		await Assert.That(await Db.GetSessionAsync(token)).IsNotNull();

		await Task.Delay(500);

		await Assert.That(await store.ValidateAsync(token)).IsNull();
		await Assert.That(await Db.GetSessionAsync(token)).IsNull();
	}
}
