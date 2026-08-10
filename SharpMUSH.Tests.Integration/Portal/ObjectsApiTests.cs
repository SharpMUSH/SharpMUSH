using SharpMUSH.Tests.Infrastructure;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace SharpMUSH.Tests.Integration.Portal;

/// <summary>
/// The typed object/attribute API (<c>api/objects</c>) that the portal's Softcode Editor writes
/// through instead of the terminal.
///
/// The terminal channel is line-delimited, so the editor used to rewrite every newline as the two
/// characters <c>%r</c> before sending <c>&amp;ATTR #dbref=value</c>; because <c>&amp;</c> is
/// RSNoParse for direct input, that literal <c>%r</c> is what reached the database. Every other
/// write path in the server — <c>@set</c>, <c>&amp;</c> from a queue, package install — stores real
/// newlines. These tests pin the API to that majority behaviour: what you send is what is stored,
/// byte for byte, with the engine's own permission checks in front of it.
/// </summary>
[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
public class ObjectsApiTests(ServerWebAppFactory factory)
{
	private record CharacterSummary(int DbrefNumber, long CreationTime, string Name, string Flags);
	private record AccountLoginResponse(string AccountId, string Username, List<CharacterSummary> Characters, string AccountSessionToken, bool MustChangePassword);
	private record AccountRegisterRequest(string Username, string? Email, string Password);
	private record CreateCharacterRequest(string Name, string Password);
	private record CreatedCharacterResponse(int DbrefNumber, long CreationTime);

	private record AttributeDto(string Name, string Value, IReadOnlyList<string> Flags);
	private record SetAttributeRequest(string Value);
	private record ObjectSummaryDto(string Dbref, string Name, string Type, string Owner, IReadOnlyList<string> Flags);
	private record CreateObjectRequest(string Name, string Type, string? Destination = null);
	private record CreatedObjectDto(string Dbref);

	private const string Password = "Integration-Test-Pw-1!";

	/// <summary>
	/// Pinned to https: the server uses UseHttpsRedirection, and following the 307 from http→https
	/// makes HttpClient drop the Authorization header.
	/// </summary>
	private HttpClient CreateClient()
	{
		var http = factory.CreateHttpClient();
		http.BaseAddress = new Uri("https://localhost/");
		return http;
	}

	private static string UniqueName(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

	/// <summary>An account with exactly one character, and a client bearing its session token.</summary>
	private async Task<(HttpClient Http, int Dbref)> CharacterAsync(string prefix)
	{
		var http = CreateClient();

		var registered = await http.PostAsJsonAsync(
			"api/auth/account-register", new AccountRegisterRequest(UniqueName(prefix), null, Password));
		await Assert.That(registered.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var account = (await registered.Content.ReadFromJsonAsync<AccountLoginResponse>())!;

		http.DefaultRequestHeaders.Authorization =
			new AuthenticationHeaderValue("Bearer", account.AccountSessionToken);

		var created = await http.PostAsJsonAsync(
			"api/account/characters", new CreateCharacterRequest(UniqueName($"{prefix}c"), Password));
		await Assert.That(created.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var character = (await created.Content.ReadFromJsonAsync<CreatedCharacterResponse>())!;

		return (http, character.DbrefNumber);
	}

	private static string AttrUrl(int dbref, string attribute)
		=> $"api/objects/{dbref}/attributes/{Uri.EscapeDataString(attribute)}";

	/// <summary>
	/// The headline case. A three-line value survives the round trip unchanged — no %r anywhere,
	/// no lost indentation, no truncation at the first newline.
	/// </summary>
	[Test]
	public async Task SetAttribute_MultiLineValue_RoundTripsByteExact()
	{
		var (http, dbref) = await CharacterAsync("multiline");
		const string value = "$greet *:@pemit %#=Hi;\n  @pemit %#=There;\n  @pemit %#=Bye";

		var put = await http.PutAsJsonAsync(AttrUrl(dbref, "CMD_GREET"), new SetAttributeRequest(value));
		await Assert.That(put.StatusCode).IsEqualTo(HttpStatusCode.NoContent);

		var read = await http.GetFromJsonAsync<AttributeDto>(AttrUrl(dbref, "CMD_GREET"));

		await Assert.That(read!.Value).IsEqualTo(value);
	}

	/// <summary>
	/// The other half of dropping the conversion: a %r the author actually typed is content, not a
	/// newline, and must come back as the two characters they wrote.
	/// </summary>
	[Test]
	public async Task SetAttribute_LiteralPercentR_IsStoredLiterally()
	{
		var (http, dbref) = await CharacterAsync("literalpr");
		const string value = "line one%rline two";

		await http.PutAsJsonAsync(AttrUrl(dbref, "DESC_LITERAL"), new SetAttributeRequest(value));
		var read = await http.GetFromJsonAsync<AttributeDto>(AttrUrl(dbref, "DESC_LITERAL"));

		await Assert.That(read!.Value).IsEqualTo(value);
		await Assert.That(read.Value).DoesNotContain("\n");
	}

	/// <summary>Attribute trees address by backtick, which has to survive URL routing.</summary>
	[Test]
	public async Task SetAttribute_AttributeTreeLeaf_RoundTrips()
	{
		var (http, dbref) = await CharacterAsync("attrtree");
		const string value = "leaf body\nsecond line";

		var put = await http.PutAsJsonAsync(AttrUrl(dbref, "BRANCH`LEAF"), new SetAttributeRequest(value));
		await Assert.That(put.StatusCode).IsEqualTo(HttpStatusCode.NoContent);

		var read = await http.GetFromJsonAsync<AttributeDto>(AttrUrl(dbref, "BRANCH`LEAF"));
		await Assert.That(read!.Value).IsEqualTo(value);
	}

	[Test]
	public async Task DeleteAttribute_RemovesIt()
	{
		var (http, dbref) = await CharacterAsync("delattr");

		await http.PutAsJsonAsync(AttrUrl(dbref, "TRANSIENT"), new SetAttributeRequest("here"));
		var deleted = await http.DeleteAsync(AttrUrl(dbref, "TRANSIENT"));
		await Assert.That(deleted.StatusCode).IsEqualTo(HttpStatusCode.NoContent);

		var read = await http.GetAsync(AttrUrl(dbref, "TRANSIENT"));
		await Assert.That(read.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
	}

	[Test]
	public async Task ListAttributes_IncludesWhatWasJustWritten()
	{
		var (http, dbref) = await CharacterAsync("listattr");

		await http.PutAsJsonAsync(AttrUrl(dbref, "LISTED_ONE"), new SetAttributeRequest("first"));

		var all = await http.GetFromJsonAsync<List<AttributeDto>>($"api/objects/{dbref}/attributes");

		await Assert.That(all!.Any(a => a.Name.Equals("LISTED_ONE", StringComparison.OrdinalIgnoreCase))).IsTrue()
			.Because($"got: [{string.Join(", ", all!.Select(a => a.Name))}]");
	}

	/// <summary>
	/// Creation runs the real <c>@create</c> through the engine rather than reimplementing it, so
	/// the object it returns is a genuine one: addressable, and immediately writable through the
	/// rest of this API.
	/// </summary>
	[Test]
	public async Task CreateObject_ReturnsAUsableThing()
	{
		var (http, _) = await CharacterAsync("createobj");
		var name = UniqueName("Widget");

		var created = await http.PostAsJsonAsync("api/objects", new CreateObjectRequest(name, "THING"));
		await Assert.That(created.StatusCode).IsEqualTo(HttpStatusCode.OK)
			.Because($"body: [{await created.Content.ReadAsStringAsync()}]");

		var dto = (await created.Content.ReadFromJsonAsync<CreatedObjectDto>())!;
		var number = int.Parse(dto.Dbref.TrimStart('#').Split(':')[0]);

		var summary = await http.GetFromJsonAsync<ObjectSummaryDto>($"api/objects/{number}");
		await Assert.That(summary!.Name).IsEqualTo(name);
		await Assert.That(summary.Type).IsEqualTo("THING");
	}

	/// <summary>
	/// The name never becomes part of a command line, so a separator in it is inert data rather
	/// than a second command. <c>NameRegex</c> does not forbid ';', which is exactly why the
	/// endpoint passes the name as a pre-split argument instead of building "@create {name}".
	/// </summary>
	[Test]
	public async Task CreateObject_NameContainingASeparator_IsNotTreatedAsASecondCommand()
	{
		var (http, _) = await CharacterAsync("createinj");
		var name = $"{UniqueName("Inj")};@dig Injected";

		var created = await http.PostAsJsonAsync("api/objects", new CreateObjectRequest(name, "THING"));
		await Assert.That(created.StatusCode).IsEqualTo(HttpStatusCode.OK)
			.Because($"body: [{await created.Content.ReadAsStringAsync()}]");

		var dto = (await created.Content.ReadFromJsonAsync<CreatedObjectDto>())!;
		var number = int.Parse(dto.Dbref.TrimStart('#').Split(':')[0]);

		var summary = await http.GetFromJsonAsync<ObjectSummaryDto>($"api/objects/{number}");
		await Assert.That(summary!.Name).IsEqualTo(name)
			.Because("the whole string is the name; nothing after the ';' may execute");
	}

	[Test]
	public async Task GetObject_ReportsNameAndType()
	{
		var (http, dbref) = await CharacterAsync("objinfo");

		var summary = await http.GetFromJsonAsync<ObjectSummaryDto>($"api/objects/{dbref}");

		await Assert.That(summary!.Type).IsEqualTo("PLAYER");
		await Assert.That(summary.Name).IsNotNull().And.IsNotEmpty();
	}

}
