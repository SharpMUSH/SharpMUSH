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
/// The simplified handler records lcon(%0)/words(lcon(%0)) into scratch attributes on #9
/// instead of calling oob() — oob() requires WebSocket connections that the unit test
/// harness does not provide — so assertions focus on fan-out targeting logic.
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

	[Test]
	public async ValueTask HandlerReceivesCorrectRoomDbrefInPercent0()
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

		// Cleanup
		await Cmd("&ROOM`CONTENTS #9=");
		await Cmd("&FANOUT_LIST #9=");
	}

	[Test]
	public async ValueTask HandlerCountsOccupantsMatchingIndependentLcon()
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

		// Cleanup
		await Cmd("&ROOM`CONTENTS #9=");
		await Cmd("&FANOUT_COUNT #9=");
	}

	[Test]
	public async ValueTask HandlerCauseArgIsPassedAsPercent1()
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

		// Cleanup
		await Cmd("&ROOM`CONTENTS #9=");
		await Cmd("&LAST_CAUSE #9=");
	}

	[Test]
	public async ValueTask HandlerDoesNotRunAfterAttributeIsCleared()
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

		// Cleanup (belt-and-suspenders in case the test fails mid-way)
		await Cmd("&ROOM`CONTENTS #9=");
		await Cmd("&FANOUT_SENTINEL #9=");
	}
}
