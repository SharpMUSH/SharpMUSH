using System.Buffers.Text;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Models.Portal;
using SharpMUSH.Library.OAuth;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests.Infrastructure;

namespace SharpMUSH.Tests.Integration.Auth;

/// <summary>
/// An app allowed by a player, end to end over HTTP: the consent page's question and answer, the token exchange, and
/// commands run with the app's token. The character keeps only the permissions the app's scopes cover, in the command
/// and in the work the command queues, and the token reaches no endpoint that is not open to apps.
/// </summary>
[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
public class OAuthFlowTests(ServerWebAppFactory factory)
{
	private record CharacterSummary(int DbrefNumber, long CreationTime, string Name, string Flags);
	private record AccountLoginResponse(string AccountId, string Username, List<CharacterSummary> Characters, string AccountSessionToken, bool MustChangePassword);
	private record CreatedCharacterResponse(int DbrefNumber, long CreationTime);
	private record TokenResponse(string AccessToken, string? RefreshToken, string Scope);

	private const string Password = "Integration-Test-Pw-1!";
	private const string Redirect = "https://builder.example.test/callback";
	private const string Verifier = "integration-verifier-at-least-forty-three-characters-long";

	private HttpClient CreateClient()
	{
		var http = factory.CreateHttpClient();
		http.BaseAddress = new Uri("https://localhost/");
		return http;
	}

	private static string UniqueName(string prefix) => $"{prefix[..Math.Min(prefix.Length, 4)]}{Guid.NewGuid():N}"[..15];

	private static HttpRequestMessage Bearer(HttpMethod method, string url, string token, HttpContent? content = null)
	{
		var request = new HttpRequestMessage(method, url) { Content = content };
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
		return request;
	}

	private async Task<(HttpClient Http, string Session, string Name, int Character)> PlayerAsync()
	{
		var http = CreateClient();
		using var registered = await http.PostAsJsonAsync("api/auth/account-register", new { Username = UniqueName("oapp"), Email = (string?)null, Password });
		await Assert.That(registered.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var account = (await registered.Content.ReadFromJsonAsync<AccountLoginResponse>())!;
		var name = UniqueName("Oac");
		using var create = Bearer(HttpMethod.Post, "api/account/characters", account.AccountSessionToken,
			JsonContent.Create(new { Name = name, Password }));
		using var created = await http.SendAsync(create);
		await Assert.That(created.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var character = (await created.Content.ReadFromJsonAsync<CreatedCharacterResponse>())!;
		return (http, account.AccountSessionToken, name, character.DbrefNumber);
	}

	private async Task<string> RegisterAppAsync()
		=> (await factory.Services.GetRequiredService<IOAuthService>().RegisterClientAsync(
			new OAuthClientDraft("Grid Builder " + Guid.NewGuid().ToString("N")[..6], null, null, [Redirect],
				[DelegatedScopes.Commands, DelegatedScopes.Build]), confidential: false)).Expect<RegisteredOAuthClient>().Client.Id;

	/// <summary>The consent page's round trip as the player, then the app's token exchange.</summary>
	private async Task<TokenResponse> AuthorizeAsync(HttpClient http, string session, string clientId, string scope)
	{
		var challenge = Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(Verifier)));
		var request = new OAuthAuthorizeRequest("code", clientId, Redirect, scope, "st", challenge, "S256");
		using var check = Bearer(HttpMethod.Get,
			$"api/oauth/authorize?response_type=code&client_id={clientId}&redirect_uri={Uri.EscapeDataString(Redirect)}"
			+ $"&scope={Uri.EscapeDataString(scope)}&state=st&code_challenge={challenge}&code_challenge_method=S256", session);
		using var asked = await http.SendAsync(check);
		var view = (await asked.Content.ReadFromJsonAsync<OAuthAuthorizeView>())!;
		await Assert.That(view.App?.ClientId).IsEqualTo(clientId);

		using var decide = Bearer(HttpMethod.Post, "api/oauth/authorize", session, JsonContent.Create(new OAuthDecisionRequest(request, true)));
		using var decided = await http.SendAsync(decide);
		var location = (await decided.Content.ReadFromJsonAsync<OAuthDecisionView>())!.Location!;
		var code = Uri.UnescapeDataString(location.Split("code=")[1].Split('&')[0]);

		using var form = new FormUrlEncodedContent(new Dictionary<string, string>
		{
			["grant_type"] = "authorization_code",
			["code"] = code,
			["redirect_uri"] = Redirect,
			["code_verifier"] = Verifier,
			["client_id"] = clientId,
		});
		using var exchanged = await http.PostAsync("api/oauth/token", form);
		await Assert.That(exchanged.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var json = await exchanged.Content.ReadFromJsonAsync<Dictionary<string, System.Text.Json.JsonElement>>();
		return new(json!["access_token"].GetString()!, json["refresh_token"].GetString(), json["scope"].GetString()!);
	}

	private static async Task<(HttpStatusCode Status, string Output)> RunAsync(HttpClient http, string token, string command)
	{
		using var request = Bearer(HttpMethod.Post, "api/commands", token, JsonContent.Create(new PortalCommandRequest(command)));
		using var response = await http.SendAsync(request);
		if (!response.IsSuccessStatusCode) return (response.StatusCode, string.Empty);
		var answer = (await response.Content.ReadFromJsonAsync<PortalCommandResponse>())!;
		return (response.StatusCode, string.Join("\n", answer.Output));
	}

	[Test]
	public async Task AnAppsCommands_HoldOnlyThePermissionsItsScopesCover_EvenWhenQueued()
	{
		var (http, session, _, character) = await PlayerAsync();
		await factory.Services.GetRequiredService<IRoleRegistryService>().AssignRoleToObjectAsync(character, "builder");
		await factory.Services.GetRequiredService<IMediator>().Send(new InvalidateGrantsCommand(null));
		var clientId = await RegisterAppAsync();

		// The test host's own session plays #1, which sees the character's powers as they are.
		await Assert.That(await RunAsync(http, session, $"think [haspower(#{character},builder)]")).IsEqualTo((HttpStatusCode.OK, "1"));

		var commandsOnly = await AuthorizeAsync(http, session, clientId, "commands");
		await Assert.That(commandsOnly.Scope).IsEqualTo("profile commands");
		await Assert.That(await RunAsync(http, commandsOnly.AccessToken, "think [haspower(me,builder)]")).IsEqualTo((HttpStatusCode.OK, "0"));

		// Work the command queues runs as the app too: it does not get the Builder power back.
		await RunAsync(http, commandsOnly.AccessToken, "&OAUTH_DO me=think [set(me,OAUTH_QUEUED:[haspower(me,builder)])]");
		await RunAsync(http, commandsOnly.AccessToken, "@trigger me/OAUTH_DO");
		await factory.Services.GetRequiredService<ITaskScheduler>().DrainImmediateQueueForTests(TimeSpan.FromSeconds(30));
		await Assert.That(await RunAsync(http, session, $"think [get(#{character}/OAUTH_QUEUED)]")).IsEqualTo((HttpStatusCode.OK, "0"));

		var building = await AuthorizeAsync(http, session, clientId, "commands build");
		await Assert.That(await RunAsync(http, building.AccessToken, "think [haspower(me,builder)]")).IsEqualTo((HttpStatusCode.OK, "1"));
	}

	[Test]
	public async Task AnAppsToken_SaysWhoItActsAs_AndReachesNothingElse()
	{
		var (http, session, name, _) = await PlayerAsync();
		var token = (await AuthorizeAsync(http, session, await RegisterAppAsync(), "commands")).AccessToken;

		using var me = await http.SendAsync(Bearer(HttpMethod.Get, "api/oauth/me", token));
		await Assert.That(me.StatusCode).IsEqualTo(HttpStatusCode.OK);
		await Assert.That((await me.Content.ReadFromJsonAsync<OAuthMeView>())!.Name).IsEqualTo(name);

		using var grants = await http.SendAsync(Bearer(HttpMethod.Get, "api/oauth/grants", token));
		await Assert.That(grants.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
		using var characters = await http.SendAsync(Bearer(HttpMethod.Get, "api/account/characters", token));
		await Assert.That(characters.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
	}

	[Test]
	public async Task ACommandRestrictedNoApp_IsRefusedToTheApp_NotToThePlayer()
	{
		var (http, session, _, _) = await PlayerAsync();
		var token = (await AuthorizeAsync(http, session, await RegisterAppAsync(), "commands")).AccessToken;

		// Only an app's commands are refused, and only these tests run as an app, so no other test sees it.
		var (_, restricted) = await RunAsync(http, session, "@command/restrict @lemit=noapp");
		await Assert.That(restricted).Contains("NoApp");

		var (_, asApp) = await RunAsync(http, token, "@lemit Built by an app.");
		await Assert.That(asApp).DoesNotContain("Built by an app.");
		var (_, asPlayer) = await RunAsync(http, session, "@lemit Built by a player.");
		await Assert.That(asPlayer).Contains("Built by a player.");
	}

	[Test]
	public async Task RevokingFromTheAccount_StopsTheAppAtOnce()
	{
		var (http, session, _, _) = await PlayerAsync();
		var token = (await AuthorizeAsync(http, session, await RegisterAppAsync(), "commands")).AccessToken;
		await Assert.That((await RunAsync(http, token, "think ok")).Status).IsEqualTo(HttpStatusCode.OK);

		using var listed = await http.SendAsync(Bearer(HttpMethod.Get, "api/oauth/grants", session));
		var grant = (await listed.Content.ReadFromJsonAsync<List<OAuthGrantView>>())!.Single();
		using var revoked = await http.SendAsync(Bearer(HttpMethod.Delete, $"api/oauth/grants/{grant.Id}", session));
		await Assert.That(revoked.StatusCode).IsEqualTo(HttpStatusCode.NoContent);

		await Assert.That((await RunAsync(http, token, "think ok")).Status).IsEqualTo(HttpStatusCode.Unauthorized);
	}
}
