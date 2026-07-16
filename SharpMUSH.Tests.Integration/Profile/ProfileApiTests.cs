using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Tests.Infrastructure;
using System.Net;
using System.Text.Json;

namespace SharpMUSH.Tests.Integration.Profile;

/// <summary>
/// End-to-end tests for the default character-profile and character-directory softcode, served
/// through the routed http_handler (help sharphttp). The bootstrap seeds the GET verb router and
/// the GET`CHARACTERS / GET`PROFILE`SCHEMA / GET`PROFILE sub-attributes onto #8 at startup;
/// characters are addressed by objid via a query parameter, and real HTTP statuses come from
/// @respond — there is no JSON status envelope.
/// </summary>
[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
public class ProfileApiTests(ServerWebAppFactory factory)
{
	/// <summary>Resolves #1 (God)'s name and objid from the engine, so tests aren't tied to literals.</summary>
	private async Task<(string Name, string Objid)> GodIdentity()
	{
		var mediator = factory.Services.GetRequiredService<IMediator>();
		var god = await mediator.Send(new GetObjectNodeQuery(new DBRef(1, null)));
		await Assert.That(god.IsNone).IsFalse();
		var obj = god.Known.Object();
		return (obj.Name, $"#{obj.Key}:{obj.CreationTime}");
	}

	[Test]
	public async Task Characters_ListsEveryPlayerWithObjid()
	{
		var (name, objid) = await GodIdentity();

		var http = factory.CreateHttpClient();
		var response = await http.GetAsync("http/characters");
		var body = await response.Content.ReadAsStringAsync();

		await Assert.That((int)response.StatusCode).IsEqualTo(200);
		using var doc = JsonDocument.Parse(body);
		await Assert.That(doc.RootElement.ValueKind).IsEqualTo(JsonValueKind.Array);

		var god = doc.RootElement.EnumerateArray()
			.FirstOrDefault(row => row.GetProperty("name").GetString() == name);
		await Assert.That(god.ValueKind).IsEqualTo(JsonValueKind.Object);
		await Assert.That(god.GetProperty("objid").GetString()).IsEqualTo(objid);
		await Assert.That(god.GetProperty("created").ValueKind).IsEqualTo(JsonValueKind.Number);
		// FN`CHARCAT default categorization: God carries the WIZARD flag in every provider's seed.
		await Assert.That(god.GetProperty("category").GetString()).IsEqualTo("Wizard");
	}

	/// <summary>
	/// Covers the default FN`CHARCAT / FN`CHARVIS policy beyond the Wizard branch: a ROYALTY
	/// player categorizes as Royalty, a flagless player has a blank category, and a Guest-powered
	/// player is filtered out of the directory entirely (MUSH-side, via filter(me/FN`CHARVIS,…)).
	/// </summary>
	[Test]
	public async Task Characters_CategorizesByFlags_AndHidesGuests()
	{
		var mediator = factory.Services.GetRequiredService<IMediator>();
		var home = new DBRef(0, null);

		await mediator.Send(new CreatePlayerCommand("DirRoyal", "testpass", home, home, 1));
		await mediator.Send(new CreatePlayerCommand("DirPleb", "testpass", home, home, 1));
		await mediator.Send(new CreatePlayerCommand("DirGuest", "testpass", home, home, 1));

		var royal = await mediator.CreateStream(new GetPlayerQuery("DirRoyal")).FirstAsync();
		var royaltyFlag = await mediator.Send(new GetObjectFlagQuery("ROYALTY"));
		await Assert.That(royaltyFlag).IsNotNull();
		await mediator.Send(new SetObjectFlagCommand(new AnySharpObject(royal), royaltyFlag!));

		var guest = await mediator.CreateStream(new GetPlayerQuery("DirGuest")).FirstAsync();
		var guestPower = await mediator.Send(new GetPowerQuery("Guest"));
		await Assert.That(guestPower).IsNotNull();
		await mediator.Send(new SetObjectPowerCommand(new AnySharpObject(guest), guestPower!));

		var http = factory.CreateHttpClient();
		var response = await http.GetAsync("http/characters");
		var body = await response.Content.ReadAsStringAsync();

		await Assert.That((int)response.StatusCode).IsEqualTo(200);
		using var doc = JsonDocument.Parse(body);
		var rows = doc.RootElement.EnumerateArray()
			.ToDictionary(row => row.GetProperty("name").GetString()!, row => row);

		await Assert.That(rows["DirRoyal"].GetProperty("category").GetString()).IsEqualTo("Royalty");
		await Assert.That(rows["DirPleb"].GetProperty("category").GetString()).IsEqualTo(string.Empty);
		await Assert.That(rows.ContainsKey("DirGuest")).IsFalse();
	}

	/// <summary>
	/// Both routes assemble their array with json_array(iter(...)), and json_array() splits its
	/// input BEFORE parsing each element — so a row containing the separator is shredded into
	/// invalid JSON. Rows embed the player's name, names contain spaces, and space is
	/// json_array()'s default separator; the routes pass %r instead. This pins that choice: a
	/// player whose name has a space must survive the round trip intact.
	/// </summary>
	[Test]
	public async Task Characters_HandlesNamesContainingTheDefaultSeparator()
	{
		var mediator = factory.Services.GetRequiredService<IMediator>();
		var home = new DBRef(0, null);
		await mediator.Send(new CreatePlayerCommand("Spaced Out Name", "testpass", home, home, 1));

		var http = factory.CreateHttpClient();
		var response = await http.GetAsync("http/characters");
		var body = await response.Content.ReadAsStringAsync();

		await Assert.That((int)response.StatusCode).IsEqualTo(200);
		// A shredded row surfaces as a parse failure or an error string, not a usable array.
		using var doc = JsonDocument.Parse(body);
		await Assert.That(doc.RootElement.ValueKind).IsEqualTo(JsonValueKind.Array);

		var names = doc.RootElement.EnumerateArray()
			.Select(row => row.GetProperty("name").GetString())
			.ToList();

		await Assert.That(names).Contains("Spaced Out Name");
	}

	/// <summary>
	/// #7 Package Manager is seeded as a real PLAYER (it owns softcode-package objects), so a
	/// type-based roster picks it up even though nobody plays it. FN`CHARVIS excludes it by the
	/// {{$package_manager}} config ref rather than a literal #7 — the seed numbering is
	/// config-driven and has moved before (#3 → #7).
	/// </summary>
	[Test]
	public async Task Characters_HidesThePackageManagerSystemPrincipal()
	{
		var http = factory.CreateHttpClient();
		var response = await http.GetAsync("http/characters");
		var body = await response.Content.ReadAsStringAsync();

		await Assert.That((int)response.StatusCode).IsEqualTo(200);
		using var doc = JsonDocument.Parse(body);
		var names = doc.RootElement.EnumerateArray()
			.Select(row => row.GetProperty("name").GetString())
			.ToList();

		await Assert.That(names).DoesNotContain("Package Manager");
	}

	/// <summary>
	/// GET /http/online is the connection list, not the roster: it is built on lwho(), the same
	/// registry WHO reads, so it tracks actual connections in both directions. The portal used to
	/// derive "players online" from the full character roster, which made every seeded player —
	/// including the Package Manager principal — look connected.
	/// The shared factory logs God in, so God is the connected fixture here; a freshly created
	/// player that never binds a connection is the unconnected one.
	/// </summary>
	[Test]
	public async Task Online_ListsConnectedPlayersOnly()
	{
		var (godName, _) = await GodIdentity();
		var mediator = factory.Services.GetRequiredService<IMediator>();
		var home = new DBRef(0, null);
		await mediator.Send(new CreatePlayerCommand("OnlineNobody", "testpass", home, home, 1));

		var http = factory.CreateHttpClient();
		var response = await http.GetAsync("http/online");
		var body = await response.Content.ReadAsStringAsync();

		await Assert.That((int)response.StatusCode).IsEqualTo(200);
		await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("application/json");
		using var doc = JsonDocument.Parse(body);
		await Assert.That(doc.RootElement.ValueKind).IsEqualTo(JsonValueKind.Array);

		var names = doc.RootElement.EnumerateArray()
			.Select(row => row.GetProperty("name").GetString())
			.ToList();

		// Holds a connection in this session → present. Proves the route reports real presence
		// rather than just returning an empty array.
		await Assert.That(names).Contains(godName);
		// Exist but never bound a connection → absent. This is what the roster-backed widget got wrong.
		await Assert.That(names).DoesNotContain("OnlineNobody");
		await Assert.That(names).DoesNotContain("Package Manager");
	}

	[Test]
	public async Task ProfileSchema_ReturnsSectionsJson()
	{
		var http = factory.CreateHttpClient();
		var response = await http.GetAsync("http/profile/schema");
		var body = await response.Content.ReadAsStringAsync();

		await Assert.That((int)response.StatusCode).IsEqualTo(200);
		await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("application/json");
		using var doc = JsonDocument.Parse(body);
		// Schema-driven view (PortalSchemaDocument shape): sections live under pages[].sections.
		await Assert.That(doc.RootElement.TryGetProperty("pages", out var pages)).IsTrue();
		await Assert.That(pages.GetArrayLength()).IsEqualTo(1);
		await Assert.That(pages[0].TryGetProperty("sections", out var sections)).IsTrue();
		// Public read-only schema: Demographics + Status + Description.
		await Assert.That(sections.GetArrayLength()).IsEqualTo(3);
	}

	[Test]
	public async Task ProfileGet_ByObjid_ReturnsPublicProfile()
	{
		var (name, objid) = await GodIdentity();

		var http = factory.CreateHttpClient();
		var response = await http.GetAsync($"http/profile?objid={Uri.EscapeDataString(objid)}");
		var body = await response.Content.ReadAsStringAsync();

		await Assert.That((int)response.StatusCode).IsEqualTo(200);
		using var doc = JsonDocument.Parse(body);
		await Assert.That(doc.RootElement.GetProperty("character").GetString()).IsEqualTo(name);
		await Assert.That(doc.RootElement.GetProperty("objid").GetString()).IsEqualTo(objid);
		// All public fields are present as {value, visible} even when unset.
		var fields = doc.RootElement.GetProperty("fields");
		await Assert.That(fields.TryGetProperty("fullname", out var fullname)).IsTrue();
		await Assert.That(fullname.GetProperty("visible").GetBoolean()).IsTrue();
	}

	[Test]
	public async Task ProfileGet_UnknownObjid_Returns404()
	{
		var http = factory.CreateHttpClient();
		var response = await http.GetAsync("http/profile?objid=%23999999:12345");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
		await Assert.That(response.ReasonPhrase ?? string.Empty).Contains("NO SUCH CHARACTER");
	}

	[Test]
	public async Task ProfileGet_MissingObjidParam_Returns404()
	{
		var http = factory.CreateHttpClient();
		var response = await http.GetAsync("http/profile");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
	}
}
