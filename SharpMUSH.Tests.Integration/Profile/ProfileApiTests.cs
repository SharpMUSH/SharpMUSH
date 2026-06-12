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
/// the GET`CHARACTERS / GET`PROFILE`SCHEMA / GET`PROFILE sub-attributes onto #4 at startup;
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

	[Test]
	public async Task ProfileSchema_ReturnsSectionsJson()
	{
		var http = factory.CreateHttpClient();
		var response = await http.GetAsync("http/profile/schema");
		var body = await response.Content.ReadAsStringAsync();

		await Assert.That((int)response.StatusCode).IsEqualTo(200);
		await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("application/json");
		using var doc = JsonDocument.Parse(body);
		await Assert.That(doc.RootElement.TryGetProperty("sections", out var sections)).IsTrue();
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
