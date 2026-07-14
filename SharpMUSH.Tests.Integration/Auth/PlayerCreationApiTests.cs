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
/// HTTP-level integration tests verifying that <c>Net.PlayerCreation = false</c> refuses
/// web account registration (<c>POST api/auth/account-register</c>) and character creation
/// (<c>POST api/account/characters</c>) with 403, matching the telnet-side enforcement in
/// <c>PlayerCreationConfigTests</c>.
///
/// The tests re-stub the shared <see cref="IOptionsWrapper{SharpMUSHOptions}"/> substitute's
/// <c>CurrentValue</c> around the call under test and restore it in a finally block —
/// <c>[NotInParallel("ConfigMutation")]</c> keeps them from racing other suites that do the same.
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

	[Test, NotInParallel("ConfigMutation")]
	public async Task AccountRegister_WhenDisabled_Returns403()
	{
		var options = factory.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>();
		var original = options.CurrentValue;
		var restricted = original with { Net = original.Net with { PlayerCreation = false } };
		options.CurrentValue.Returns(restricted);
		try
		{
			var http = CreateClient();
			using var response = await http.PostAsJsonAsync("api/auth/account-register",
				new AccountRegisterRequest(UniqueName("blocked"), null, "password-123"));
			await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
		}
		finally
		{
			options.CurrentValue.Returns(original);
		}
	}

	[Test, NotInParallel("ConfigMutation")]
	public async Task CreateCharacter_WhenDisabled_Returns403()
	{
		var (http, account) = await RegisterAccountAsync();

		var options = factory.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>();
		var original = options.CurrentValue;
		var restricted = original with { Net = original.Net with { PlayerCreation = false } };
		options.CurrentValue.Returns(restricted);
		try
		{
			using var request = new HttpRequestMessage(HttpMethod.Post, "api/account/characters")
			{
				Content = JsonContent.Create(new CreateCharacterRequest(UniqueName("blockedchar"), Password)),
			};
			request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account.AccountSessionToken);
			using var response = await http.SendAsync(request);
			await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
		}
		finally
		{
			options.CurrentValue.Returns(original);
		}
	}
}
