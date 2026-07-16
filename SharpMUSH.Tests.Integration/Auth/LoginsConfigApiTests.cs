using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests.Infrastructure;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace SharpMUSH.Tests.Integration.Auth;

/// <summary>
/// HTTP-level integration tests verifying that <c>Net.Logins = false</c> refuses
/// non-staff web logins (<c>POST api/auth/account-login</c>, <c>api/auth/mush-token</c>
/// both paths) with 403, matching the telnet-side enforcement in <c>LoginsConfigTests</c>.
/// The equivalent gate on <c>api/auth/switch-character</c> is covered by
/// <c>SwitchCharacterTests.SwitchCharacter_WhenLoginsDisabled_NonStaff403</c>.
///
/// The tests re-stub the shared <see cref="IOptionsWrapper{SharpMUSHOptions}"/> substitute's
/// <c>CurrentValue</c> around the call under test and restore it in a finally block —
/// <c>[NotInParallel("ConfigMutation")]</c> keeps them from racing other suites that do the same.
/// </summary>
[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
public class LoginsConfigApiTests(ServerWebAppFactory factory)
{
	private record CharacterSummary(int DbrefNumber, long CreationTime, string Name, string Flags);
	private record AccountLoginResponse(string AccountId, string Username, List<CharacterSummary> Characters, string AccountSessionToken, bool MustChangePassword);
	private record CreatedCharacterResponse(int DbrefNumber, long CreationTime);
	private record AccountRegisterRequest(string Username, string? Email, string Password);
	private record CreateCharacterRequest(string Name, string Password);
	private record AccountLoginRequest(string UsernameOrEmail, string Password);
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
		using var request = new HttpRequestMessage(HttpMethod.Post, "api/account/characters")
		{
			Content = JsonContent.Create(new CreateCharacterRequest(name, password)),
		};
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessionToken);
		using var response = await http.SendAsync(request);
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		return (await response.Content.ReadFromJsonAsync<CreatedCharacterResponse>())!;
	}

	private static (IOptionsWrapper<SharpMUSHOptions> Options, SharpMUSHOptions Original) DisableLogins(ServerWebAppFactory factory)
	{
		var options = factory.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>();
		var original = options.CurrentValue;
		options.CurrentValue.Returns(original with { Net = original.Net with { Logins = false } });
		return (options, original);
	}

	[Test, NotInParallel("ConfigMutation")]
	public async Task AccountLogin_WhenLoginsDisabled_NonStaff403()
	{
		var (http, account) = await RegisterAccountAsync();
		await CreateCharacterAsync(http, account.AccountSessionToken, UniqueName("Pleb"), Password);

		var (options, original) = DisableLogins(factory);
		try
		{
			using var response = await http.PostAsJsonAsync("api/auth/account-login",
				new AccountLoginRequest(account.Username, Password));
			await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
		}
		finally
		{
			options.CurrentValue.Returns(original);
		}
	}

	[Test, NotInParallel("ConfigMutation")]
	public async Task GetMushToken_ViaAccountSession_WhenLoginsDisabled_NonStaff403()
	{
		var (http, account) = await RegisterAccountAsync();
		var character = await CreateCharacterAsync(http, account.AccountSessionToken, UniqueName("Pleb"), Password);

		var (options, original) = DisableLogins(factory);
		try
		{
			using var response = await http.PostAsJsonAsync("api/auth/mush-token",
				new MushTokenRequest(null, null, account.AccountSessionToken, character.DbrefNumber, character.CreationTime));
			await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
		}
		finally
		{
			options.CurrentValue.Returns(original);
		}
	}

	[Test, NotInParallel("ConfigMutation")]
	public async Task GetMushToken_ViaCharacterCredentials_WhenLoginsDisabled_NonStaff403()
	{
		var (http, account) = await RegisterAccountAsync();
		var charName = UniqueName("Pleb");
		await CreateCharacterAsync(http, account.AccountSessionToken, charName, Password);

		var (options, original) = DisableLogins(factory);
		try
		{
			using var response = await http.PostAsJsonAsync("api/auth/mush-token",
				new MushTokenRequest(charName, Password, null, null, null));
			await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
		}
		finally
		{
			options.CurrentValue.Returns(original);
		}
	}

	[Test, NotInParallel("ConfigMutation")]
	public async Task AccountLogin_WhenLoginsDisabled_StaffAccountAllowed()
	{
		var (http, account) = await RegisterAccountAsync();
		var character = await CreateCharacterAsync(http, account.AccountSessionToken, UniqueName("Staff"), Password);

		var mediator = factory.Services.GetRequiredService<IMediator>();
		var wizardFlag = await mediator.Send(new GetObjectFlagQuery("WIZARD"));
		await Assert.That(wizardFlag).IsNotNull();
		var characterNode = await mediator.Send(new GetObjectNodeQuery(new DBRef(character.DbrefNumber, character.CreationTime)));
		await mediator.Send(new SetObjectFlagCommand(new AnySharpObject(characterNode.AsPlayer), wizardFlag!));

		var (options, original) = DisableLogins(factory);
		try
		{
			using var response = await http.PostAsJsonAsync("api/auth/account-login",
				new AccountLoginRequest(account.Username, Password));
			await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		}
		finally
		{
			options.CurrentValue.Returns(original);
		}
	}
}
