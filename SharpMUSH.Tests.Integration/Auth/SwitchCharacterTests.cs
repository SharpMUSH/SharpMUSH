using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests.Infrastructure;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace SharpMUSH.Tests.Integration.Auth;

/// <summary>
/// HTTP-level integration tests for <c>POST api/auth/switch-character</c> — the session-based
/// replacement for <c>jwt-switch-character</c>. Authenticates via the AccountSession scheme
/// (bearer = the account-session token from account-login/register) and returns an OTT for a
/// linked character rather than a new JWT pair.
/// </summary>
[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
public class SwitchCharacterTests(ServerWebAppFactory factory)
{
	private record CharacterSummary(int DbrefNumber, long CreationTime, string Name, string Flags);
	private record AccountLoginResponse(string AccountId, string Username, List<CharacterSummary> Characters, string AccountSessionToken, bool MustChangePassword);
	private record CreatedCharacterResponse(int DbrefNumber, long CreationTime);
	private record SwitchCharacterResponse(string Ott, int ExpiresIn);

	private record AccountRegisterRequest(string Username, string? Email, string Password);
	private record CreateCharacterRequest(string Name, string Password);
	private record SwitchCharacterRequest(int CharacterKey, long CharacterCreationTime);

	private const string Password = "Integration-Test-Pw-1!";

	private IOptionsWrapper<SharpMUSHOptions> Options => factory.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>();

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

	/// <summary>
	/// Discovers the client IP this HttpClient resolves to server-side, mirroring
	/// <c>SitelockCheckTests.GetClientIpAsync</c> — see that class's remarks for why this (rather
	/// than a guessed loopback literal) is required against the in-process TestServer.
	/// </summary>
	private static async Task<string> GetClientIpAsync(HttpClient http)
	{
		using var response = await http.GetAsync("api/debug/client-ip");
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var raw = await response.Content.ReadAsStringAsync();
		return string.IsNullOrEmpty(raw) ? "unknown" : raw;
	}

	/// <summary>Re-stubs the shared options substitute's SitelockRules; caller restores in a finally block.</summary>
	private (IOptionsWrapper<SharpMUSHOptions> Options, SharpMUSHOptions Original) StubSitelockRules(
		Dictionary<string, string[]> rules)
	{
		var options = Options;
		var original = options.CurrentValue;
		options.CurrentValue.Returns(original with { SitelockRules = new SitelockRulesOptions(rules) });
		return (options, original);
	}

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
		using var request = new HttpRequestMessage(HttpMethod.Post, "api/account/characters")
		{
			Content = JsonContent.Create(new CreateCharacterRequest(name, password)),
		};
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessionToken);
		using var response = await http.SendAsync(request);
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		return (await response.Content.ReadFromJsonAsync<CreatedCharacterResponse>())!;
	}

	private static HttpRequestMessage SwitchCharacterRequestMessage(string accountSessionToken, int key, long creationTime)
	{
		var request = new HttpRequestMessage(HttpMethod.Post, "api/auth/switch-character")
		{
			Content = JsonContent.Create(new SwitchCharacterRequest(key, creationTime)),
		};
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accountSessionToken);
		return request;
	}

	// switch-character is gated by Net.Logins too, so a plain non-staff account expecting
	// success here races the shared substitute mutated by SwitchCharacter_WhenLoginsDisabled_
	// NonStaff403 below and the other Net.Logins-toggling tests across this test assembly.
	[Test, NotInParallel("ConfigMutation")]
	public async Task SwitchCharacter_LinkedCharacter_Returns200WithNonEmptyOtt()
	{
		var (http, account) = await RegisterAccountAsync();
		var characterOne = await CreateCharacterAsync(http, account.AccountSessionToken, UniqueName("SwOne"), Password);
		var characterTwo = await CreateCharacterAsync(http, account.AccountSessionToken, UniqueName("SwTwo"), Password);
		// Two characters are created (per the brief) so the switch target isn't the account's only/primary character.
		await Assert.That(characterOne.DbrefNumber).IsNotEqualTo(characterTwo.DbrefNumber);

		using var request = SwitchCharacterRequestMessage(account.AccountSessionToken, characterTwo.DbrefNumber, characterTwo.CreationTime);
		using var response = await http.SendAsync(request);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var body = await response.Content.ReadFromJsonAsync<SwitchCharacterResponse>();
		await Assert.That(body!.Ott).IsNotEmpty();
	}

	[Test, NotInParallel("ConfigMutation")]
	public async Task SwitchCharacter_FromSitelockedIp_Returns403()
	{
		var (http, account) = await RegisterAccountAsync();
		var character = await CreateCharacterAsync(http, account.AccountSessionToken, UniqueName("SwLock"), Password);

		var clientIp = await GetClientIpAsync(http);
		var (options, original) = StubSitelockRules(new Dictionary<string, string[]> { [clientIp] = ["!connect"] });
		try
		{
			using var request = SwitchCharacterRequestMessage(account.AccountSessionToken, character.DbrefNumber, character.CreationTime);
			using var response = await http.SendAsync(request);

			await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
		}
		finally
		{
			options.CurrentValue.Returns(original);
		}
	}

	// The Net.Logins gate runs before the "character not linked" check, so a concurrent
	// Net.Logins-disabling test would flip the expected 401 to 403 here — same race as
	// SwitchCharacter_LinkedCharacter_Returns200WithNonEmptyOtt above.
	[Test, NotInParallel("ConfigMutation")]
	public async Task SwitchCharacter_CharacterNotLinked_Returns401()
	{
		var (http, account) = await RegisterAccountAsync();

		using var request = SwitchCharacterRequestMessage(account.AccountSessionToken, 999999, 0);
		using var response = await http.SendAsync(request);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
	}

	[Test]
	public async Task SwitchCharacter_DisabledAccountSession_Returns401()
	{
		var (http, account) = await RegisterAccountAsync();
		var character = await CreateCharacterAsync(http, account.AccountSessionToken, UniqueName("SwDis"), Password);

		var accountService = factory.Services.GetRequiredService<IAccountService>();
		await accountService.DisableAccountAsync(account.AccountId);

		using var request = SwitchCharacterRequestMessage(account.AccountSessionToken, character.DbrefNumber, character.CreationTime);
		using var response = await http.SendAsync(request);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
	}

	[Test]
	public async Task SwitchCharacter_MustChangePasswordFlaggedAccount_Returns403()
	{
		var (http, account) = await RegisterAccountAsync();
		var character = await CreateCharacterAsync(http, account.AccountSessionToken, UniqueName("SwMcp"), Password);

		var accountService = factory.Services.GetRequiredService<IAccountService>();
		await accountService.ForcePasswordChangeAsync(account.AccountId);

		using var request = SwitchCharacterRequestMessage(account.AccountSessionToken, character.DbrefNumber, character.CreationTime);
		using var response = await http.SendAsync(request);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
	}

	/// <summary>
	/// Re-stubs the shared <see cref="IOptionsWrapper{SharpMUSHOptions}"/> substitute's
	/// <c>CurrentValue</c> to disable Net.Logins. Mirrors <c>LoginsConfigApiTests.DisableLogins</c> —
	/// callers must restore the original value in a finally block and mark the test
	/// <c>[NotInParallel("ConfigMutation")]</c> to avoid racing other suites that do the same.
	/// </summary>
	private static (IOptionsWrapper<SharpMUSHOptions> Options, SharpMUSHOptions Original) DisableLogins(ServerWebAppFactory factory)
	{
		var options = factory.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>();
		var original = options.CurrentValue;
		options.CurrentValue.Returns(original with { Net = original.Net with { Logins = false } });
		return (options, original);
	}

	[Test, NotInParallel("ConfigMutation")]
	public async Task SwitchCharacter_WhenLoginsDisabled_NonStaff403()
	{
		var (http, account) = await RegisterAccountAsync();
		var character = await CreateCharacterAsync(http, account.AccountSessionToken, UniqueName("SwPleb"), Password);

		var (options, original) = DisableLogins(factory);
		try
		{
			using var request = SwitchCharacterRequestMessage(account.AccountSessionToken, character.DbrefNumber, character.CreationTime);
			using var response = await http.SendAsync(request);

			await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
		}
		finally
		{
			options.CurrentValue.Returns(original);
		}
	}
}
