using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Server.Authentication;
using SharpMUSH.Server.Controllers;
using SharpMUSH.Tests.Infrastructure;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;

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
/// <para>
/// N-03: the role and scopes the session authenticates with are cached for 30s and were never
/// invalidated when the character set changed, so the same first login answered <c>role=Guest</c>
/// with 0 scopes and, thirty-five seconds later, <c>role=Player</c> with 5.
/// </para>
/// <para>
/// The claim-level tests below drive the real <c>AccountSession</c> handler out of the host's own
/// DI container rather than over HTTP, because the test host runs in Development, where
/// <c>DebugAuth</c> is the DEFAULT scheme and would authenticate any <c>[Authorize]</c> endpoint as
/// the bootstrap admin regardless of the bearer — proving nothing about the token under test.
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

	/// <summary>
	/// Runs the real <c>AccountSession</c> authentication handler over <paramref name="token"/>
	/// through the host's DI container, returning the principal a request bearing that token would
	/// carry — role claim, permission-scope claims and acting-character claims included.
	/// </summary>
	/// <remarks>
	/// A fresh DI scope per call, deliberately: <c>IAuthenticationHandlerProvider</c> is scoped and
	/// memoizes handlers, and <c>AuthenticationHandler&lt;T&gt;</c> memoizes its own authenticate
	/// result — so reusing one scope would replay the first answer and quietly assert nothing about
	/// the second.
	/// </remarks>
	private async Task<ClaimsPrincipal> AuthenticateAsync(string token)
	{
		using var scope = factory.Services.CreateScope();
		var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
		context.Request.Headers.Authorization = $"Bearer {token}";

		var result = await context.AuthenticateAsync(AccountSessionAuthenticationHandler.SchemeName);

		await Assert.That(result.Succeeded).IsTrue().Because(result.Failure?.Message ?? "no failure reported");
		return result.Principal!;
	}

	private static string[] ScopesOf(ClaimsPrincipal principal) =>
		[.. principal.FindAll(PortalPermission.ClaimType).Select(c => c.Value).Order(StringComparer.Ordinal)];

	/// <summary>
	/// The measured reproduction: create your first character and authenticate immediately, and the
	/// answer was <c>role=Guest</c> with 0 scopes for the remainder of the 30s cache window. The
	/// account role was already cached as Guest by registration, moments earlier.
	/// </summary>
	[Test]
	public async Task FirstCharacter_LiftsRoleAndScopes_OnTheVeryNextRequest()
	{
		var (http, account) = await RegisterAsync(CreateClient());

		// Registration itself authenticates, which is what seeds the Guest role/scope cache entries
		// that used to survive the character creation below.
		var before = await AuthenticateAsync(account.AccountSessionToken);
		await Assert.That(before.FindFirstValue(ClaimTypes.Role)).IsEqualTo(nameof(PortalRole.Guest));
		await Assert.That(ScopesOf(before)).IsEmpty();

		await CreateCharacterAsync(http, account.AccountSessionToken, UniqueName("Gwen"));

		var after = await AuthenticateAsync(account.AccountSessionToken);

		await Assert.That(after.FindFirstValue(ClaimTypes.Role)).IsEqualTo(nameof(PortalRole.Player));
		await Assert.That(ScopesOf(after)).IsEquivalentTo(new[]
		{
			PortalPermission.MediaUpload,
			PortalPermission.SoftcodeUse,
			PortalPermission.WikiCreate,
			PortalPermission.WikiEdit,
			PortalPermission.WikiRead,
		});
	}

	/// <summary>
	/// The unlink direction, which is the security-relevant one: dropping the last character drops
	/// the account back to Guest, and the Player scopes must not outlive it.
	/// </summary>
	[Test]
	public async Task UnlinkingTheLastCharacter_DropsScopes_OnTheVeryNextRequest()
	{
		var (http, account) = await RegisterAsync(CreateClient());
		var created = await CreateCharacterAsync(http, account.AccountSessionToken, UniqueName("Temp"));

		var withCharacter = await AuthenticateAsync(account.AccountSessionToken);
		await Assert.That(ScopesOf(withCharacter)).IsNotEmpty();

		using var unlink = new HttpRequestMessage(HttpMethod.Delete, $"api/account/characters/{created.DbrefNumber}");
		unlink.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account.AccountSessionToken);
		using var unlinkResponse = await http.SendAsync(unlink);
		await Assert.That(unlinkResponse.StatusCode).IsEqualTo(HttpStatusCode.NoContent);

		var afterUnlink = await AuthenticateAsync(account.AccountSessionToken);

		await Assert.That(afterUnlink.FindFirstValue(ClaimTypes.Role)).IsEqualTo(nameof(PortalRole.Guest));
		await Assert.That(ScopesOf(afterUnlink)).IsEmpty();
	}

	/// <summary>
	/// Both defects at once, in the shape the player actually met them: under the registration
	/// session, <c>POST /api/wiki</c> answered <c>401 Missing character identity.</c> (N-02) behind a
	/// <c>wiki.create</c> gate the account did not yet hold (N-03). Neither this test nor the player
	/// re-authenticates anywhere.
	/// </summary>
	[Test]
	public async Task RegistrationSession_CanCreateAWikiPage_WithoutReAuthenticating()
	{
		using var scope = factory.Services.CreateScope();
		var (http, account) = await RegisterAsync(CreateClient());
		await CreateCharacterAsync(http, account.AccountSessionToken, UniqueName("Scribe"));

		var principal = await AuthenticateAsync(account.AccountSessionToken);

		// The gate WikiController.CreatePage sits behind, evaluated against the real policy provider.
		var authorization = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();
		var gate = await authorization.AuthorizeAsync(principal, resource: null, PortalPermission.WikiCreate);
		await Assert.That(gate.Succeeded).IsTrue().Because("wiki.create must be granted on the first request");

		var controller = new WikiController(
			scope.ServiceProvider.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IWikiService>(),
			scope.ServiceProvider.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IWikiLocalizationService>(),
			scope.ServiceProvider.GetRequiredService<SharpMUSH.Server.Services.IPrerenderCacheService>(),
			scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<WikiController>>())
		{
			ControllerContext = new ControllerContext
			{
				HttpContext = new DefaultHttpContext { User = principal, RequestServices = scope.ServiceProvider }
			}
		};

		var result = await controller.CreatePage(new WikiController.CreatePageRequest(
			UniqueName("Page"), "Written by a brand-new player.", null, null));

		await Assert.That(result).IsTypeOf<CreatedAtActionResult>()
			.Because($"expected the page to be created, got: {result}");
	}
}
