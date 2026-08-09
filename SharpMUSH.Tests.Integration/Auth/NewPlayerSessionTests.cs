using SharpMUSH.Tests.Infrastructure;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace SharpMUSH.Tests.Integration.Auth;

/// <summary>
/// The two defects every brand-new player hit in sequence, reproduced end to end against the real
/// host: a session minted at registration carries no character binding (the account had none yet),
/// and the cached role/scope claims the session authenticates with are derived from a character set
/// that has just changed.
/// <para>
/// N-02: the registration session never bound the character created under it, so the roster reported
/// the account's only character as <c>isActing:false</c> and every write needing a character
/// identity answered <c>401 Missing character identity.</c> — until the player logged out and back
/// in, at which point login bound one and everything worked.
/// </para>
/// </summary>
[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
public class NewPlayerSessionTests(ServerWebAppFactory factory)
{
	private record CharacterSummary(int DbrefNumber, long CreationTime, string Name, string Flags, bool IsActing);

	private record AccountLoginResponse(string AccountId, string Username, List<CharacterSummary> Characters,
		string AccountSessionToken, bool MustChangePassword, string Role, List<string> Permissions);

	private record AccountRegisterRequest(string Username, string? Email, string Password);

	private record CreateCharacterRequest(string Name, string Password);

	private record CreatedCharacterResponse(int DbrefNumber, long CreationTime);

	private const string Password = "Integration-Test-Pw-1!";

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

	/// <summary>Registers a brand-new account and returns the session token minted for it — the one
	/// that names no character, because the account owns none at that moment.</summary>
	private static async Task<(HttpClient Http, AccountLoginResponse Account)> RegisterAsync(HttpClient http)
	{
		using var response = await http.PostAsJsonAsync("api/auth/account-register",
			new AccountRegisterRequest(UniqueName("newpl"), null, Password));
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var account = await response.Content.ReadFromJsonAsync<AccountLoginResponse>();
		await Assert.That(account).IsNotNull();
		await Assert.That(account!.Characters).IsEmpty();
		return (http, account);
	}

	private static async Task<CreatedCharacterResponse> CreateCharacterAsync(HttpClient http, string token, string name)
	{
		using var request = new HttpRequestMessage(HttpMethod.Post, "api/account/characters")
		{
			Content = JsonContent.Create(new CreateCharacterRequest(name, Password))
		};
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
		using var response = await http.SendAsync(request);
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK)
			.Because(await response.Content.ReadAsStringAsync());
		var created = await response.Content.ReadFromJsonAsync<CreatedCharacterResponse>();
		await Assert.That(created).IsNotNull();
		return created!;
	}

	private static async Task<List<CharacterSummary>> RosterAsync(HttpClient http, string token)
	{
		using var request = new HttpRequestMessage(HttpMethod.Get, "api/account/characters");
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
		using var response = await http.SendAsync(request);
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var roster = await response.Content.ReadFromJsonAsync<List<CharacterSummary>>();
		await Assert.That(roster).IsNotNull();
		return roster!;
	}

	/// <summary>
	/// The measured reproduction: under the registration session the roster answered
	/// <c>[{"name":"Gwendolyn","isActing":false}]</c>, and only a logout/login round trip turned it
	/// true. No re-authentication happens anywhere in this test.
	/// </summary>
	[Test]
	public async Task RegistrationSession_AfterCreatingItsFirstCharacter_ActsAsThatCharacter()
	{
		var (http, account) = await RegisterAsync(CreateClient());
		var created = await CreateCharacterAsync(http, account.AccountSessionToken, UniqueName("Gwen"));

		var roster = await RosterAsync(http, account.AccountSessionToken);

		await Assert.That(roster).Count().IsEqualTo(1);
		await Assert.That(roster[0].DbrefNumber).IsEqualTo(created.DbrefNumber);
		await Assert.That(roster[0].IsActing).IsTrue()
			.Because("the session minted at registration must adopt the account's character without a re-login");
	}

	/// <summary>
	/// An account that already owns characters keeps the one its token names. This is the property
	/// that stops the implicit fallback from quietly undoing <c>POST api/auth/switch-character</c>.
	/// </summary>
	[Test]
	public async Task SwitchedSession_KeepsItsChosenCharacter_NotTheLowestDbref()
	{
		var (http, account) = await RegisterAsync(CreateClient());
		var first = await CreateCharacterAsync(http, account.AccountSessionToken, UniqueName("Aaa"));
		var second = await CreateCharacterAsync(http, account.AccountSessionToken, UniqueName("Bbb"));
		await Assert.That(second.DbrefNumber).IsGreaterThan(first.DbrefNumber);

		using var switchRequest = new HttpRequestMessage(HttpMethod.Post, "api/auth/switch-character")
		{
			Content = JsonContent.Create(new { CharacterKey = second.DbrefNumber, CharacterCreationTime = second.CreationTime })
		};
		switchRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account.AccountSessionToken);
		using var switchResponse = await http.SendAsync(switchRequest);
		await Assert.That(switchResponse.StatusCode).IsEqualTo(HttpStatusCode.OK)
			.Because(await switchResponse.Content.ReadAsStringAsync());
		var switched = await switchResponse.Content.ReadFromJsonAsync<SwitchCharacterResponse>();

		var roster = await RosterAsync(http, switched!.AccountSessionToken);

		await Assert.That(roster.Single(c => c.IsActing).DbrefNumber).IsEqualTo(second.DbrefNumber);
	}

	private record SwitchCharacterResponse(string Ott, int ExpiresIn, string AccountSessionToken);
}
