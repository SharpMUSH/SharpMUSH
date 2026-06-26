using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// Wiring tests for the WebSocket Support Package reference ROOM`CONTENTS handler.
///
/// These tests install a simplified version of the reference handler on #9, fire
/// ROOM`CONTENTS via EventService.TriggerEventAsync (the same path movement uses),
/// then assert the handler executed with the correct room dbref in %0.
///
/// They prove:
///   1. The ROOM`CONTENTS attribute on #9 is executed when the event fires.
///   2. %0 correctly carries the room dbref into the handler body.
///   3. lcon(%0) returns the room's occupants from within the handler context.
///
/// Most tests record lcon(%0)/words(lcon(%0)) into scratch attributes on #9 instead of calling
/// oob() — oob() requires WebSocket connections the unit harness lacks — so they assert fan-out
/// targeting logic. HandlerBuildsValidRoomContentsJsonPayload goes further: it runs the real
/// reference idioms (json_array + iter + filter + helper rows) and asserts the built
/// room.contents payload is VALID JSON containing the room's actual occupants.
///
/// Each test cleans up its scratch attributes in a finally block (via <see cref="ClearScratch"/>)
/// so a failed assertion cannot leak handler state into the next test.
/// </summary>
[NotInParallel]
public class RoomContentsHandlerReferenceTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IEventService EventService => WebAppFactoryArg.Services.GetRequiredService<IEventService>();

	private Task Cmd(string command) =>
		WebAppFactoryArg.CommandParser.CommandParse(1, ConnectionService, MModule.single(command)).AsTask();

	private async Task<string> Eval(string expression) =>
		(await WebAppFactoryArg.FunctionParser.FunctionParse(MModule.single(expression)))!.Message!.ToPlainText();

	/// <summary>
	/// Clears every scratch attribute these tests install on #9. Runs in each test's finally so a
	/// failed assertion never leaks handler state into a later test (they run sequentially).
	/// </summary>
	private async Task ClearScratch()
	{
		foreach (var attr in new[]
		{
			"ROOM`CONTENTS", "FANOUT_LIST", "FANOUT_COUNT", "LAST_CAUSE",
			"FANOUT_SENTINEL", "LAST_PAYLOAD", "FN`WHOROW", "FN`NOTEXIT",
		})
		{
			await Cmd($"&{attr} #9=");
		}
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
			// TriggerEventAsync runs with God as executor, same elevated-permission context.
			await EventService.TriggerEventAsync(
				WebAppFactoryArg.CommandParser,
				SharpEvents.RoomContents,
				WebAppFactoryArg.ExecutorDBRef,
				room,
				"move-in");

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
			await ClearScratch();
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

			await EventService.TriggerEventAsync(
				WebAppFactoryArg.CommandParser,
				SharpEvents.RoomContents,
				WebAppFactoryArg.ExecutorDBRef,
				room,
				"move-in");

			var recorded = await Eval("get(#9/FANOUT_COUNT)");
			var expected = await Eval($"words(lcon({room}))");

			// Recorded count must equal the independently computed lcon count.
			// Fails if the handler did not run, or ran against the wrong room.
			await Assert.That(recorded).IsEqualTo(expected);
		}
		finally
		{
			await ClearScratch();
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

			await EventService.TriggerEventAsync(
				WebAppFactoryArg.CommandParser,
				SharpEvents.RoomContents,
				WebAppFactoryArg.ExecutorDBRef,
				room,
				cause);

			var recorded = await Eval("get(#9/LAST_CAUSE)");

			// %1 must carry the cause string "move-in".
			await Assert.That(recorded).IsEqualTo(cause);
		}
		finally
		{
			await ClearScratch();
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
			// the reference handler's payload-building (json_array + iter + filter + helper rows)
			// actually produces VALID JSON for the room's occupants. This exercises exactly that.

			// A uniquely-named thing dropped into God's room becomes a who-list occupant.
			await Cmd($"@create {thingName}");
			await Cmd($"drop {thingName}");

			// Install the real reference helpers, plus a handler that records the room.contents
			// payload (json_array of per-occupant rows) into LAST_PAYLOAD — oob() needs a live
			// WebSocket the harness lacks, so we capture the built payload instead of sending it.
			await Cmd("&FN`NOTEXIT #9=not(hastype(%0,exit))");
			await Cmd("&FN`WHOROW #9=json(object,dbref,json(string,[num(%0)]),name,json(string,name(%0)),cmd,json(string,look [num(%0)]))");
			await Cmd("&ROOM`CONTENTS #9=&LAST_PAYLOAD #9=json(object,who,json_array(iter(filter(#9/FN`NOTEXIT,lcon(%0)),u(#9/FN`WHOROW,itext(0)),%b,|),|))");

			var room = await Eval("loc(#1)");
			await EventService.TriggerEventAsync(
				WebAppFactoryArg.CommandParser,
				SharpEvents.RoomContents,
				WebAppFactoryArg.ExecutorDBRef,
				room,
				"move-in");

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
			await ClearScratch();
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
			await EventService.TriggerEventAsync(
				WebAppFactoryArg.CommandParser,
				SharpEvents.RoomContents,
				WebAppFactoryArg.ExecutorDBRef,
				room,
				"move-in");

			var sentinel = await Eval("get(#9/FANOUT_SENTINEL)");
			var emptyStr = string.Empty;
			// Sentinel must be empty because the handler was cleared before the trigger.
			await Assert.That(sentinel).IsEqualTo(emptyStr);
		}
		finally
		{
			await ClearScratch();
		}
	}
}
