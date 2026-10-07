using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests.Infrastructure;
using System.Collections.Concurrent;
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
/// <remarks>
/// Exclusive because these routes enumerate every player in the database. Eight other suites create
/// players, and a player that lsearch() already returns but whose creation has not finished is one
/// whose name and objid cannot yet be resolved — so the row renders as #-1, json_array rejects the
/// batch, and the locate failures land in the response body, because everything notified while an
/// HTTP request is served becomes that body.
///
/// That is what CI kept failing on, and why nothing reproduced it: by the time anything could ask,
/// the creations had finished and every stage of the route evaluated perfectly. It appeared when
/// this branch added more player-creating suites and the interleaving got wider, on a runner slow
/// enough to hold the window open.
///
/// A test that reads the whole database cannot run while other tests are writing to it.
/// </remarks>
[NotInParallel]
[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
public class ProfileApiTests(ServerWebAppFactory factory)
{
	/// <summary>Resolves #1 (God)'s name and objid from the engine, so tests aren't tied to literals.</summary>
	private async Task<(string Name, string Objid)> GodIdentity()
	{
		var mediator = factory.Services.GetRequiredService<IMediator>();
		var obj = (await mediator.Send(new GetObjectNodeQuery(new DBRef(1, null)))).Expect<AnySharpObject>().Object();
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
		// FN`CHARCAT default categorization: God carries the WIZARD flag in the seed.
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

		// A shredded row surfaces as a parse failure or an error string, not a usable array, and
		// JsonDocument.Parse reports only the offending byte — not enough to tell those apart
		// somewhere no debugger can reach. So the body is checked first and, only when it is wrong,
		// the route is taken apart to say which stage produced it: the route is a filter, an iter and
		// a json_array on one line, and any of them fails identically from outside. The stages are
		// evaluated as God while the route runs as #8, so the last probe asks which players #8 itself
		// cannot see — the answer that distinguishes a broken route from an invisible player.
		//
		// Built only on the failing path. These are six extra evaluations and a green run should not
		// pay for them.
		if (!body.TrimStart().StartsWith('['))
		{
			async Task<string> Probe(string expression) =>
				(await factory.FunctionParser.FunctionParse(MarkupText.Plain(expression)))!
					.Message!.ToPlainText().Replace("\n", "\\n");

			var players = await Probe("lsearch(all,type,player)");
			var visible = await Probe("filter(#8/FN`CHARVIS,lsearch(all,type,player))");
			var rows = await Probe(
				"iter(filter(#8/FN`CHARVIS,lsearch(all,type,player)),u(#8/FN`CHARROW,%i0),,%r)");
			var unseen = await Probe(
				"objeval(#8,iter(lsearch(all,type,player),switch(name(##),#-1*,##,)))");
			var unseenNames = await Probe(
				"iter(objeval(#8,iter(lsearch(all,type,player),switch(name(##),#-1*,##,))),name(##))");

			Assert.Fail($"the route must answer with a JSON array.\n"
				+ $"  players = [{players}]\n"
				+ $"  visible = [{visible}]\n"
				+ $"  rows    = [{rows}]\n"
				+ $"  unseen by #8 = [{unseen}] -> [{unseenNames}]\n"
				+ $"  body    = [{body.Replace("\n", "\\n")}]");
		}

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
	/// The package_manager ref is written into FN`CHARVIS as an objid when the package installs. If
	/// that objid stops resolving (the object was recreated, or the world replaced under the
	/// installed package), the predicate must still answer quietly. It used to locate the ref with
	/// num(), which notifies "I can't see that here." on a miss, once per player, and everything
	/// notified during a request becomes the response body: the directory came back as a stack of
	/// locate failures in front of the JSON.
	/// </summary>
	[Test]
	[Arguments("#7:1")]
	[Arguments("#999999:1")]
	public async Task Characters_StalePackageManagerRefStillAnswersJson(string staleRef)
	{
		var verb = staleRef.StartsWith("#7:") ? "STALEPMSEEDED" : "STALEPMMISSING";
		var manifest = SharpMUSH.Server.Services.BundledPackages.ManifestYaml("profile-handler");
		var charvis = System.Text.RegularExpressions.Regex
			.Match(manifest, @"FN`CHARVIS: \|-\r?\n\s+(?<body>.+)").Groups["body"].Value.Trim();
		await Assert.That(charvis).Contains("{{$package_manager}}");

		var mediator = factory.Services.GetRequiredService<IMediator>();
		var attributes = factory.Services.GetRequiredService<IAttributeService>();
		var god = (await mediator.Send(new GetObjectNodeQuery(new DBRef(1, null)))).Expect<AnySharpObject>();
		var handler = (await mediator.Send(new GetObjectNodeQuery(new DBRef(8, null)))).Expect<AnySharpObject>();
		(await attributes.SetAttributeAsync(god, handler, $"FN`{verb}",
			MarkupText.Plain(charvis.Replace("{{$package_manager}}", staleRef)))).Expect<Success>();
		(await attributes.SetAttributeAsync(god, handler, verb, MarkupText.Plain(
			$"@respond/type application/json; think json_array(iter(filter(me/FN`{verb},lsearch(all,type,player)),u(me/FN`CHARROW,%i0),,%r),%r)"))).Expect<Success>();

		var http = factory.CreateHttpClient();
		using var request = new HttpRequestMessage(new HttpMethod(verb), "http/characters");
		var response = await http.SendAsync(request);
		var body = await response.Content.ReadAsStringAsync();

		await Assert.That((int)response.StatusCode).IsEqualTo(200);
		await Assert.That(body).DoesNotContain("I can't see that here.");
		using var doc = JsonDocument.Parse(body);
		var names = doc.RootElement.EnumerateArray().Select(row => row.GetProperty("name").GetString()).ToList();
		await Assert.That(names).Contains((await GodIdentity()).Name);
		// The ref's dbref still names the seeded principal, so it stays hidden.
		if (staleRef.StartsWith("#7:")) await Assert.That(names).DoesNotContain("Package Manager");
	}

	/// <summary>
	/// GET /http/online reports presence, not the roster: it is built on mwho(), which reads the
	/// same connection registry WHO does, so it tracks who is actually here in both directions. The
	/// portal used to derive "players online" from the full character roster, which made every
	/// seeded player — including the Package Manager principal — look connected.
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

	/// <summary>
	/// One row per player, however many sockets they hold. mwho() lists players, so a second login
	/// for the same character adds no row — measured against a live server, this route returned the
	/// same objid eight times for one character, and the portal reported eight people online.
	/// </summary>
	/// <remarks>
	/// This changes the shared presence registry and reads the global online list. Inherit the
	/// class's global exclusion. A method-level key would override it and allow unrelated tests
	/// to overlap on TUnit 1.66, including notifications to the shared HTTP executor.
	/// </remarks>
	[Test]
	public async Task Online_ListsADoublyConnectedPlayerOnce()
	{
		var (godName, _) = await GodIdentity();
		var connectionService = factory.Services.GetRequiredService<IConnectionService>();

		// A second connection bound to the same character as the factory's own login.
		var handle = await TestIsolationHelpers.ConnectTestHandleAsync(connectionService, new DBRef(1, null), "websocket",
			new ConcurrentDictionary<string, string>(new Dictionary<string, string>
			{
				["ConnectionStartTime"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(),
				["LastConnectionSignal"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(),
				["InternetProtocolAddress"] = "127.0.0.1",
				["HostName"] = "localhost",
				["ConnectionType"] = "websocket",
				["PresenceClass"] = PresenceClasses.Play
			}));

		try
		{
			var http = factory.CreateHttpClient();
			var response = await http.GetAsync("http/online");
			var body = await response.Content.ReadAsStringAsync();

			await Assert.That((int)response.StatusCode).IsEqualTo(200);
			using var doc = JsonDocument.Parse(body);
			var rows = doc.RootElement.EnumerateArray()
				.Select(row => (Name: row.GetProperty("name").GetString(), Objid: row.GetProperty("objid").GetString()))
				.ToList();

			await Assert.That(rows.Count(r => r.Name == godName)).IsEqualTo(1);
			// The objid is the identity the portal deduplicates on, so it must be unique too.
			await Assert.That(rows.Select(r => r.Objid).Distinct().Count()).IsEqualTo(rows.Count);
		}
		finally
		{
			await connectionService.Disconnect(handle);
		}
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
		// Public read-only schema: one Identity section. Every element it declares must be one the
		// data route always fills — a declared-but-unfilled field renders as a blank row, which is
		// how a fresh character's profile came to be a table of empty labels.
		await Assert.That(sections.GetArrayLength()).IsEqualTo(1);
		await Assert.That(sections[0].GetProperty("name").GetString()).IsEqualTo("Identity");
		var keys = sections[0].GetProperty("elements").EnumerateArray()
			.Select(e => e.GetProperty("key").GetString()!).ToArray();
		await Assert.That(keys).IsEquivalentTo(new[] { "created", "objid" });
	}

	/// <summary>
	/// The schema and the data route have to agree: every key the schema declares must arrive with a
	/// non-empty value for a character that has done nothing but exist. This is the regression — the
	/// profile page rendered Full Name / Alias / Age / Concept / Status / Faction all blank because
	/// none of those attributes is set on a newly created character.
	/// </summary>
	[Test]
	public async Task ProfileSchemaAndData_Agree_EveryDeclaredFieldHasAValue()
	{
		var mediator = factory.Services.GetRequiredService<IMediator>();
		var home = new DBRef(0, null);
		await mediator.Send(new CreatePlayerCommand("SchemaFresh", "testpass", home, home, 1));
		var fresh = await mediator.CreateStream(new GetPlayerQuery("SchemaFresh")).FirstAsync();
		var objid = $"#{fresh.Object.Key}:{fresh.Object.CreationTime}";

		var http = factory.CreateHttpClient();
		using var schemaDoc = JsonDocument.Parse(
			await (await http.GetAsync("http/profile/schema")).Content.ReadAsStringAsync());
		using var dataDoc = JsonDocument.Parse(
			await (await http.GetAsync($"http/profile?objid={Uri.EscapeDataString(objid)}")).Content.ReadAsStringAsync());

		var declared = schemaDoc.RootElement.GetProperty("pages").EnumerateArray()
			.SelectMany(p => p.GetProperty("sections").EnumerateArray())
			.SelectMany(s => s.GetProperty("elements").EnumerateArray())
			.Where(e => e.GetProperty("kind").GetString() == "field")
			.Select(e => e.GetProperty("key").GetString()!)
			.ToList();

		await Assert.That(declared).IsNotEmpty();

		var fields = dataDoc.RootElement.GetProperty("fields");
		foreach (var key in declared)
		{
			await Assert.That(fields.TryGetProperty(key, out var field)).IsTrue();
			await Assert.That(field.GetProperty("visible").GetBoolean()).IsTrue();
			await Assert.That(field.GetProperty("value").GetString()).IsNotNullOrEmpty();
		}
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
		// Public fields arrive as {value, visible}. objid is repeated inside `fields` deliberately:
		// the schema renderer resolves element keys only against `fields`, not the envelope.
		var fields = doc.RootElement.GetProperty("fields");
		await Assert.That(fields.GetProperty("objid").GetProperty("value").GetString()).IsEqualTo(objid);
		await Assert.That(fields.GetProperty("objid").GetProperty("visible").GetBoolean()).IsTrue();
		await Assert.That(fields.GetProperty("created").GetProperty("value").GetString()).IsNotNullOrEmpty();
	}

	/// <summary>
	/// profile-handler 1.5 (spec 2026-09-29 §3): the profile carries the seeded image attributes and
	/// the opt-in name colour, in the same {value, visible} shape as every other field, and every
	/// directory row carries the portrait. banner never falls back to IMAGE: the avatar and the banner
	/// are separate pictures, and no banner is the portal's hue gradient. All three are read with get(): a URL is
	/// text the player typed, and evaluating it would run whatever they hid in it as the handler.
	/// </summary>
	[Test]
	public async Task ProfileAndDirectory_CarryImageBannerAndColor_ReadNotEvaluated()
	{
		var mediator = factory.Services.GetRequiredService<IMediator>();
		var connectionService = factory.Services.GetRequiredService<IConnectionService>();
		var home = new DBRef(0, null);
		var name = TestIsolationHelpers.GenerateUniqueName("ImgProfile");
		await mediator.Send(new CreatePlayerCommand(name, "testpass", home, home, 1));
		var player = await mediator.CreateStream(new GetPlayerQuery(name)).FirstAsync();
		var objid = $"#{player.Object.Key}:{player.Object.CreationTime}";
		var http = factory.CreateHttpClient();

		async Task Cmd(string command) =>
			await factory.CommandParser.CommandParse(1, connectionService, MarkupText.Plain(command));

		async Task<JsonDocument> Profile() =>
			JsonDocument.Parse(await (await http.GetAsync($"http/profile?objid={Uri.EscapeDataString(objid)}")).Content.ReadAsStringAsync());

		// Nothing set: the keys are there, visible, and blank — the portal draws its fallbacks.
		using (var blank = await Profile())
		{
			var fields = blank.RootElement.GetProperty("fields");
			foreach (var key in new[] { "image", "banner", "color" })
			{
				await Assert.That(fields.GetProperty(key).GetProperty("visible").GetBoolean()).IsTrue();
				await Assert.That(fields.GetProperty(key).GetProperty("value").GetString()).IsEqualTo(string.Empty);
			}
		}

		// A portrait with softcode in it must come back verbatim: get(), never u(). The colour lands
		// in a CSS custom property, so only #rrggbb is accepted: a hostile value reads as blank.
		await Cmd($"&IMAGE #{player.Object.Key}=/assets/chars/[name(me)].jpg");
		await Cmd($"&PROFILE`COLOR #{player.Object.Key}=#fff;background:url(x)");

		using (var hostile = await Profile())
		{
			await Assert.That(hostile.RootElement.GetProperty("fields").GetProperty("color").GetProperty("value").GetString()).IsEqualTo(string.Empty);
		}

		await Cmd($"&PROFILE`COLOR #{player.Object.Key}=#ffb454");

		using (var portrait = await Profile())
		{
			var fields = portrait.RootElement.GetProperty("fields");
			await Assert.That(fields.GetProperty("image").GetProperty("value").GetString()).IsEqualTo("/assets/chars/[name(me)].jpg");
			await Assert.That(fields.GetProperty("banner").GetProperty("value").GetString()).IsEqualTo(string.Empty)
				.Because("the avatar is not the banner");
			await Assert.That(fields.GetProperty("color").GetProperty("value").GetString()).IsEqualTo("#ffb454");
		}

		await Cmd($"&IMAGE`BANNER #{player.Object.Key}=/assets/chars/wide.jpg");

		using (var banner = await Profile())
		{
			var fields = banner.RootElement.GetProperty("fields");
			await Assert.That(fields.GetProperty("banner").GetProperty("value").GetString()).IsEqualTo("/assets/chars/wide.jpg");
			await Assert.That(fields.GetProperty("image").GetProperty("value").GetString()).IsEqualTo("/assets/chars/[name(me)].jpg");
		}

		using var directory = JsonDocument.Parse(await (await http.GetAsync("http/characters")).Content.ReadAsStringAsync());
		var rows = directory.RootElement.EnumerateArray()
			.ToDictionary(row => row.GetProperty("name").GetString()!, row => row);
		await Assert.That(rows[name].GetProperty("image").GetString()).IsEqualTo("/assets/chars/[name(me)].jpg");
		// Every row carries the key, blank for a character that has set nothing.
		await Assert.That(rows.Values.All(row => row.TryGetProperty("image", out var image) && image.ValueKind == JsonValueKind.String)).IsTrue();

		// The handler is a wizard, so get() reads a private attribute too. Only a visual image is
		// published: the seeded flags are defaults, and an IMAGE set before they existed, or with
		// visual cleared, keeps its owner's choice. A private banner reads as none, not as the IMAGE.
		await Cmd($"@set #{player.Object.Key}/IMAGE`BANNER=!visual");

		using (var privateBanner = await Profile())
		{
			var fields = privateBanner.RootElement.GetProperty("fields");
			await Assert.That(fields.GetProperty("banner").GetProperty("value").GetString()).IsEqualTo(string.Empty);
		}

		await Cmd($"@set #{player.Object.Key}/IMAGE=!visual");

		using (var privateImage = await Profile())
		{
			var fields = privateImage.RootElement.GetProperty("fields");
			await Assert.That(fields.GetProperty("image").GetProperty("value").GetString()).IsEqualTo(string.Empty);
			await Assert.That(fields.GetProperty("banner").GetProperty("value").GetString()).IsEqualTo(string.Empty);
		}

		using var hidden = JsonDocument.Parse(await (await http.GetAsync("http/characters")).Content.ReadAsStringAsync());
		var hiddenRow = hidden.RootElement.EnumerateArray().Single(row => row.GetProperty("name").GetString() == name);
		await Assert.That(hiddenRow.GetProperty("image").GetString()).IsEqualTo(string.Empty);

		// visual does not propagate down an attribute tree, and a private branch hides its leaves
		// (help ATTRIBUTE FLAGS2): a visual IMAGE`BANNER under a private IMAGE stays unpublished.
		await Cmd($"@set #{player.Object.Key}/IMAGE`BANNER=visual");

		using (var privateBranch = await Profile())
		{
			var fields = privateBranch.RootElement.GetProperty("fields");
			await Assert.That(fields.GetProperty("banner").GetProperty("value").GetString()).IsEqualTo(string.Empty);
		}
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
