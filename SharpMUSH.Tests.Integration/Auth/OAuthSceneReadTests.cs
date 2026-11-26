using System.Buffers.Text;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Models.Portal;
using SharpMUSH.Library.OAuth;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Integration.Auth;

/// <summary>
/// A desktop client reading a player's scenes with an app token (<c>scenes.read</c>), over HTTP through the real
/// pipeline and the loaded Scene plugin: the character's own hidden scene reads, another's does not, a token without
/// the scope is refused with <c>insufficient_scope</c>, a bad token with <c>invalid_token</c>, the token writes nothing,
/// a wizard's app needs <c>game</c> for the wizard's view, and the live feed's hub takes the token on the same terms.
/// The app is registered at a loopback address without a port and comes back on one, as a desktop app does.
/// </summary>
[NotInParallel]
[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
public class OAuthSceneReadTests(ServerWebAppFactory factory)
{
	private record CharacterSummary(int DbrefNumber, long CreationTime, string Name, string Flags);
	private record AccountLoginResponse(string AccountId, string Username, List<CharacterSummary> Characters, string AccountSessionToken, bool MustChangePassword);
	private record CreatedCharacterResponse(int DbrefNumber, long CreationTime);
	private record SceneDto(string Id, bool IsPublic, string Status, string? OwnerDbref);

	private const string Password = "Integration-Test-Pw-1!";
	private const string Registered = "http://127.0.0.1/callback";
	private const string Redirect = "http://127.0.0.1:51234/callback";
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

	private async Task<string> Eval(string expression) =>
		(await factory.FunctionParser.EvaluateAsync(MarkupText.Plain(expression))).ToPlainText().Trim();

	private async Task<(HttpClient Http, string Session, int Character)> PlayerAsync()
	{
		var http = CreateClient();
		using var registered = await http.PostAsJsonAsync("api/auth/account-register", new { Username = UniqueName("osr"), Email = (string?)null, Password });
		await Assert.That(registered.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var account = (await registered.Content.ReadFromJsonAsync<AccountLoginResponse>())!;
		using var create = Bearer(HttpMethod.Post, "api/account/characters", account.AccountSessionToken,
			JsonContent.Create(new { Name = UniqueName("Osr"), Password }));
		using var created = await http.SendAsync(create);
		await Assert.That(created.StatusCode).IsEqualTo(HttpStatusCode.OK);
		return (http, account.AccountSessionToken, (await created.Content.ReadFromJsonAsync<CreatedCharacterResponse>())!.DbrefNumber);
	}

	private async Task<string> RegisterAppAsync()
		=> (await factory.Services.GetRequiredService<IOAuthService>().RegisterClientAsync(
			new OAuthClientDraft("Valyria " + Guid.NewGuid().ToString("N")[..6], null, null, [Registered],
				[DelegatedScopes.ScenesRead, DelegatedScopes.Commands, DelegatedScopes.Game]), confidential: false))
			.Expect<RegisteredOAuthClient>().Client.Id;

	/// <summary>The player allows the app <paramref name="scope"/>; the app trades the code on its loopback port.</summary>
	private async Task<string> TokenAsync(HttpClient http, string session, string clientId, string scope)
	{
		var challenge = Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(Verifier)));
		var request = new OAuthAuthorizeRequest("code", clientId, Redirect, scope, "st", challenge, "S256");
		using var decide = Bearer(HttpMethod.Post, "api/oauth/authorize", session, JsonContent.Create(new OAuthDecisionRequest(request, true)));
		using var decided = await http.SendAsync(decide);
		var location = (await decided.Content.ReadFromJsonAsync<OAuthDecisionView>())!.Location!;
		await Assert.That(location).StartsWith(Redirect + "?code=");
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
		return json!["access_token"].GetString()!;
	}

	/// <summary>A private scene that has ended: hidden from everyone but its owner, members, invitees and wizards.</summary>
	private async Task<string> HiddenSceneAsync(string owner)
	{
		var id = await Eval($"scenecreate(,{owner},Hidden {Guid.NewGuid():N})");
		await Eval($"sceneset({id},public,0)");
		await Eval($"sceneset({id},status,finished)");
		return id;
	}

	private static async Task<HttpResponseMessage> GetAsync(HttpClient http, string url, string token)
		=> await http.SendAsync(Bearer(HttpMethod.Get, url, token));

	[Test]
	public async Task AnAppWithScenesRead_ReadsWhatItsCharacterReads_AndNothingElse()
	{
		var (http, session, character) = await PlayerAsync();
		var mine = await HiddenSceneAsync($"#{character}");
		var joined = await HiddenSceneAsync("#1");
		await Eval($"sceneaddmember({joined},#{character},participant)");
		var others = await HiddenSceneAsync("#1");
		var token = await TokenAsync(http, session, await RegisterAppAsync(), "scenes.read");

		using var own = await GetAsync(http, $"api/scenes/{mine}", token);
		await Assert.That(own.StatusCode).IsEqualTo(HttpStatusCode.OK);
		await Assert.That((await own.Content.ReadFromJsonAsync<SceneDto>())!.Id).IsEqualTo(mine);
		using var log = await GetAsync(http, $"api/scenes/{mine}/poses", token);
		await Assert.That(log.StatusCode).IsEqualTo(HttpStatusCode.OK);

		using var hidden = await GetAsync(http, $"api/scenes/{others}", token);
		await Assert.That(hidden.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
		using var hiddenLog = await GetAsync(http, $"api/scenes/{others}/poses", token);
		await Assert.That(hiddenLog.StatusCode).IsEqualTo(HttpStatusCode.NotFound);

		using var agenda = await GetAsync(http, "api/scenes?filter=mine&count=200", token);
		await Assert.That(agenda.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var listed = (await agenda.Content.ReadFromJsonAsync<List<SceneDto>>())!.Select(scene => scene.Id).ToList();
		await Assert.That(listed).Contains(joined);
		await Assert.That(listed).DoesNotContain(others);
		using var member = await GetAsync(http, $"api/scenes/{joined}", token);
		await Assert.That(member.StatusCode).IsEqualTo(HttpStatusCode.OK);
	}

	[Test]
	public async Task ATokenWithoutScenesRead_IsRefusedForInsufficientScope_AndABadOneForInvalidToken()
	{
		var (http, session, character) = await PlayerAsync();
		var mine = await HiddenSceneAsync($"#{character}");
		var token = await TokenAsync(http, session, await RegisterAppAsync(), "commands");

		using var refused = await GetAsync(http, $"api/scenes/{mine}", token);
		await Assert.That(refused.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
		await Assert.That(refused.Headers.WwwAuthenticate.ToString()).Contains("insufficient_scope");

		using var bad = await GetAsync(http, $"api/scenes/{mine}", "smat_not-a-real-token");
		await Assert.That(bad.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
		await Assert.That(bad.Headers.WwwAuthenticate.ToString()).Contains("invalid_token");
	}

	[Test]
	public async Task AScenesReadToken_WritesNothing()
	{
		var (http, session, character) = await PlayerAsync();
		var mine = await HiddenSceneAsync($"#{character}");
		var token = await TokenAsync(http, session, await RegisterAppAsync(), "scenes.read");

		using var draft = await http.SendAsync(Bearer(HttpMethod.Put, $"api/scenes/{mine}/draft", token,
			JsonContent.Create(new { Mode = "emit", Text = "hello", Markup = (string?)null })));
		await Assert.That(draft.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
		using var link = await http.SendAsync(Bearer(HttpMethod.Post, "api/scenes/calendar/link", token));
		await Assert.That(link.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
		using var command = await http.SendAsync(Bearer(HttpMethod.Post, "api/commands", token,
			JsonContent.Create(new PortalCommandRequest($"sceneset({mine},status,active)"))));
		await Assert.That(command.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
	}

	[Test]
	public async Task AWizardsApp_GetsTheWizardsViewOnlyWithGame()
	{
		var (http, session, character) = await PlayerAsync();
		await factory.Services.GetRequiredService<IRoleRegistryService>().AssignRoleToObjectAsync(character, "wizard");
		await factory.Services.GetRequiredService<IMediator>().Send(new InvalidateGrantsCommand(null));
		var others = await HiddenSceneAsync("#1");
		var clientId = await RegisterAppAsync();

		var readOnly = await TokenAsync(http, session, clientId, "scenes.read");
		using var refused = await GetAsync(http, $"api/scenes/{others}", readOnly);
		await Assert.That(refused.StatusCode).IsEqualTo(HttpStatusCode.NotFound);

		var staff = await TokenAsync(http, session, clientId, "scenes.read game");
		using var seen = await GetAsync(http, $"api/scenes/{others}", staff);
		await Assert.That(seen.StatusCode).IsEqualTo(HttpStatusCode.OK);
	}

	[Test]
	public async Task TheLiveFeedsHub_TakesAScenesReadToken_AndNoOther()
	{
		var (http, session, _) = await PlayerAsync();
		var clientId = await RegisterAppAsync();
		var reader = await TokenAsync(http, session, clientId, "scenes.read");
		var other = await TokenAsync(http, session, clientId, "commands");

		using var header = await http.SendAsync(Bearer(HttpMethod.Post, "hubs/scene/negotiate?negotiateVersion=1", reader));
		await Assert.That(header.StatusCode).IsEqualTo(HttpStatusCode.OK);
		// A browser's WebSocket cannot carry a header; SignalR sends the token in the query on a hub's address.
		using var query = await http.PostAsync($"hubs/scene/negotiate?negotiateVersion=1&access_token={reader}", null);
		await Assert.That(query.StatusCode).IsEqualTo(HttpStatusCode.OK);
		using var badQuery = await http.PostAsync("hubs/scene/negotiate?negotiateVersion=1&access_token=smat_not-a-real-token", null);
		await Assert.That(badQuery.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);

		using var refused = await http.SendAsync(Bearer(HttpMethod.Post, "hubs/scene/negotiate?negotiateVersion=1", other));
		await Assert.That(refused.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
		using var bad = await http.SendAsync(Bearer(HttpMethod.Post, "hubs/scene/negotiate?negotiateVersion=1", "smat_not-a-real-token"));
		await Assert.That(bad.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
	}
}
