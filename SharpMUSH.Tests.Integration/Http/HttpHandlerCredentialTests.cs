using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests.Infrastructure;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace SharpMUSH.Tests.Integration.Http;

/// <summary>
/// The portal sends its account-session bearer to <c>/http/</c>, and every route's softcode reads
/// the same request registers. The credential (Authorization, Cookie, <c>?access_token</c>) must
/// never reach softcode; the character it proves does, as <c>%q&lt;viewer&gt;</c>.
/// </summary>
[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
public class HttpHandlerCredentialTests(ServerWebAppFactory factory)
{
	private record CharacterSummary(int DbrefNumber, long CreationTime, string Name, string Flags);
	private record AccountLoginResponse(string AccountId, string Username, List<CharacterSummary> Characters, string AccountSessionToken, bool MustChangePassword);
	private record CreatedCharacterResponse(int DbrefNumber, long CreationTime);
	private record AccountRegisterRequest(string Username, string? Email, string Password);
	private record CreateCharacterRequest(string Name, string Password);

	private const string Password = "Integration-Test-Pw-1!";

	private static string UniqueName(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..15];

	/// <summary>Pinned to https: following the http→https redirect drops the Authorization header.</summary>
	private HttpClient CreateClient()
	{
		var http = factory.CreateHttpClient();
		http.BaseAddress = new Uri("https://localhost/");
		return http;
	}

	private async Task SeedHandlerAttribute(string method, string commandList)
	{
		var mediator = factory.Services.GetRequiredService<IMediator>();
		var attributeService = factory.Services.GetRequiredService<IAttributeService>();

		var god = (await mediator.Send(new GetObjectNodeQuery(new DBRef(1, null)))).Expect<AnySharpObject>();
		var handler = (await mediator.Send(new GetObjectNodeQuery(new DBRef(8, null)))).Expect<AnySharpObject>();

		var result = await attributeService.SetAttributeAsync(god, handler, method, MarkupText.Plain(commandList));
		result.Expect<Success>();
	}

	/// <summary>A registered account with one character, and the session token that acts as it.</summary>
	private async Task<(string Token, string Objid)> SignedInCharacterAsync(HttpClient http)
	{
		var register = await http.PostAsJsonAsync("api/auth/account-register",
			new AccountRegisterRequest(UniqueName("cred"), null, Password));
		await Assert.That(register.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var account = (await register.Content.ReadFromJsonAsync<AccountLoginResponse>())!;

		using var create = new HttpRequestMessage(HttpMethod.Post, "api/account/characters")
		{
			Content = JsonContent.Create(new CreateCharacterRequest(UniqueName("Cred"), Password)),
		};
		create.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account.AccountSessionToken);
		using var created = await http.SendAsync(create);
		await Assert.That(created.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var character = (await created.Content.ReadFromJsonAsync<CreatedCharacterResponse>())!;

		return (account.AccountSessionToken, $"#{character.DbrefNumber}:{character.CreationTime}");
	}

	[Test]
	public async Task CredentialHeadersNeverReachSoftcode()
	{
		await SeedHandlerAttribute("CREDS",
			"think auth=|%q<hdr.authorization>| proxy=|%q<hdr.proxy-authorization>| cookie=|%q<hdr.cookie>| names=|%q<headers>| custom=|%q<hdr.x-sharp-test>|");

		var http = CreateClient();
		var (token, _) = await SignedInCharacterAsync(http);
		using var request = new HttpRequestMessage(new HttpMethod("CREDS"), "http/creds");
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
		request.Headers.TryAddWithoutValidation("Proxy-Authorization", "Basic cHJveHk6c2VjcmV0");
		request.Headers.TryAddWithoutValidation("Cookie", "session=cookie-secret");
		request.Headers.Add("X-Sharp-Test", "still-here");
		var response = await http.SendAsync(request);
		var body = await response.Content.ReadAsStringAsync();

		await Assert.That((int)response.StatusCode).IsEqualTo(200);
		await Assert.That(body).Contains("auth=|| proxy=|| cookie=||");
		await Assert.That(body).DoesNotContain(token);
		await Assert.That(body).DoesNotContain("cookie-secret");
		await Assert.That(body).DoesNotContain("AUTHORIZATION");
		await Assert.That(body).DoesNotContain("COOKIE");
		// Every other header still arrives as before.
		await Assert.That(body).Contains("X-SHARP-TEST");
		await Assert.That(body).Contains("custom=|still-here|");
	}

	[Test]
	public async Task BasicCredentialsNeverReachSoftcode()
	{
		await SeedHandlerAttribute("BASICCREDS", "think auth=|%q<hdr.authorization>| viewer=|%q<viewer>|");

		var http = CreateClient();
		using var request = new HttpRequestMessage(new HttpMethod("BASICCREDS"), "http/basic");
		request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
			Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("One:not-a-password")));
		var response = await http.SendAsync(request);
		var body = await response.Content.ReadAsStringAsync();

		await Assert.That((int)response.StatusCode).IsEqualTo(200);
		await Assert.That(body).Contains("auth=|| viewer=||");
	}

	[Test]
	public async Task AccessTokenQueryParameterIsRemovedFromThePath()
	{
		await SeedHandlerAttribute("QUERYCREDS", "think path=|%0| viewer=|%q<viewer>|");

		var http = CreateClient();
		var (token, objid) = await SignedInCharacterAsync(http);
		using var request = new HttpRequestMessage(new HttpMethod("QUERYCREDS"),
			$"http/q?a=1&access_token={Uri.EscapeDataString(token)}&b=2");
		var response = await http.SendAsync(request);
		var body = await response.Content.ReadAsStringAsync();

		await Assert.That((int)response.StatusCode).IsEqualTo(200);
		await Assert.That(body).Contains("path=|/q?a=1&b=2|");
		await Assert.That(body).DoesNotContain(token);
		// The token in the query string still authenticates the request; only softcode is kept from it.
		await Assert.That(body).Contains($"viewer=|{objid}|");
	}

	[Test]
	public async Task SignedInRequestNamesItsCharacterAsViewer()
	{
		await SeedHandlerAttribute("WHOAMI", "think viewer=|%q<viewer>| name=|[name(%q<viewer>)]|");

		var http = CreateClient();
		var (token, objid) = await SignedInCharacterAsync(http);
		using var request = new HttpRequestMessage(new HttpMethod("WHOAMI"), "http/whoami");
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
		var response = await http.SendAsync(request);
		var body = await response.Content.ReadAsStringAsync();

		await Assert.That((int)response.StatusCode).IsEqualTo(200);
		await Assert.That(body).Contains($"viewer=|{objid}|");
		await Assert.That(body).DoesNotContain("name=||");
	}

	[Test]
	public async Task AnonymousRequestHasNoViewer()
	{
		await SeedHandlerAttribute("ANONWHO", "think viewer=|%q<viewer>|");

		var http = CreateClient();
		using var request = new HttpRequestMessage(new HttpMethod("ANONWHO"), "http/anon");
		var response = await http.SendAsync(request);
		var body = await response.Content.ReadAsStringAsync();

		await Assert.That((int)response.StatusCode).IsEqualTo(200);
		await Assert.That(body).Contains("viewer=||");
	}

	[Test]
	public async Task ViewerReachesRoutedSubHandlers()
	{
		// The stock router @includes the sub-attribute, which keeps the request's registers.
		await SeedHandlerAttribute("POST", SharpMUSH.Server.Services.BundledHttpHooks.Attribute("POST"));
		await SeedHandlerAttribute("POST`CREDTEST`WHO", "think viewer=|%q<viewer>| auth=|%q<hdr.authorization>|");

		var http = CreateClient();
		var (token, objid) = await SignedInCharacterAsync(http);
		using var request = new HttpRequestMessage(HttpMethod.Post, "http/credtest/who")
		{
			Content = new StringContent(string.Empty),
		};
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
		var response = await http.SendAsync(request);
		var body = await response.Content.ReadAsStringAsync();

		await Assert.That((int)response.StatusCode).IsEqualTo(200);
		await Assert.That(body).Contains($"viewer=|{objid}| auth=||");
	}
}
