using System.Text.Json;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// Wiring tests for the bundled <c>room-contents</c> package's ROOM`CONTENTS handler.
///
/// These tests install handlers on #9, fire ROOM`CONTENTS via EventService.TriggerEventAsync (the
/// same path movement uses), then assert on what the handler did.
///
/// They prove:
///   1. The ROOM`CONTENTS attribute on #9 is executed when the event fires.
///   2. %0 correctly carries the room dbref into the handler body.
///   3. lcon(%0) returns the room's occupants from within the handler context.
///   4. The package's own helpers build valid OOB v2 payloads, per viewer.
///
/// The first tests record lcon(%0)/words(lcon(%0)) into scratch attributes on #9 instead of calling
/// oob() — oob() requires WebSocket connections the unit harness lacks — so they assert fan-out
/// targeting logic. The payload tests go further: they install the REAL package attributes from
/// the embedded manifest and substitute only the handler, with one that records the payload a
/// helper builds for a named viewer into LAST_PAYLOAD instead of sending it. That exercises the
/// helpers exactly as the shipped handler calls them (executor #9, %0 the room), with the
/// send replaced by a store.
///
/// Each test cleans up in a finally block (via <see cref="RestorePackage"/>) so a failed assertion
/// cannot leak handler state into the next test; that cleanup reinstalls the package's attributes
/// rather than blanking them, because the package is bootstrapped onto #9 at first boot and the
/// rest of the session expects it there.
/// </summary>
[NotInParallel]
public class RoomContentsHandlerReferenceTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IEventService EventService => WebAppFactoryArg.Services.GetRequiredService<IEventService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	/// <summary>The attributes the shipped manifest installs on the event handler, by name.</summary>
	private static readonly Lazy<IReadOnlyDictionary<string, string>> PackageAttributes = new(() =>
		new PackageManifestService()
			.ParseManifest(BundledPackages.ManifestYaml("room-contents"))
			.Expect<ParsedPackageManifest>("the bundled room-contents manifest must parse")
			.Manifest.Objects.Single().Attributes
			.ToDictionary(a => a.Key, a => a.Value.Value));

	private Task Cmd(string command) =>
		WebAppFactoryArg.CommandParser.CommandParse(1, ConnectionService, MarkupText.Plain(command)).AsTask();

	private async Task<string> Eval(string expression) =>
		(await WebAppFactoryArg.FunctionParser.FunctionParse(MarkupText.Plain(expression)))!.Message!.ToPlainText();

	private Task Trigger(string room, string cause) =>
		EventService.TriggerEventAsync(
			WebAppFactoryArg.CommandParser,
			SharpEvents.RoomContents,
			WebAppFactoryArg.ExecutorDBRef,
			room,
			cause).AsTask();

	/// <summary>
	/// Installs (or reinstalls) every attribute the package manifest declares onto #9, verbatim: an
	/// <c>&amp;</c> typed at a client stores without evaluating, which is what the package installer
	/// does too.
	/// </summary>
	private async Task InstallPackage()
	{
		foreach (var (name, value) in PackageAttributes.Value)
		{
			await Cmd($"&{name} #9={value}");
		}
	}

	/// <summary>
	/// Clears every scratch attribute these tests write on #9 and puts the package's own attributes
	/// back. Runs in each test's finally so a failed assertion never leaks handler state into a
	/// later test (they run sequentially).
	/// </summary>
	private async Task RestorePackage()
	{
		foreach (var attr in new[]
		{
			"FANOUT_LIST", "FANOUT_COUNT", "LAST_CAUSE", "FANOUT_SENTINEL", "LAST_PAYLOAD",
			"FN`NOTEXIT", "FN`V1ROW",
		})
		{
			await Cmd($"&{attr} #9=");
		}

		await InstallPackage();
	}

	[Test]
	public async ValueTask HandlerReceivesCorrectRoomDbrefInPercent0()
	{
		try
		{
			// Install a simplified handler: record lcon(%0) (all occupants) into FANOUT_LIST
			// so we can verify %0 was the correct room and that lcon sees its contents.
			await Cmd("&ROOM`CONTENTS #9=&FANOUT_LIST #9=[lcon(%0)]");

			// Resolve God's (#1) current room — that is the room we'll pass as %0.
			var room = await Eval("loc(#1)");
			await Assert.That(room).StartsWith("#");

			// Fire the event via the same service path that movement/connect/disconnect use.
			// TriggerEventAsync runs the handler (#9) with its own permissions; #9 is seeded WIZARD,
			// so it retains the elevated (see-all) access this handler needs.
			await Trigger(room, "move-in");

			// Handler should have recorded lcon(room) into FANOUT_LIST on #9.
			var recorded = await Eval("get(#9/FANOUT_LIST)");

			// Independently compute lcon of the same room to verify handler saw same contents.
			var expected = await Eval($"lcon({room})");

			// This assertion FAILS if the handler did not run OR if %0 was not the correct room.
			await Assert.That(recorded).IsEqualTo(expected);

			// Sanity: God (#1) should appear in the room's contents (uses objid format).
			await Assert.That(recorded).Contains("#1");
		}
		finally
		{
			await RestorePackage();
		}
	}

	[Test]
	public async ValueTask HandlerCountsOccupantsMatchingIndependentLcon()
	{
		try
		{
			// Install a handler that records the occupant COUNT (via words()) for easier assertion.
			await Cmd("&ROOM`CONTENTS #9=&FANOUT_COUNT #9=[words(lcon(%0))]");

			var room = await Eval("loc(#1)");
			await Assert.That(room).StartsWith("#");

			await Trigger(room, "move-in");

			var recorded = await Eval("get(#9/FANOUT_COUNT)");
			var expected = await Eval($"words(lcon({room}))");

			// Recorded count must equal the independently computed lcon count.
			// Fails if the handler did not run, or ran against the wrong room.
			await Assert.That(recorded).IsEqualTo(expected);
		}
		finally
		{
			await RestorePackage();
		}
	}

	[Test]
	public async ValueTask HandlerCauseArgIsPassedAsPercent1()
	{
		try
		{
			// Install a handler that captures %1 (the cause) into LAST_CAUSE.
			await Cmd("&ROOM`CONTENTS #9=&LAST_CAUSE #9=%1");

			var room = await Eval("loc(#1)");
			var cause = "move-in";

			await Trigger(room, cause);

			var recorded = await Eval("get(#9/LAST_CAUSE)");

			// %1 must carry the cause string "move-in".
			await Assert.That(recorded).IsEqualTo(cause);
		}
		finally
		{
			await RestorePackage();
		}
	}

	[Test]
	public async ValueTask HandlerBuildsValidRoomContentsJsonPayload()
	{
		var token = Guid.NewGuid().ToString("N")[..8];
		var thingName = $"Probe{token}";
		try
		{
			// The previous tests prove the handler fires with the right room/cause, but not that
			// the v1 idioms (json_array + iter + filter + helper rows) actually produce VALID JSON for
			// the room's occupants. This exercises exactly that, with the 1.0 row shape.

			// A uniquely-named thing dropped into God's room becomes a who-list occupant.
			await Cmd($"@create {thingName}");
			await Cmd($"drop {thingName}");

			// Install the v1 helpers under scratch names, plus a handler that records the
			// room.contents payload (json_array of per-occupant rows) into LAST_PAYLOAD — oob() needs
			// a live WebSocket the harness lacks, so we capture the built payload instead of sending it.
			await Cmd("&FN`NOTEXIT #9=not(hastype(%0,exit))");
			await Cmd("&FN`V1ROW #9=json(object,dbref,json(string,[num(%0)]),name,json(string,name(%0)),cmd,json(string,look [num(%0)]))");
			await Cmd("&ROOM`CONTENTS #9=&LAST_PAYLOAD #9=json(object,who,json_array(iter(filter(#9/FN`NOTEXIT,lcon(%0)),u(#9/FN`V1ROW,itext(0)),%b,|),|))");

			var room = await Eval("loc(#1)");
			await Trigger(room, "move-in");

			// The core assertion the earlier tests missed: the handler emits VALID JSON.
			var valid = await Eval("isjson(get(#9/LAST_PAYLOAD))");
			var one = "1";
			await Assert.That(valid).IsEqualTo(one);

			// And the who list is built from the real occupants: it contains the unique thing and God.
			var payload = await Eval("get(#9/LAST_PAYLOAD)");
			await Assert.That(payload).Contains(thingName);
			await Assert.That(payload).Contains("\"dbref\":\"#1\"");
		}
		finally
		{
			await RestorePackage();
			await Cmd($"@dest/override {thingName}");
		}
	}

	[Test]
	public async ValueTask HandlerDoesNotRunAfterAttributeIsCleared()
	{
		try
		{
			// Install, then immediately clear. Trigger must not set FANOUT_SENTINEL.
			await Cmd("&ROOM`CONTENTS #9=&FANOUT_SENTINEL #9=ran");
			await Cmd("&ROOM`CONTENTS #9=");

			var room = await Eval("loc(#1)");
			await Trigger(room, "move-in");

			var sentinel = await Eval("get(#9/FANOUT_SENTINEL)");
			var emptyStr = string.Empty;
			// Sentinel must be empty because the handler was cleared before the trigger.
			await Assert.That(sentinel).IsEqualTo(emptyStr);
		}
		finally
		{
			await RestorePackage();
		}
	}

	// ── OOB v2 (room-contents 2.0) ─────────────────────────────────────────────────────────

	/// <summary>
	/// A room built for the v2 tests: two connected players (one a wizard), one asleep, two things
	/// (one with a picture), and four exits to a second room — plain, locked against everyone but
	/// God, DARK, and unlinked. Every name carries the token so nothing here can be confused with
	/// what another test built.
	/// </summary>
	private sealed record Fixture(
		string Room, string Dest, string Wizard, string Mortal, string Asleep,
		string Bundle, string Crate, string North, string Locked, string Dark, string Unlinked,
		long WizardHandle, long MortalHandle);

	/// <summary>Runs a side-effect builder and returns the new object's bare dbref (<c>#N</c>).</summary>
	private async Task<string> Build(string expression)
	{
		var created = await Eval(expression);
		await Assert.That(created).Matches(@"^#\d+(:\d+)?$").Because($"{expression} must answer a dbref or objid, got '{created}'");
		return created.Split(':')[0];
	}

	private async Task<Fixture> BuildFixture(string token)
	{
		// Side-effect builders hand back the new object's objid, and open(<exit>, <destination>,
		// <source>) opens in a named room, so nothing here moves God. The fixture keeps bare dbrefs
		// because that is what the rows' "dbref" carries (num()).
		var room = await Build($"dig(RcRoom{token})");
		var dest = await Build($"dig(RcDest{token})");
		var roomRef = new DBRef(int.Parse(room[1..]), null);

		await Cmd($"&IMAGE {room}=/assets/rooms/{token}.jpg");
		await Cmd($"&IMAGE`ALT {room}=The quay at dusk");
		await Cmd($"&IMAGE`FOCAL {room}=0.5 0.6");
		await Cmd($"&DESCRIBE {room}=Tarred pilings and stacked crates.");
		await Cmd($"&DESCRIBE {dest}=A lamplit street.");

		var wizard = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, "RcWiz", roomRef);
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, "RcMortal", roomRef);
		var asleep = await TestIsolationHelpers.CreateTestPlayerAsync(WebAppFactoryArg.Services, Mediator, "RcAsleep");
		await Cmd($"@set #{wizard.DbRef.Number}=WIZARD");
		await Cmd($"&PROFILE`COLOR #{mortal.DbRef.Number}=#ffb454");
		await Cmd($"&IMAGE #{mortal.DbRef.Number}=/assets/chars/{token}.jpg");

		var north = await Build($"open(north{token};n{token},{dest},{room})");
		var locked = await Build($"open(east{token};e{token},{dest},{room})");
		var dark = await Build($"open(secret{token},{dest},{room})");
		var unlinked = await Build($"open(west{token},,{room})");

		// Locked to the wizard player by identity, so the wizard passes and the mortal does not, whatever
		// the lock service thinks of the WIZARD flag.
		await Cmd($"@lock {locked}=#{wizard.DbRef.Number}");
		await Cmd($"@fail {locked}=Closed after dusk.");
		await Cmd($"&CONFIRM {north}=This leaves the scene.");
		await Cmd($"@set {dark}=DARK");

		var bundle = await Build($"create(Oilcloth bundle {token})");
		var crate = await Build($"create(Crate {token})");
		await Cmd($"&IMAGE {bundle}=/assets/obj/{token}.jpg");
		await Eval($"tel({bundle},{room})");
		await Eval($"tel({crate},{room})");
		foreach (var player in new[] { wizard.DbRef, mortal.DbRef, asleep })
		{
			await Eval($"tel(#{player.Number},{room})");
		}

		return new Fixture(room, dest, $"#{wizard.DbRef.Number}", $"#{mortal.DbRef.Number}", $"#{asleep.Number}",
			bundle, crate, north, locked, dark, unlinked, wizard.Handle, mortal.Handle);
	}

	/// <summary>
	/// Drops the handles and destroys the built objects. The players stay: a player whose handle was
	/// bound and then destroyed phantoms WHO for the rest of the session, and every other suite
	/// leaves its players too.
	/// </summary>
	private async Task TearDownFixture(Fixture f)
	{
		await ConnectionService.Disconnect(f.WizardHandle);
		await ConnectionService.Disconnect(f.MortalHandle);
		foreach (var obj in new[] { f.North, f.Locked, f.Dark, f.Unlinked, f.Bundle, f.Crate, f.Dest, f.Room })
		{
			await Cmd($"@dest/override {obj}");
		}
	}

	/// <summary>
	/// Runs the package's payload helper for <paramref name="viewer"/> through the real event path
	/// with the send replaced by a store, and parses what it built. The handler is the package's
	/// own but for the oob() call: <c>u(me/&lt;helper&gt;,%0,&lt;viewer&gt;)</c> is exactly the
	/// expression the shipped ROOM`CONTENTS hands to oob().
	/// </summary>
	private async Task<JsonDocument> Payload(string helper, string room, string viewer, string cause = "move-in")
	{
		await Cmd($"&ROOM`CONTENTS #9=&LAST_PAYLOAD #9=[u(me/{helper},%0,{viewer})]");
		await Trigger(room, cause);
		var payload = await Eval("get(#9/LAST_PAYLOAD)");
		await Assert.That(payload).DoesNotContain("#-1").Because($"{helper} for {viewer} produced an error: {payload}");
		return JsonDocument.Parse(payload);
	}

	private static Dictionary<string, JsonElement> RowsByDbref(JsonDocument doc, string list) =>
		doc.RootElement.GetProperty(list).EnumerateArray()
			.ToDictionary(row => row.GetProperty("dbref").GetString()!, row => row);

	[Test]
	public async ValueTask V2Contents_RowsCarryIdentityImagesStatusAndYou_PerViewer()
	{
		var token = Guid.NewGuid().ToString("N")[..8];
		Fixture? f = null;
		try
		{
			await InstallPackage();
			f = await BuildFixture(token);

			using var forMortal = await Payload("FN`PAYLOAD`CONTENTS", f.Room, f.Mortal);
			using var forWizard = await Payload("FN`PAYLOAD`CONTENTS", f.Room, f.Wizard);

			foreach (var doc in new[] { forMortal, forWizard })
			{
				await Assert.That(doc.RootElement.GetProperty("v").GetInt32()).IsEqualTo(2);
			}

			var mortalSees = RowsByDbref(forMortal, "who");
			var wizardSees = RowsByDbref(forWizard, "who");

			// Both connected players and both things are listed; the asleep player is not
			// (FN`WHOVIS: players only while CONNECTED — 1.0's isplayer() did not exist and let
			// every disconnected player through).
			foreach (var rows in new[] { mortalSees, wizardSees })
			{
				await Assert.That(rows.Keys).Contains(f.Mortal);
				await Assert.That(rows.Keys).Contains(f.Wizard);
				await Assert.That(rows.Keys).Contains(f.Bundle);
				await Assert.That(rows.Keys).Contains(f.Crate);
				await Assert.That(rows.Keys).DoesNotContain(f.Asleep);
				// lcon() includes exits on this engine; none may leak into who.
				await Assert.That(rows.Keys).DoesNotContain(f.North);
			}

			// "you" only on the viewer's own row, and only for that viewer.
			await Assert.That(mortalSees[f.Mortal].GetProperty("you").GetBoolean()).IsTrue();
			await Assert.That(mortalSees[f.Wizard].TryGetProperty("you", out _)).IsFalse();
			await Assert.That(wizardSees[f.Wizard].GetProperty("you").GetBoolean()).IsTrue();
			await Assert.That(wizardSees[f.Mortal].TryGetProperty("you", out _)).IsFalse();

			// v1 keys survive unchanged; v2 identity and type are added.
			var mortalRow = wizardSees[f.Mortal];
			await Assert.That(mortalRow.GetProperty("cmd").GetString()).IsEqualTo($"look {f.Mortal}");
			await Assert.That(mortalRow.GetProperty("objid").GetString()).StartsWith($"{f.Mortal}:");
			await Assert.That(mortalRow.GetProperty("type").GetString()).IsEqualTo("player");
			await Assert.That(wizardSees[f.Bundle].GetProperty("type").GetString()).IsEqualTo("thing");

			// Player extras: colour, status/idle, profile, and a Page action for everyone but yourself.
			await Assert.That(mortalRow.GetProperty("color").GetString()).IsEqualTo("#ffb454");
			await Assert.That(mortalRow.GetProperty("status").GetString()).IsEqualTo("active");
			await Assert.That(mortalRow.GetProperty("idle").ValueKind).IsEqualTo(JsonValueKind.Number);
			await Assert.That(mortalRow.GetProperty("profile").GetBoolean()).IsTrue();
			await Assert.That(mortalRow.GetProperty("actions")[0].GetProperty("cmd").GetString()).IsEqualTo($"page {f.Mortal}=");
			await Assert.That(wizardSees[f.Wizard].TryGetProperty("actions", out _)).IsFalse();
			await Assert.That(wizardSees[f.Wizard].TryGetProperty("color", out _)).IsFalse();

			// Images: {url, alt} from IMAGE / IMAGE`ALT (alt falls back to the name); no image key at all
			// on a row without IMAGE. Things carry none of the player-only keys.
			var bundle = wizardSees[f.Bundle];
			await Assert.That(bundle.GetProperty("image").GetProperty("url").GetString()).IsEqualTo($"/assets/obj/{token}.jpg");
			await Assert.That(bundle.GetProperty("image").GetProperty("alt").GetString()).IsEqualTo($"Oilcloth bundle {token}");
			await Assert.That(bundle.GetProperty("image").TryGetProperty("focal", out _)).IsFalse();
			await Assert.That(mortalRow.GetProperty("image").GetProperty("url").GetString()).IsEqualTo($"/assets/chars/{token}.jpg");
			var crate = wizardSees[f.Crate];
			foreach (var absent in new[] { "image", "status", "idle", "profile", "actions", "color", "you" })
			{
				await Assert.That(crate.TryGetProperty(absent, out _)).IsFalse().Because($"a thing row must not carry {absent}");
			}
		}
		finally
		{
			await RestorePackage();
			if (f is not null) await TearDownFixture(f);
		}
	}

	[Test]
	public async ValueTask V2Exits_LockedDarkAndUnlinked_AreReportedPerViewer()
	{
		var token = Guid.NewGuid().ToString("N")[..8];
		Fixture? f = null;
		try
		{
			await InstallPackage();
			f = await BuildFixture(token);

			using var forMortal = await Payload("FN`PAYLOAD`EXITS", f.Room, f.Mortal);
			using var forWizard = await Payload("FN`PAYLOAD`EXITS", f.Room, f.Wizard);

			await Assert.That(forMortal.RootElement.GetProperty("v").GetInt32()).IsEqualTo(2);
			var mortalSees = RowsByDbref(forMortal, "exits");
			var wizardSees = RowsByDbref(forWizard, "exits");

			// The DARK exit is omitted for the mortal and present for the wizard.
			await Assert.That(mortalSees.Keys).DoesNotContain(f.Dark);
			await Assert.That(wizardSees.Keys).Contains(f.Dark);

			// The locked exit: locked, with its @fail as the hint, for the mortal; open for the wizard,
			// who passes every lock. cmd stays on both.
			await Assert.That(mortalSees[f.Locked].GetProperty("state").GetString()).IsEqualTo("locked");
			await Assert.That(mortalSees[f.Locked].GetProperty("hint").GetString()).IsEqualTo("Closed after dusk.");
			await Assert.That(mortalSees[f.Locked].GetProperty("cmd").GetString()).IsEqualTo($"goto {f.Locked}");
			await Assert.That(wizardSees[f.Locked].GetProperty("state").GetString()).IsEqualTo("open");
			await Assert.That(wizardSees[f.Locked].TryGetProperty("hint", out _)).IsFalse();

			// The plain exit: open, aliases from the exit name, confirm from its CONFIRM attribute, and a
			// destination preview with the connected head count of the (empty) far room.
			var north = mortalSees[f.North];
			await Assert.That(north.GetProperty("state").GetString()).IsEqualTo("open");
			await Assert.That(north.GetProperty("name").GetString()).IsEqualTo($"north{token}");
			await Assert.That(north.GetProperty("aliases").EnumerateArray().Select(a => a.GetString())).Contains($"n{token}");
			await Assert.That(north.GetProperty("confirm").GetString()).IsEqualTo("This leaves the scene.");
			await Assert.That(north.GetProperty("objid").GetString()).StartsWith($"{f.North}:");
			var dest = north.GetProperty("dest");
			await Assert.That(dest.GetProperty("name").GetString()).IsEqualTo($"RcDest{token}");
			await Assert.That(dest.GetProperty("desc").GetString()).IsEqualTo("A lamplit street.");
			await Assert.That(dest.GetProperty("here").GetInt32()).IsEqualTo(0);
			await Assert.That(dest.TryGetProperty("image", out _)).IsFalse();
			await Assert.That(dest.TryGetProperty("area", out _)).IsFalse();

			// An exit that leads nowhere is closed and has no destination.
			await Assert.That(mortalSees[f.Unlinked].GetProperty("state").GetString()).IsEqualTo("closed");
			await Assert.That(mortalSees[f.Unlinked].TryGetProperty("dest", out _)).IsFalse();
			await Assert.That(mortalSees[f.Unlinked].GetProperty("aliases").GetArrayLength()).IsEqualTo(0);
		}
		finally
		{
			await RestorePackage();
			if (f is not null) await TearDownFixture(f);
		}
	}

	[Test]
	public async ValueTask V2Info_CarriesIdentityImageAndDescription()
	{
		var token = Guid.NewGuid().ToString("N")[..8];
		Fixture? f = null;
		try
		{
			await InstallPackage();
			f = await BuildFixture(token);

			using var info = await Payload("FN`PAYLOAD`INFO", f.Room, f.Mortal, "connect");
			var root = info.RootElement;

			await Assert.That(root.GetProperty("v").GetInt32()).IsEqualTo(2);
			await Assert.That(root.GetProperty("dbref").GetString()).IsEqualTo(f.Room);
			await Assert.That(root.GetProperty("objid").GetString()).StartsWith($"{f.Room}:");
			await Assert.That(root.GetProperty("name").GetString()).IsEqualTo($"RcRoom{token}");
			await Assert.That(root.GetProperty("desc").GetProperty("format").GetString()).IsEqualTo("text");
			await Assert.That(root.GetProperty("desc").GetProperty("text").GetString()).IsEqualTo("Tarred pilings and stacked crates.");

			// &IMAGE on the room yields image.url, alt from IMAGE`ALT, focal from IMAGE`FOCAL as numbers.
			var image = root.GetProperty("image");
			await Assert.That(image.GetProperty("url").GetString()).IsEqualTo($"/assets/rooms/{token}.jpg");
			await Assert.That(image.GetProperty("alt").GetString()).IsEqualTo("The quay at dusk");
			await Assert.That(image.GetProperty("focal").EnumerateArray().Select(n => n.GetDouble())).IsEquivalentTo([0.5, 0.6]);

			// No zone, no parent, no scene: the optional keys are absent, not null.
			await Assert.That(root.TryGetProperty("area", out _)).IsFalse();
			await Assert.That(root.TryGetProperty("scene", out _)).IsFalse();

			// Once the room has a parent, FN`AREA names it.
			await Cmd($"@parent {f.Room}={f.Dest}");
			using var withArea = await Payload("FN`PAYLOAD`INFO", f.Room, f.Mortal, "connect");
			await Assert.That(withArea.RootElement.GetProperty("area").GetString()).IsEqualTo($"RcDest{token}");

			// A room with no picture carries no image key.
			using var plain = await Payload("FN`PAYLOAD`INFO", f.Dest, f.Mortal, "connect");
			await Assert.That(plain.RootElement.TryGetProperty("image", out _)).IsFalse();
		}
		finally
		{
			await RestorePackage();
			if (f is not null) await TearDownFixture(f);
		}
	}

	/// <summary>
	/// The shipped handler itself, unmodified, run through the real event path against the fixture
	/// room for each cause. oob() delivers to nobody here (the harness has no WebSocket), so the
	/// assertion is that the handler runs to completion without an error reaching the handler
	/// object — the whole per-viewer iteration, every helper, every json_mod() patch.
	/// </summary>
	[Test]
	public async ValueTask ShippedHandler_RunsEndToEnd_ForEveryCause()
	{
		var token = Guid.NewGuid().ToString("N")[..8];
		Fixture? f = null;
		try
		{
			await InstallPackage();
			f = await BuildFixture(token);

			// null() swallows the handler's own output; anything else reaching #9 is an evaluation
			// error ("#-1 ...") or a locate failure ("I can't see that here."). Capture both by
			// recording what the handler evaluates to, with the send left in place.
			var handler = PackageAttributes.Value["ROOM`CONTENTS"];
			await Assert.That(handler).StartsWith("think null(");
			var body = handler["think null(".Length..^1];
			await Cmd($"&ROOM`CONTENTS #9=&LAST_PAYLOAD #9=[{body}]");

			foreach (var cause in new[] { "move-in", "move-out", "connect", "disconnect" })
			{
				await Cmd("&LAST_PAYLOAD #9=unset");
				await Trigger(f.Room, cause);
				var result = await Eval("get(#9/LAST_PAYLOAD)");
				// Two viewers, each sent room.contents and room.exits (0 deliveries each, no WebSocket),
				// plus room.info on the arrival causes: per viewer the counts concatenate to a run of
				// zeros, and iter() joins the viewers with its default space.
				await Assert.That(result).IsEqualTo(cause is "move-in" or "connect" ? "000 000" : "00 00")
					.Because($"{cause}: the handler must run every oob() cleanly, got '{result}'");
			}
		}
		finally
		{
			await RestorePackage();
			if (f is not null) await TearDownFixture(f);
		}
	}
}
