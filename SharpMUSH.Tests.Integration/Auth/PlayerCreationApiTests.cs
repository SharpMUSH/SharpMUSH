using SharpMUSH.Tests.Infrastructure;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace SharpMUSH.Tests.Integration.Auth;

/// <summary>
/// HTTP-level integration tests verifying that <c>Net.PlayerCreation = false</c> refuses
/// web account registration (<c>POST api/auth/account-register</c>) and character creation
/// (<c>POST api/account/characters</c>) with 403, matching the telnet-side enforcement in
/// <c>PlayerCreationConfigTests</c>.
///
/// Player creation is disabled through <see cref="TestOptionsOverride"/>, which reaches the
/// requests the test sends and no other test's.
/// </summary>
[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
public class PlayerCreationApiTests(ServerWebAppFactory factory)
{
	private record CharacterSummary(int DbrefNumber, long CreationTime, string Name, string Flags);
	private record AccountLoginResponse(string AccountId, string Username, List<CharacterSummary> Characters, string AccountSessionToken, bool MustChangePassword);
	private record CreatedCharacterResponse(int DbrefNumber, long CreationTime);
	private record AccountRegisterRequest(string Username, string? Email, string Password);
	private record CreateCharacterRequest(string Name, string Password);

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

	private static IDisposable DisablePlayerCreation() => TestOptionsOverride.Scope(options => options with
	{
		Net = options.Net with { PlayerCreation = false }
	});

	[Test]
	public async Task AccountRegister_WhenDisabled_Returns403()
	{
		using var configuration = DisablePlayerCreation();
		var http = CreateClient();
		using var response = await http.PostAsJsonAsync("api/auth/account-register",
			new AccountRegisterRequest(UniqueName("blocked"), null, "password-123"));
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
	}

	[Test]
	public async Task CreateCharacter_WhenDisabled_Returns403()
	{
		var (http, account) = await RegisterAccountAsync();

		using var configuration = DisablePlayerCreation();
		using var request = new HttpRequestMessage(HttpMethod.Post, "api/account/characters")
		{
			Content = JsonContent.Create(new CreateCharacterRequest(UniqueName("blockedchar"), Password)),
		};
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account.AccountSessionToken);
		using var response = await http.SendAsync(request);
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
	}
}
