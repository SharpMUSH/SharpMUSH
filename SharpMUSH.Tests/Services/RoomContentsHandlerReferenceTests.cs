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
/// targeting logic. They replace the session-wide handler on #9, which every move in every test
/// fires, so they run one at a time and each puts the package back in a finally block (via
/// <see cref="RestorePackage"/>), reinstalling rather than blanking it because the package is
/// bootstrapped onto #9 at first boot and the rest of the session expects it there.
///
/// The payload tests go further: they install the REAL package attributes from the embedded
/// manifest onto a WIZARD thing of their own — #9 is seeded WIZARD, and the helpers name only
/// <c>me/</c> — and call a helper for a named viewer with u(), which keeps God as the enactor the
/// way the event path does. That exercises the helpers as the shipped handler calls them (%0 the
/// room, %1 the viewer) without touching #9, so they run in parallel.
/// </summary>
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

	/// <summary>Raises ROOM`CONTENTS as God, and returns once the queued handler has run.</summary>
	private Task Trigger(string room, string cause) => Raise(WebAppFactoryArg.ExecutorDBRef, room, cause);

	/// <summary>Raises ROOM`CONTENTS as <paramref name="enactor"/>, and returns once the queued handler has run.</summary>
	private async Task Raise(DBRef enactor, string room, string cause)
	{
		await EventService.TriggerEventAsync(SharpEvents.RoomContents, enactor, room, cause);
		await WebAppFactoryArg.QueueBarrierAsync();
	}

	/// <summary>
	/// Installs (or reinstalls) every attribute the package manifest declares onto
	/// <paramref name="target"/>, verbatim: an <c>&amp;</c> typed at a client stores without
	/// evaluating, which is what the package installer does too.
	/// </summary>
	private async Task InstallPackage(string target = "#9")
	{
		foreach (var (name, value) in PackageAttributes.Value)
		{
			await Cmd($"&{name} {target}={value}");
		}
	}

	/// <summary>A WIZARD thing carrying the package's attributes, standing in for #9.</summary>
	private async Task<string> BuildHandler(string token)
	{
		var handler = await Build($"create(RcHandler{token})");
		await Cmd($"@set {handler}=WIZARD");
		await InstallPackage(handler);
		return handler;
	}

	/// <summary>
	/// Clears every scratch attribute the event-path tests write on #9 and puts the package's own
	/// attributes back. Runs in each test's finally so a failed assertion never leaks handler state
	/// into a later test.
	/// </summary>
	private async Task RestorePackage()
	{
		foreach (var attr in new[]
		{
			"FANOUT_LIST", "FANOUT_COUNT", "LAST_CAUSE", "FANOUT_SENTINEL", "LAST_PAYLOAD",
			"FN`NOTEXIT", "FN`V1ROW",
		})
		{
			await Cmd($"@wipe #9/{attr}");
		}

		await InstallPackage();
	}

	/// <summary>
	/// A room of the test's own holding one thing, built without moving God. A room another test can
	/// walk into (God's, say) has ROOM`CONTENTS queued for it by that test too, so a handler recording
	/// what it saw there could record that test's event instead of this one's.
	/// </summary>
	private async Task<(string Room, string Thing)> ProbeRoom()
	{
		var token = Guid.NewGuid().ToString("N")[..8];
		var room = await Build($"dig(RcProbe{token})");
		var thing = await Build($"create(RcProbeThing{token})");
		await Eval($"tel({thing},{room})");
		return (room, thing);
	}

	private async Task RemoveProbeRoom((string Room, string Thing) probe)
	{
		await Cmd($"@dest/override {probe.Thing}");
		await Cmd($"@dest/override {probe.Room}");
	}

	[Test]
	[NotInParallel]
	public async ValueTask HandlerReceivesCorrectRoomDbrefInPercent0()
	{
		var probe = await ProbeRoom();
		try
		{
			// Install a simplified handler: record lcon(%0) (all occupants) under the room's number, so
			// we can verify %0 was the correct room and that lcon sees its contents.
			await Cmd("&ROOM`CONTENTS #9=&FANOUT_LIST`[after(first(%0,:),#)] #9=[lcon(%0)]");

			// Fire the event via the same service path that movement/connect/disconnect use.
			// The handler (#9) runs with its own permissions; #9 is seeded WIZARD, so it retains the
			// elevated (see-all) access this handler needs.
			await Trigger(probe.Room, "move-in");

			var recorded = await Eval($"get(#9/FANOUT_LIST`{probe.Room[1..]})");

			// Independently compute lcon of the same room to verify handler saw same contents.
			var expected = await Eval($"lcon({probe.Room})");

			// This assertion FAILS if the handler did not run OR if %0 was not the correct room.
			await Assert.That(recorded).IsEqualTo(expected);
			await Assert.That(recorded).Contains(probe.Thing);
		}
		finally
		{
			await RestorePackage();
			await RemoveProbeRoom(probe);
		}
	}

	[Test]
	[NotInParallel]
	public async ValueTask HandlerCountsOccupantsMatchingIndependentLcon()
	{
		var probe = await ProbeRoom();
		try
		{
			// Install a handler that records the occupant COUNT (via words()) for easier assertion.
			await Cmd("&ROOM`CONTENTS #9=&FANOUT_COUNT`[after(first(%0,:),#)] #9=[words(lcon(%0))]");

			await Trigger(probe.Room, "move-in");

			var recorded = await Eval($"get(#9/FANOUT_COUNT`{probe.Room[1..]})");
			var expected = await Eval($"words(lcon({probe.Room}))");

			// Recorded count must equal the independently computed lcon count.
			// Fails if the handler did not run, or ran against the wrong room.
			await Assert.That(recorded).IsEqualTo(expected);
		}
		finally
		{
			await RestorePackage();
			await RemoveProbeRoom(probe);
		}
	}

	[Test]
	[NotInParallel]
	public async ValueTask HandlerCauseArgIsPassedAsPercent1()
	{
		var probe = await ProbeRoom();
		try
		{
			// Install a handler that captures %1 (the cause) into LAST_CAUSE.
			await Cmd("&ROOM`CONTENTS #9=&LAST_CAUSE`[after(first(%0,:),#)] #9=%1");

			var cause = "move-in";
			await Trigger(probe.Room, cause);

			var recorded = await Eval($"get(#9/LAST_CAUSE`{probe.Room[1..]})");

			// %1 must carry the cause string "move-in".
			await Assert.That(recorded).IsEqualTo(cause);
		}
		finally
		{
			await RestorePackage();
			await RemoveProbeRoom(probe);
		}
	}

	[Test]
	[NotInParallel]
	public async ValueTask V1RowShape_StillBuildsValidJson()
	{
		var probe = await ProbeRoom();
		var thingName = await Eval($"name({probe.Thing})");
		try
		{
			// The previous tests prove the handler fires with the right room/cause, but not that
			// the v1 idioms (json_array + iter + filter + helper rows) actually produce VALID JSON for
			// the room's occupants. This exercises exactly that, with the 1.0 row shape.

			// Install the v1 helpers under scratch names, plus a handler that records the
			// room.contents payload (json_array of per-occupant rows) into LAST_PAYLOAD — oob() needs
			// a live WebSocket the harness lacks, so we capture the built payload instead of sending it.
			await Cmd("&FN`NOTEXIT #9=not(hastype(%0,exit))");
			await Cmd("&FN`V1ROW #9=json(object,dbref,json(string,[num(%0)]),name,json(string,name(%0)),cmd,json(string,look [num(%0)]))");
			await Cmd("&ROOM`CONTENTS #9=&LAST_PAYLOAD`[after(first(%0,:),#)] #9=json(object,who,json_array(iter(filter(#9/FN`NOTEXIT,lcon(%0)),u(#9/FN`V1ROW,itext(0)),%b,|),|))");

			await Trigger(probe.Room, "move-in");

			// The core assertion the earlier tests missed: the handler emits VALID JSON.
			var payload = await Eval($"get(#9/LAST_PAYLOAD`{probe.Room[1..]})");
			await Assert.That(await Eval($"isjson(get(#9/LAST_PAYLOAD`{probe.Room[1..]}))")).IsEqualTo("1");

			// And the who list is built from the real occupants: it carries the room's one thing.
			await Assert.That(payload).Contains(thingName);
			await Assert.That(payload).Contains($"\"dbref\":\"{probe.Thing}\"");
		}
		finally
		{
			await RestorePackage();
			await RemoveProbeRoom(probe);
		}
	}

	[Test]
	[NotInParallel]
	public async ValueTask HandlerDoesNotRunAfterAttributeIsCleared()
	{
		var probe = await ProbeRoom();
		try
		{
			// Install, then immediately clear. Trigger must not set FANOUT_SENTINEL.
			await Cmd("&ROOM`CONTENTS #9=&FANOUT_SENTINEL`[after(first(%0,:),#)] #9=ran");
			await Cmd("&ROOM`CONTENTS #9=");

			await Trigger(probe.Room, "move-in");

			// Sentinel must be empty because the handler was cleared before the trigger.
			await Assert.That(await Eval($"get(#9/FANOUT_SENTINEL`{probe.Room[1..]})")).IsEqualTo(string.Empty);
		}
		finally
		{
			await RestorePackage();
			await RemoveProbeRoom(probe);
		}
	}

	// ── OOB v2 (room-contents 2.0) ─────────────────────────────────────────────────────────

	/// <summary>
	/// A room built for the v2 tests. Connected: a wizard who is DARK (hidden from mortals) with a
	/// valid PROFILE`COLOR, and a mortal with a portrait, a bad IMAGE`FOCAL and a hostile
	/// PROFILE`COLOR. Asleep: one player. Things: a bundle with a picture, a DARK crate, and a thing
	/// whose name carries a comma, quotes, parentheses and a semicolon. Exits to a second room: plain
	/// (with a CONFIRM), locked to the wizard, DARK, and unlinked. The room's description has a
	/// literal newline and a %N. Every name carries the token so nothing here can be confused with
	/// what another test built.
	/// </summary>
	private sealed record Fixture(
		string Room, string Dest, string Wizard, string Mortal, string Asleep,
		string Bundle, string Crate, string Hostile, string North, string Locked, string Dark, string Unlinked,
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
		// Typed at a client, & stores verbatim: the %r and %N are code the handler evaluates.
		await Cmd($"&DESCRIBE {room}=Tarred pilings.%rStacked crates, %N looks on.");
		await Cmd($"&DESCRIBE {dest}=A lamplit street.");

		var wizard = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, "RcWiz", roomRef);
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, "RcMortal", roomRef);
		var asleep = await TestIsolationHelpers.CreateTestPlayerAsync(WebAppFactoryArg.Services, Mediator, "RcAsleep");
		await Cmd($"@set #{wizard.DbRef.Number}=WIZARD");
		await Cmd($"@set #{wizard.DbRef.Number}=DARK");
		await Cmd($"&PROFILE`COLOR #{wizard.DbRef.Number}=#ffb454");
		await Cmd($"&PROFILE`COLOR #{mortal.DbRef.Number}=#fff;background:url(x)");
		await Cmd($"&IMAGE #{mortal.DbRef.Number}=/assets/chars/{token}.jpg");
		await Cmd($"&IMAGE`FOCAL #{mortal.DbRef.Number}=center");

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
		await Cmd($"@set {crate}=DARK");
		// A name with everything a legal name may carry that JSON, the argument splitter or a
		// command list would trip on: a comma, double quotes, parentheses, a semicolon, a dollar and
		// angle brackets. (Backslash, pipe, brackets, percent and control characters are not legal
		// in a name here — ValidateService.NameRegex — so they cannot reach a row.) Renamed after
		// creation because a comma cannot pass through create()'s argument list.
		var hostile = await Build($"create(Bad{token})");
		await Cmd($"@name {hostile}=Bad{token}, \"quoted\" (thing); $5 <tag>");
		await Assert.That(await Eval($"name({hostile})")).Contains("\"quoted\" (thing);").Because("the hostile name must have been applied");
		foreach (var thing in new[] { bundle, crate, hostile })
		{
			await Eval($"tel({thing},{room})");
		}

		foreach (var player in new[] { wizard.DbRef, mortal.DbRef, asleep })
		{
			await Eval($"tel(#{player.Number},{room})");
		}

		return new Fixture(room, dest, $"#{wizard.DbRef.Number}", $"#{mortal.DbRef.Number}", $"#{asleep.Number}",
			bundle, crate, hostile, north, locked, dark, unlinked, wizard.Handle, mortal.Handle);
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
		foreach (var obj in new[] { f.North, f.Locked, f.Dark, f.Unlinked, f.Bundle, f.Crate, f.Hostile, f.Dest, f.Room })
		{
			await Cmd($"@dest/override {obj}");
		}
	}

	/// <summary>
	/// Runs the package's payload helper for <paramref name="viewer"/> on <paramref name="handler"/>
	/// and parses what it built: FN`PREPARE first, then <c>u(me/&lt;helper&gt;,%0,&lt;viewer&gt;)</c>,
	/// which is exactly what the shipped ROOM`CONTENTS hands to oob().
	/// </summary>
	private async Task<JsonDocument> Payload(string handler, string helper, string room, string viewer)
	{
		await Cmd($"&FN`T`PAYLOAD {handler}=[null(u(me/FN`PREPARE,%0))][u(me/{helper},%0,%1)]");
		var payload = await Eval($"u({handler}/FN`T`PAYLOAD,{room},{viewer})");
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
		string? handler = null;
		try
		{
			handler = await BuildHandler(token);
			f = await BuildFixture(token);

			using var forMortal = await Payload(handler, "FN`PAYLOAD`CONTENTS", f.Room, f.Mortal);
			using var forWizard = await Payload(handler, "FN`PAYLOAD`CONTENTS", f.Room, f.Wizard);

			foreach (var doc in new[] { forMortal, forWizard })
			{
				await Assert.That(doc.RootElement.GetProperty("v").GetInt32()).IsEqualTo(2);
			}

			var mortalSees = RowsByDbref(forMortal, "who");
			var wizardSees = RowsByDbref(forWizard, "who");

			// Connected players and things are listed; the asleep player is not (FN`WHOVIS: players
			// only while CONNECTED — 1.0's isplayer() did not exist and let every disconnected player
			// through). lcon() includes exits on this engine; none may leak into who.
			foreach (var rows in new[] { mortalSees, wizardSees })
			{
				await Assert.That(rows.Keys).Contains(f.Mortal);
				await Assert.That(rows.Keys).Contains(f.Bundle);
				await Assert.That(rows.Keys).Contains(f.Hostile);
				await Assert.That(rows.Keys).DoesNotContain(f.Asleep);
				await Assert.That(rows.Keys).DoesNotContain(f.North);
			}

			// DARK occupants: lcon() runs as #9 and sees them, so FN`WHOVIS must drop them for a viewer
			// who may not — the DARK wizard and the DARK crate are absent for the mortal, present for
			// the wizard.
			await Assert.That(mortalSees.Keys).DoesNotContain(f.Wizard);
			await Assert.That(mortalSees.Keys).DoesNotContain(f.Crate);
			await Assert.That(wizardSees.Keys).Contains(f.Wizard);
			await Assert.That(wizardSees.Keys).Contains(f.Crate);

			// "you" only on the viewer's own row, and only for that viewer.
			await Assert.That(mortalSees[f.Mortal].GetProperty("you").GetBoolean()).IsTrue();
			await Assert.That(wizardSees[f.Wizard].GetProperty("you").GetBoolean()).IsTrue();
			await Assert.That(wizardSees[f.Mortal].TryGetProperty("you", out _)).IsFalse();

			// v1 keys survive unchanged; v2 identity and type are added.
			var mortalRow = wizardSees[f.Mortal];
			await Assert.That(mortalRow.GetProperty("cmd").GetString()).IsEqualTo($"look {f.Mortal}");
			await Assert.That(mortalRow.GetProperty("objid").GetString()).StartsWith($"{f.Mortal}:");
			await Assert.That(mortalRow.GetProperty("type").GetString()).IsEqualTo("player");
			await Assert.That(wizardSees[f.Bundle].GetProperty("type").GetString()).IsEqualTo("thing");

			// Player extras: status/idle, profile, and a Page action for everyone but yourself.
			await Assert.That(mortalRow.GetProperty("status").GetString()).IsEqualTo("active");
			await Assert.That(mortalRow.GetProperty("idle").ValueKind).IsEqualTo(JsonValueKind.Number);
			await Assert.That(mortalRow.GetProperty("profile").GetBoolean()).IsTrue();
			await Assert.That(mortalRow.GetProperty("actions")[0].GetProperty("cmd").GetString()).IsEqualTo($"page {f.Mortal}=");
			await Assert.That(wizardSees[f.Wizard].TryGetProperty("actions", out _)).IsFalse();

			// color lands in a CSS custom property: only #rrggbb passes, a hostile value is dropped.
			await Assert.That(wizardSees[f.Wizard].GetProperty("color").GetString()).IsEqualTo("#ffb454");
			await Assert.That(mortalRow.TryGetProperty("color", out _)).IsFalse();

			// Images: {url, alt} from IMAGE / IMAGE`ALT (alt falls back to the name); no image key at all
			// on a row without IMAGE; a malformed IMAGE`FOCAL is dropped, not an error that would have
			// made the whole payload invalid.
			var bundle = wizardSees[f.Bundle];
			await Assert.That(bundle.GetProperty("image").GetProperty("url").GetString()).IsEqualTo($"/assets/obj/{token}.jpg");
			await Assert.That(bundle.GetProperty("image").GetProperty("alt").GetString()).IsEqualTo($"Oilcloth bundle {token}");
			await Assert.That(bundle.GetProperty("image").TryGetProperty("focal", out _)).IsFalse();
			await Assert.That(mortalRow.GetProperty("image").GetProperty("url").GetString()).IsEqualTo($"/assets/chars/{token}.jpg");
			await Assert.That(mortalRow.GetProperty("image").TryGetProperty("focal", out _)).IsFalse();

			// A hostile name (comma, quotes, parentheses, semicolon) comes through exactly as name() has it.
			await Assert.That(wizardSees[f.Hostile].GetProperty("name").GetString()).IsEqualTo(await Eval($"name({f.Hostile})"));

			// Things carry none of the player-only keys.
			var crate = wizardSees[f.Crate];
			foreach (var absent in new[] { "image", "status", "idle", "profile", "actions", "color", "you" })
			{
				await Assert.That(crate.TryGetProperty(absent, out _)).IsFalse().Because($"a thing row must not carry {absent}");
			}
		}
		finally
		{
			if (handler is not null) await Cmd($"@dest/override {handler}");
			if (f is not null) await TearDownFixture(f);
		}
	}

	[Test]
	public async ValueTask V2Exits_LockedDarkAndUnlinked_AreReportedPerViewer()
	{
		var token = Guid.NewGuid().ToString("N")[..8];
		Fixture? f = null;
		string? handler = null;
		try
		{
			handler = await BuildHandler(token);
			f = await BuildFixture(token);

			using var forMortal = await Payload(handler, "FN`PAYLOAD`EXITS", f.Room, f.Mortal);
			using var forWizard = await Payload(handler, "FN`PAYLOAD`EXITS", f.Room, f.Wizard);

			await Assert.That(forMortal.RootElement.GetProperty("v").GetInt32()).IsEqualTo(2);
			var mortalSees = RowsByDbref(forMortal, "exits");
			var wizardSees = RowsByDbref(forWizard, "exits");

			// The DARK exit is omitted for the mortal and present for the wizard.
			await Assert.That(mortalSees.Keys).DoesNotContain(f.Dark);
			await Assert.That(wizardSees.Keys).Contains(f.Dark);

			// The locked exit: locked, with its @fail as the hint and NO destination preview, for the
			// mortal; open with the preview for the wizard, who holds the key. cmd stays on both.
			await Assert.That(mortalSees[f.Locked].GetProperty("state").GetString()).IsEqualTo("locked");
			await Assert.That(mortalSees[f.Locked].GetProperty("hint").GetString()).IsEqualTo("Closed after dusk.");
			await Assert.That(mortalSees[f.Locked].TryGetProperty("dest", out _)).IsFalse();
			await Assert.That(mortalSees[f.Locked].GetProperty("cmd").GetString()).IsEqualTo($"goto {f.Locked}");
			await Assert.That(wizardSees[f.Locked].GetProperty("state").GetString()).IsEqualTo("open");
			await Assert.That(wizardSees[f.Locked].TryGetProperty("hint", out _)).IsFalse();
			await Assert.That(wizardSees[f.Locked].GetProperty("dest").GetProperty("name").GetString()).IsEqualTo($"RcDest{token}");

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
			if (handler is not null) await Cmd($"@dest/override {handler}");
			if (f is not null) await TearDownFixture(f);
		}
	}

	[Test]
	public async ValueTask V2Info_CarriesIdentityImageAndDescription()
	{
		var token = Guid.NewGuid().ToString("N")[..8];
		Fixture? f = null;
		string? handler = null;
		try
		{
			handler = await BuildHandler(token);
			f = await BuildFixture(token);

			using var info = await Payload(handler, "FN`PAYLOAD`INFO", f.Room, f.Mortal);
			var root = info.RootElement;

			await Assert.That(root.GetProperty("v").GetInt32()).IsEqualTo(2);
			await Assert.That(root.GetProperty("dbref").GetString()).IsEqualTo(f.Room);
			await Assert.That(root.GetProperty("objid").GetString()).StartsWith($"{f.Room}:");
			await Assert.That(root.GetProperty("name").GetString()).IsEqualTo($"RcRoom{token}");
			await Assert.That(root.GetProperty("desc").GetProperty("format").GetString()).IsEqualTo("text");
			// The description is evaluated: the %r is a real newline (escaped by json(), so the payload
			// stayed valid) and %N is the event's enactor — God here, who fired the event.
			await Assert.That(root.GetProperty("desc").GetProperty("text").GetString())
				.IsEqualTo($"Tarred pilings.\nStacked crates, {await Eval("name(#1)")} looks on.");

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
			using var withArea = await Payload(handler, "FN`PAYLOAD`INFO", f.Room, f.Mortal);
			await Assert.That(withArea.RootElement.GetProperty("area").GetString()).IsEqualTo($"RcDest{token}");

			// A room with no picture carries no image key.
			using var plain = await Payload(handler, "FN`PAYLOAD`INFO", f.Dest, f.Mortal);
			await Assert.That(plain.RootElement.TryGetProperty("image", out _)).IsFalse();
		}
		finally
		{
			if (handler is not null) await Cmd($"@dest/override {handler}");
			if (f is not null) await TearDownFixture(f);
		}
	}

	/// <summary>
	/// room.info is the Play page's banner, so it takes the wide art: IMAGE`BANNER, falling back to
	/// IMAGE (help IMAGE). alt and focal still come from IMAGE`ALT / IMAGE`FOCAL. The occupant and
	/// destination rows keep IMAGE — a thumbnail is not a banner.
	/// </summary>
	[Test]
	public async ValueTask V2Info_UsesTheBanner_FallingBackToImage()
	{
		var token = Guid.NewGuid().ToString("N")[..8];
		Fixture? f = null;
		string? handler = null;
		try
		{
			handler = await BuildHandler(token);
			f = await BuildFixture(token);

			await Cmd($"&IMAGE`BANNER {f.Room}=/assets/rooms/{token}-wide.jpg");
			using var both = await Payload(handler, "FN`PAYLOAD`INFO", f.Room, f.Mortal);
			var image = both.RootElement.GetProperty("image");
			await Assert.That(image.GetProperty("url").GetString()).IsEqualTo($"/assets/rooms/{token}-wide.jpg");
			await Assert.That(image.GetProperty("alt").GetString()).IsEqualTo("The quay at dusk");

			// A room with a banner and no IMAGE still gets a banner.
			await Cmd($"&IMAGE`BANNER {f.Dest}=/assets/rooms/{token}-dest-wide.jpg");
			using var bannerOnly = await Payload(handler, "FN`PAYLOAD`INFO", f.Dest, f.Mortal);
			await Assert.That(bannerOnly.RootElement.GetProperty("image").GetProperty("url").GetString())
				.IsEqualTo($"/assets/rooms/{token}-dest-wide.jpg");

			// The exit's destination preview is a thumbnail: IMAGE only, so the banner-only room has none.
			using var exits = await Payload(handler, "FN`PAYLOAD`EXITS", f.Room, f.Wizard);
			var north = RowsByDbref(exits, "exits")[f.North];
			await Assert.That(north.GetProperty("dest").TryGetProperty("image", out _)).IsFalse();
		}
		finally
		{
			if (handler is not null) await Cmd($"@dest/override {handler}");
			if (f is not null) await TearDownFixture(f);
		}
	}

	/// <summary>
	/// The handler is a wizard, so get() would read an image attribute its owner has made private.
	/// Only a visual one is published: the seeded flags are defaults for a new attribute, and an IMAGE
	/// set before they existed, or with visual cleared, keeps its owner's choice.
	/// </summary>
	[Test]
	public async ValueTask V2_PrivateImageAttributes_AreNotPublished()
	{
		var token = Guid.NewGuid().ToString("N")[..8];
		Fixture? f = null;
		string? handler = null;
		try
		{
			handler = await BuildHandler(token);
			f = await BuildFixture(token);

			// visual does not propagate down an attribute tree, and a private branch hides its leaves
			// (help ATTRIBUTE FLAGS2): the room's visual IMAGE`BANNER, under a private IMAGE, stays hidden.
			await Cmd($"&IMAGE`BANNER {f.Room}=/assets/rooms/{token}-leaf.jpg");
			await Cmd($"@set {f.Mortal}/IMAGE=!visual");
			await Cmd($"@set {f.Room}/IMAGE=!visual");
			await Cmd($"&IMAGE`BANNER {f.Dest}=/assets/rooms/{token}-private.jpg");
			await Cmd($"@set {f.Dest}/IMAGE`BANNER=!visual");

			using var contents = await Payload(handler, "FN`PAYLOAD`CONTENTS", f.Room, f.Wizard);
			var rows = RowsByDbref(contents, "who");
			await Assert.That(rows[f.Mortal].TryGetProperty("image", out _)).IsFalse()
				.Because("a private IMAGE must not reach anyone's room.contents");
			await Assert.That(rows[f.Bundle].GetProperty("image").GetProperty("url").GetString())
				.IsEqualTo($"/assets/obj/{token}.jpg");

			using var info = await Payload(handler, "FN`PAYLOAD`INFO", f.Room, f.Mortal);
			await Assert.That(info.RootElement.TryGetProperty("image", out _)).IsFalse()
				.Because("a visual IMAGE`BANNER under a private IMAGE must not be published");

			using var dest = await Payload(handler, "FN`PAYLOAD`INFO", f.Dest, f.Mortal);
			await Assert.That(dest.RootElement.TryGetProperty("image", out _)).IsFalse()
				.Because("a private IMAGE`BANNER must not be published either");
		}
		finally
		{
			if (handler is not null) await Cmd($"@dest/override {handler}");
			if (f is not null) await TearDownFixture(f);
		}
	}

	/// <summary>
	/// The scene block against the Scene plugin's real answers, stood in for by @function globals
	/// (the plugin is not loaded in this harness): scenewhere() answers the constant
	/// <c>#-1 NOT FOUND</c> for a room with no scene, and scene(#-1 NOT FOUND,id) answers the same
	/// constant — an id-echo guard alone passes that, and a Wizard viewer then got a phantom scene
	/// in every scene-less room. With a real scene, public, every viewer gets id, title and cast.
	/// </summary>
	[Test]
	// Defines session-wide @function globals.
	[NotInParallel]
	public async ValueTask V2Info_SceneBlock_RejectsNotFound_AndCarriesARealScene()
	{
		var token = Guid.NewGuid().ToString("N")[..8];
		Fixture? f = null;
		string? handler = null;
		try
		{
			handler = await BuildHandler(token);
			f = await BuildFixture(token);

			await Cmd("&FN`T`NOTFOUND #9=#-1 NOT FOUND");
			await Cmd("@function scenewhere=#9,FN`T`NOTFOUND");
			await Cmd("@function scene=#9,FN`T`NOTFOUND");

			using var none = await Payload(handler, "FN`PAYLOAD`INFO", f.Room, f.Wizard);
			await Assert.That(none.RootElement.TryGetProperty("scene", out _)).IsFalse()
				.Because("a Wizard viewer of a scene-less room must not get a phantom #-1 NOT FOUND scene");

			await Cmd("&FN`T`SCENEWHERE #9=42");
			await Cmd("&FN`T`SCENE #9=switch(%1,id,42,public,1,title,Salt Market at Dusk)");
			await Cmd("&FN`T`SCENEMEMBERS #9=#1 #2 #3");
			// The mortal is a participant focused on the scene; the wizard is neither, and is focused on
			// another scene.
			await Cmd($"&FN`T`SCENEMEMBER #9=if(strmatch(num(%1),num({f.Mortal})),participant,#-1 NOT FOUND)");
			await Cmd($"&FN`T`SCENEFOCUS #9=if(strmatch(num(%0),num({f.Mortal})),42,43)");
			await Cmd("@function scenewhere=#9,FN`T`SCENEWHERE");
			await Cmd("@function scene=#9,FN`T`SCENE");
			await Cmd("@function scenemembers=#9,FN`T`SCENEMEMBERS");
			await Cmd("@function scenemember=#9,FN`T`SCENEMEMBER");
			await Cmd("@function scenefocus=#9,FN`T`SCENEFOCUS");

			foreach (var viewer in new[] { f.Wizard, f.Mortal })
			{
				using var doc = await Payload(handler, "FN`PAYLOAD`INFO", f.Room, viewer);
				var scene = doc.RootElement.GetProperty("scene");
				await Assert.That(scene.GetProperty("id").GetString()).IsEqualTo("42");
				await Assert.That(scene.GetProperty("title").GetString()).IsEqualTo("Salt Market at Dusk");
				await Assert.That(scene.GetProperty("cast").GetInt32()).IsEqualTo(3);
			}

			// The viewer's own place in the scene: what the Play page needs to know whether a pose made
			// in the room is recorded there.
			using (var member = await Payload(handler, "FN`PAYLOAD`INFO", f.Room, f.Mortal))
			{
				var scene = member.RootElement.GetProperty("scene");
				await Assert.That(scene.GetProperty("role").GetString()).IsEqualTo("participant");
				await Assert.That(scene.GetProperty("focus").GetBoolean()).IsTrue();
				await Assert.That(scene.GetProperty("elsewhere").GetBoolean()).IsFalse();
			}

			using (var watcher = await Payload(handler, "FN`PAYLOAD`INFO", f.Room, f.Wizard))
			{
				var scene = watcher.RootElement.GetProperty("scene");
				await Assert.That(scene.GetProperty("role").ValueKind).IsEqualTo(JsonValueKind.Null);
				await Assert.That(scene.GetProperty("focus").GetBoolean()).IsFalse();
				await Assert.That(scene.GetProperty("elsewhere").GetBoolean()).IsTrue();
			}

			// A focus on a scene the causer may not see is still a focus elsewhere; only NOT FOUND is none.
			await Cmd($"&FN`T`SCENEFOCUS #9=if(strmatch(num(%0),num({f.Mortal})),42,#-1 PERMISSION)");
			using (var hidden = await Payload(handler, "FN`PAYLOAD`INFO", f.Room, f.Wizard))
			{
				await Assert.That(hidden.RootElement.GetProperty("scene").GetProperty("elsewhere").GetBoolean()).IsTrue();
			}

			await Cmd($"&FN`T`SCENEFOCUS #9=if(strmatch(num(%0),num({f.Mortal})),42,#-1 NOT FOUND)");
			using (var unfocused = await Payload(handler, "FN`PAYLOAD`INFO", f.Room, f.Wizard))
			{
				var scene = unfocused.RootElement.GetProperty("scene");
				await Assert.That(scene.GetProperty("focus").GetBoolean()).IsFalse();
				await Assert.That(scene.GetProperty("elsewhere").GetBoolean()).IsFalse();
			}
		}
		finally
		{
			foreach (var fn in new[] { "scenewhere", "scene", "scenemembers", "scenemember", "scenefocus" })
			{
				await Cmd($"@function/delete {fn}");
			}

			foreach (var attr in new[] { "FN`T`NOTFOUND", "FN`T`SCENEWHERE", "FN`T`SCENE", "FN`T`SCENEMEMBERS", "FN`T`SCENEMEMBER", "FN`T`SCENEFOCUS" })
			{
				await Cmd($"&{attr} #9=");
			}

			if (handler is not null) await Cmd($"@dest/override {handler}");
			if (f is not null) await TearDownFixture(f);
		}
	}

	/// <summary>
	/// The viewer-independent work — occupant base rows, exit base rows and destination previews,
	/// the room's own info — is computed once per event by FN`PREPARE into q-registers the
	/// per-viewer helpers read, so a room with N viewers evaluates each description and image once,
	/// not N times. u() shares the caller's registers on this engine, which is what makes that work.
	/// </summary>
	[Test]
	public async ValueTask Prepare_HoistsViewerIndependentRowsOncePerEvent()
	{
		var token = Guid.NewGuid().ToString("N")[..8];
		Fixture? f = null;
		string? handler = null;
		try
		{
			handler = await BuildHandler(token);
			f = await BuildFixture(token);

			await Cmd($"&FN`T`PREPARED {handler}=[null(u(me/FN`PREPARE,%0))][r(w{f.Mortal[1..]})]%r[r(x{f.North[1..]})]%r[r(d{f.North[1..]})]%r[r(info{f.Room[1..]})]");
			var recorded = await Eval($"u({handler}/FN`T`PREPARED,{f.Room})");
			var parts = recorded.Split('\n');
			await Assert.That(parts.Length).IsEqualTo(4).Because($"recorded: {recorded}");
			await Assert.That(parts[0]).IsNotEmpty().Because($"the mortal's base row must be in w{f.Mortal[1..]}; recorded: {recorded}");

			using var who = JsonDocument.Parse(parts[0]);
			await Assert.That(who.RootElement.GetProperty("dbref").GetString()).IsEqualTo(f.Mortal);
			await Assert.That(who.RootElement.TryGetProperty("you", out _)).IsFalse().Because("the base row carries nothing per viewer");

			using var exit = JsonDocument.Parse(parts[1]);
			await Assert.That(exit.RootElement.GetProperty("dbref").GetString()).IsEqualTo(f.North);
			await Assert.That(exit.RootElement.TryGetProperty("state", out _)).IsFalse().Because("state is per viewer");

			using var dest = JsonDocument.Parse(parts[2]);
			await Assert.That(dest.RootElement.GetProperty("name").GetString()).IsEqualTo($"RcDest{token}");

			using var info = JsonDocument.Parse(parts[3]);
			await Assert.That(info.RootElement.GetProperty("dbref").GetString()).IsEqualTo(f.Room);
			await Assert.That(info.RootElement.TryGetProperty("scene", out _)).IsFalse().Because("scene is per viewer");
		}
		finally
		{
			if (handler is not null) await Cmd($"@dest/override {handler}");
			if (f is not null) await TearDownFixture(f);
		}
	}

	/// <summary>
	/// The shipped handler itself, unmodified, run through the real event path against the fixture
	/// room for each cause. oob() delivers to nobody here (the harness has no WebSocket), so the
	/// assertion is that the handler runs to completion without an error reaching the handler
	/// object — the whole per-viewer iteration, every helper, every json_mod() patch — and that
	/// room.info goes to the causer alone when the causer is in the room, to everyone otherwise.
	/// </summary>
	[Test]
	[NotInParallel]
	public async ValueTask ShippedHandler_RunsEndToEnd_ForEveryCause_AndTargetsRoomInfo()
	{
		var token = Guid.NewGuid().ToString("N")[..8];
		Fixture? f = null;
		try
		{
			await InstallPackage();
			f = await BuildFixture(token);

			// null() swallows the handler's own output; anything else reaching #9 is an evaluation
			// error ("#-1 ...") or a locate failure ("I can't see that here."). Capture both by
			// recording what the handler evaluates to — the same body with strcat() for null(), so
			// every argument still runs and their output is stored — with the send left in place.
			var handler = PackageAttributes.Value["ROOM`CONTENTS"];
			await Assert.That(handler).StartsWith("think null(");
			var body = handler["think null(".Length..];
			// Recorded under the fixture room's number: another test's ROOM`CONTENTS cannot overwrite it.
			var payload = $"LAST_PAYLOAD`{f.Room[1..]}";
			await Cmd($"&ROOM`CONTENTS #9=&LAST_PAYLOAD`[after(first(%0,:),#)] #9=strcat({body}");

			// The causer (God, the test's enactor) is not in the room: two viewers, each sent
			// room.contents and room.exits (0 deliveries each, no WebSocket), plus room.info to both on
			// the arrival causes. Per viewer the counts concatenate to a run of zeros, and iter()
			// joins the viewers with its default space.
			foreach (var cause in new[] { "move-in", "move-out", "connect", "disconnect" })
			{
				await Cmd($"&{payload} #9=unset");
				await Trigger(f.Room, cause);
				var result = await Eval($"get(#9/{payload})");
				await Assert.That(result).IsEqualTo(cause is "move-in" or "connect" ? "000 000" : "00 00")
					.Because($"{cause}: the handler must run every oob() cleanly, got '{result}'");
			}

			// The causer is in the room (the mortal walked in): room.info goes to the mortal alone, so
			// one viewer's run has three zeros and the other's two.
			await Cmd($"&{payload} #9=unset");
			await Raise(new DBRef(int.Parse(f.Mortal[1..]), null), f.Room, "move-in");
			var targeted = (await Eval($"get(#9/{payload})")).Split(' ').Order().ToArray();
			await Assert.That(targeted).IsEquivalentTo(["00", "000"]);

			// The causer is in the room but is not a viewer — a thing that moved itself in. Nobody
			// would match it, so room.info goes to everyone rather than to no one.
			await Cmd($"&{payload} #9=unset");
			await Raise(new DBRef(int.Parse(f.Bundle[1..]), null), f.Room, "move-in");
			await Assert.That(await Eval($"get(#9/{payload})")).IsEqualTo("000 000")
				.Because("a causer who is not a connected viewer must not swallow room.info for everyone");

			// A resume re-sends one session's state, as connect does, and nothing changed for anyone
			// else: the resuming player alone is sent room.contents, room.exits and room.info.
			await Cmd($"&{payload} #9=unset");
			await Raise(new DBRef(int.Parse(f.Mortal[1..]), null), f.Room, "resume");
			await Assert.That(await Eval($"get(#9/{payload})")).IsEqualTo("000")
				.Because("a resume is sent to the resuming player alone, all three packages");

			// A scene change (the Scene plugin's cause) re-sends room.info alone, to every viewer, the
			// causer included when they are in the room: only the scene block changed, and it is per viewer.
			await Cmd($"&{payload} #9=unset");
			await Raise(new DBRef(int.Parse(f.Mortal[1..]), null), f.Room, "scene");
			await Assert.That(await Eval($"get(#9/{payload})")).IsEqualTo("0 0")
				.Because("a scene change sends room.info, and nothing else, to every viewer in the room");

			// A resuming player who is not a viewer in the room is sent nothing, and nobody else is either.
			await Cmd($"&{payload} #9=unset");
			await Trigger(f.Room, "resume");
			await Assert.That(await Eval($"get(#9/{payload})")).IsEqualTo(string.Empty);

			// A room nobody connected is in: the handler finds no viewer and builds nothing. The record also
			// carries the info<n> register FN`PREPARE fills, so preparing the room's rows anyway shows.
			await Cmd($"&ROOM`CONTENTS #9=&LAST_PAYLOAD`[after(first(%0,:),#)] #9=strcat({body[..^1]},[r(info[rest(num(%0),#)])])");
			var emptyRoom = $"LAST_PAYLOAD`{f.Dest[1..]}";
			await Cmd($"&{emptyRoom} #9=unset");
			await Trigger(f.Dest, "move-in");
			await Assert.That(await Eval($"get(#9/{emptyRoom})")).IsEqualTo(string.Empty)
				.Because("a room with no connected viewer is neither prepared nor sent anything");
		}
		finally
		{
			await RestorePackage();
			if (f is not null) await TearDownFixture(f);
		}
	}
}
