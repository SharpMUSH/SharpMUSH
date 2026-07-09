using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using SharpMUSH.Tests.Infrastructure;

namespace SharpMUSH.Tests.Integration.Mcp;

/// <summary>
/// Integration tests for the in-server MCP endpoint (<c>/mcp</c>): its character+password
/// (HTTP Basic) authentication boundary, and real MCP-protocol round-trips that exercise the
/// <c>validate</c> tool against the live parser.
///
/// Marked <see cref="ExplicitAttribute"/> because, like the other integration tests here, they
/// require the Arango/NATS/SQL Testcontainers spun up by <see cref="ServerWebAppFactory"/> and
/// so are excluded from the default (no-Docker) test run. The MCP endpoint is enabled for the
/// test host via <c>ServerTestWebApplicationBuilderFactory.ConfigureStartupConfiguration</c>.
/// </summary>
[Explicit]
[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
public class McpEndpointTests(ServerWebAppFactory factory)
{
	private const string McpPath = "/mcp";
	private static readonly Uri BaseAddress = new("https://localhost/");
	private static readonly Uri McpEndpoint = new(BaseAddress, McpPath);

	private sealed record AccountRegisterRequest(string Username, string? Email, string Password);
	private sealed record AccountLoginResponse(string AccountId, string Username, string AccountSessionToken);
	private sealed record CreateCharacterRequest(string Name, string Password);
	private sealed record CreatedCharacterResponse(int DbrefNumber, long CreationTime);

	private HttpClient CreateClient()
	{
		var http = factory.CreateHttpClient();
		http.BaseAddress = BaseAddress;
		return http;
	}

	private static string UniqueName(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..18];

	/// <summary>
	/// Registers a fresh account and creates a character on it with the given password,
	/// exercising the real PasswordService hashing round-trip. Returns the character's
	/// name so it can be used as an MCP Basic-auth credential.
	/// </summary>
	private async Task<string> CreateCharacterWithPasswordAsync(string password)
	{
		var http = CreateClient();

		var register = await http.PostAsJsonAsync(
			"api/auth/account-register",
			new AccountRegisterRequest(UniqueName("acct"), null, password));
		await Assert.That(register.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var account = await register.Content.ReadFromJsonAsync<AccountLoginResponse>();
		await Assert.That(account).IsNotNull();

		var characterName = UniqueName("mcpchar");
		var createRequest = new HttpRequestMessage(HttpMethod.Post, "api/account/characters")
		{
			Content = JsonContent.Create(new CreateCharacterRequest(characterName, password))
		};
		createRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account!.AccountSessionToken);
		var createResponse = await http.SendAsync(createRequest);
		await Assert.That(createResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var created = await createResponse.Content.ReadFromJsonAsync<CreatedCharacterResponse>();
		await Assert.That(created).IsNotNull();

		return characterName;
	}

	private static string BasicHeader(string character, string password)
		=> "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{character}:{password}"));

	private static async Task<HttpResponseMessage> PostInitializeAsync(HttpClient http, string? authorization)
	{
		var request = new HttpRequestMessage(HttpMethod.Post, McpPath)
		{
			Content = new StringContent(
				"""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"test","version":"1.0"}}}""",
				Encoding.UTF8,
				"application/json")
		};
		request.Headers.Accept.ParseAdd("application/json");
		request.Headers.Accept.ParseAdd("text/event-stream");
		if (authorization is not null)
		{
			request.Headers.TryAddWithoutValidation("Authorization", authorization);
		}

		return await http.SendAsync(request);
	}

	// ── Authentication boundary ────────────────────────────────────────────────

	[Test]
	public async Task Mcp_WithoutCredentials_Returns401WithBasicChallenge()
	{
		var http = CreateClient();

		var response = await PostInitializeAsync(http, authorization: null);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
		await Assert.That(response.Headers.WwwAuthenticate.ToString()).Contains("Basic");
	}

	[Test]
	public async Task Mcp_WithMalformedAuthorizationHeader_Returns401()
	{
		var http = CreateClient();

		var response = await PostInitializeAsync(http, authorization: "Basic not-valid-base64!!");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
	}

	[Test]
	public async Task Mcp_WithUnknownCharacter_Returns401()
	{
		var http = CreateClient();

		var response = await PostInitializeAsync(http, BasicHeader("NoSuchCharacter", "whatever"));

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
	}

	[Test]
	public async Task Mcp_WithWrongPassword_Returns401()
	{
		var character = await CreateCharacterWithPasswordAsync("Correct-Horse-1!");
		var http = CreateClient();

		var response = await PostInitializeAsync(http, BasicHeader(character, "wrong-password"));

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
	}

	[Test]
	public async Task Mcp_WithValidCharacter_IsNotUnauthorized()
	{
		var password = "Correct-Horse-2!";
		var character = await CreateCharacterWithPasswordAsync(password);
		var http = CreateClient();

		var response = await PostInitializeAsync(http, BasicHeader(character, password));

		await Assert.That(response.StatusCode).IsNotEqualTo(HttpStatusCode.Unauthorized);
	}

	// ── MCP protocol round-trips (authenticated) ───────────────────────────────

	private async Task<McpClient> ConnectAsync(string character, string password)
	{
		var http = CreateClient();
		var transport = new HttpClientTransport(
			new HttpClientTransportOptions
			{
				Endpoint = McpEndpoint,
				TransportMode = HttpTransportMode.StreamableHttp,
				AdditionalHeaders = new Dictionary<string, string>
				{
					["Authorization"] = BasicHeader(character, password)
				}
			},
			http);

		return await McpClient.CreateAsync(transport);
	}

	[Test]
	public async Task Mcp_ListTools_IncludesAllTools()
	{
		var password = "Correct-Horse-3!";
		var character = await CreateCharacterWithPasswordAsync(password);

		await using var client = await ConnectAsync(character, password);
		var names = (await client.ListToolsAsync()).Select(t => t.Name).ToList();

		foreach (var expected in new[]
		{
			"validate", "format", "hover", "complete", "signature_help",
			"document_symbols", "open_document", "close_document"
		})
		{
			await Assert.That(names).Contains(expected);
		}
	}

	[Test]
	public async Task Mcp_Validate_FlagsBrokenSoftcode_AndAcceptsValidSoftcode()
	{
		var password = "Correct-Horse-4!";
		var character = await CreateCharacterWithPasswordAsync(password);

		await using var client = await ConnectAsync(character, password);

		var broken = await client.CallToolAsync(
			"validate",
			new Dictionary<string, object?> { ["code"] = "add(" });
		var valid = await client.CallToolAsync(
			"validate",
			new Dictionary<string, object?> { ["code"] = "add(1,2)" });

		var brokenJson = JsonSerializer.Serialize(broken);
		var validJson = JsonSerializer.Serialize(valid);

		// Broken softcode surfaces at least one diagnostic; valid softcode is clean.
		await Assert.That(brokenJson).Contains("Severity");
		await Assert.That(validJson).DoesNotContain("Severity");
	}

	[Test]
	public async Task Mcp_Format_NormalizesSoftcode()
	{
		var password = "Correct-Horse-5!";
		var character = await CreateCharacterWithPasswordAsync(password);

		await using var client = await ConnectAsync(character, password);

		var result = await client.CallToolAsync(
			"format",
			new Dictionary<string, object?> { ["code"] = "  add(1,2,3)  " });

		await Assert.That(JsonSerializer.Serialize(result)).Contains("add(1, 2, 3)");
	}

	[Test]
	public async Task Mcp_DocumentSymbols_OutlinesAttributeDefinition()
	{
		var password = "Correct-Horse-6!";
		var character = await CreateCharacterWithPasswordAsync(password);

		await using var client = await ConnectAsync(character, password);

		var result = await client.CallToolAsync(
			"document_symbols",
			new Dictionary<string, object?> { ["code"] = "&greeting think hello" });

		await Assert.That(JsonSerializer.Serialize(result)).Contains("greeting");
	}

	[Test]
	public async Task Mcp_SessionHandles_ValidateByDocumentIdThenClose()
	{
		var password = "Correct-Horse-7!";
		var character = await CreateCharacterWithPasswordAsync(password);

		await using var client = await ConnectAsync(character, password);

		var opened = await client.CallToolAsync(
			"open_document",
			new Dictionary<string, object?> { ["code"] = "add(" });
		var documentId = opened.Content.OfType<TextContentBlock>().First().Text;

		var validated = await client.CallToolAsync(
			"validate",
			new Dictionary<string, object?> { ["documentId"] = documentId });
		await Assert.That(JsonSerializer.Serialize(validated)).Contains("Severity");

		var closed = await client.CallToolAsync(
			"close_document",
			new Dictionary<string, object?> { ["documentId"] = documentId });
		await Assert.That(JsonSerializer.Serialize(closed)).Contains("true");
	}
}
