using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using System.Text;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// Verifies ROOM`CONTENTS fires room-scoped on movement. We install a handler attribute on the
/// configured event_handler (#9) that records the room dbref it was called with, move an object,
/// then read the recorded value back. Runs against whichever DB provider the session selected.
/// </summary>
[NotInParallel]
public class RoomContentsEventTests
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
	public async ValueTask DirectEventServiceTriggerSetsAttribute()
	{
		// Set the handler attribute via CommandParse.
		await Cmd("&ROOM`CONTENTS #9=&FIRED #9=direct");

		// Immediately read back to confirm the set worked.
		var afterSet = await Eval("get(#9/ROOM`CONTENTS)");
		await Assert.That(afterSet).IsEqualTo("&FIRED #9=direct");

		// Fire the event directly with the CommandParser (has Handle=1).
		await EventService.TriggerEventAsync(
			WebAppFactoryArg.CommandParser,
			SharpEvents.RoomContents,
			new DBRef(1),
			"#0",       // %0 = some room dbref
			"move-in"); // %1 = cause

		// Read back the FIRED attribute on #9.
		var fired = await Eval("get(#9/FIRED)");
		await Assert.That(fired).IsEqualTo("direct");

		// Cleanup
		await Cmd("&ROOM`CONTENTS #9=");
		await Cmd("&FIRED #9=");
	}

	[Test]
	public async ValueTask MoveFiresRoomContentsForNewLocation()
	{
		// Install handler: records the room dbref (%0) keyed by cause (%1) so that the
		// two fires (move-in for dest, move-out for origin) don't overwrite each other.
		// secure(%1) is safe: "move-in" and "move-out" contain only letters and a dash,
		// which is valid in a MUSH attribute name.
		await Cmd("&ROOM`CONTENTS #9=&LAST_MOVEIN_[secure(%1)] #9=%0");

		// Create a room and a thing with unique names to avoid state pollution.
		var token = TestIsolationHelpers.GenerateUniqueName("rce");
		var roomName = $"RCERoom_{token}";
		var thingName = $"RCEThing_{token}";

		// @dig returns the room dbref.
		var digResult = await WebAppFactoryArg.CommandParser.CommandParse(1, ConnectionService, MModule.single($"@dig {roomName}"));
		var roomDbref = digResult.Message!.ToPlainText()!.Trim();

		// @create returns the thing dbref.
		var createResult = await WebAppFactoryArg.CommandParser.CommandParse(1, ConnectionService, MModule.single($"@create {thingName}"));
		var thingDbref = createResult.Message!.ToPlainText()!.Trim();

		// Sanity: both must look like dbrefs.
		await Assert.That(roomDbref).StartsWith("#");
		await Assert.That(thingDbref).StartsWith("#");

		// Verify the handler attribute was actually set.
		var handlerAttr = await Eval("get(#9/ROOM`CONTENTS)");
		await Assert.That(handlerAttr).IsEqualTo("&LAST_MOVEIN_[secure(%1)] #9=%0");

		// Move the thing to the room — this should fire ROOM`CONTENTS for the destination
		// (cause "move-in") and for the old location (cause "move-out").
		await Cmd($"@tel {thingDbref}={roomDbref}");

		// Handler should have written the destination room dbref into LAST_MOVEIN_move-in.
		var recorded = await Eval("get(#9/LAST_MOVEIN_move-in)");
		await Assert.That(recorded).IsEqualTo(roomDbref);

		// Cleanup handler attributes so they do not affect other tests.
		await Cmd("&ROOM`CONTENTS #9=");
		await Cmd("&LAST_MOVEIN_move-in #9=");
		await Cmd("&LAST_MOVEIN_move-out #9=");
	}

	[Test]
	public async ValueTask HandlerEnactorIsRealTriggeringObject()
	{
		// Regression test: before the fix, EventService hard-set Enactor = handlerRef (#9),
		// so %# inside any event handler was always #9 rather than the object that caused
		// the event. This test proves the enactor (%#) passed into the handler equals the
		// executor of the @tel command (God, #1), NOT the handler object (#9).

		// Install handler: capture %# (the enactor) into SAW_ENACTOR on #9.
		await Cmd("&ROOM`CONTENTS #9=&SAW_ENACTOR #9=%#");

		var token = TestIsolationHelpers.GenerateUniqueName("enc");
		var roomName = $"EncRoom_{token}";
		var thingName = $"EncThing_{token}";

		var digResult = await WebAppFactoryArg.CommandParser.CommandParse(1, ConnectionService, MModule.single($"@dig {roomName}"));
		var roomDbref = digResult.Message!.ToPlainText()!.Trim();

		var createResult = await WebAppFactoryArg.CommandParser.CommandParse(1, ConnectionService, MModule.single($"@create {thingName}"));
		var thingDbref = createResult.Message!.ToPlainText()!.Trim();

		await Assert.That(roomDbref).StartsWith("#");
		await Assert.That(thingDbref).StartsWith("#");

		// @tel is run as God (#1) via CommandParser.CommandParse(handle=1, ...).
		// MoveObjectCommand.Enactor is set to executor.DBRef = #1 by @TELEPORT.
		// After the fix, %# inside the handler must be #1.
		await Cmd($"@tel {thingDbref}={roomDbref}");

		var sawEnactor = await Eval("get(#9/SAW_ENACTOR)");

		// The triggering enactor is #1 (God), NOT #9 (the handler object).
		var expectedEnactor = "#1";
		var handlerObject = "#9";
		await Assert.That(sawEnactor).IsEqualTo(expectedEnactor);
		await Assert.That(sawEnactor).IsNotEqualTo(handlerObject);

		// Cleanup.
		await Cmd("&ROOM`CONTENTS #9=");
		await Cmd("&SAW_ENACTOR #9=");
	}

	[Test]
	public async ValueTask ConnectFiresRoomContentsForPlayerRoom()
	{
		// Install handler on #9: record room dbref keyed by cause.
		// "connect" contains only letters, valid as an attribute-name suffix.
		await Cmd("&ROOM`CONTENTS #9=&LAST_CONN_[secure(%1)] #9=%0");

		// Resolve God's (#1) current room so we can assert against it after connect.
		var mediator = WebAppFactoryArg.Services.GetRequiredService<IMediator>();
		var godNode = await mediator.Send(new GetObjectNodeQuery(new DBRef(1)));
		var godRoom = (await godNode.AsPlayer.Location.WithCancellation(CancellationToken.None)).Object().DBRef.ToString();

		// Use a fresh handle (9001) so there is no "already logged in" rejection.
		// The connect command works with an unregistered handle (connectionData may be null;
		// the command falls back to "unknown" for ipAddress in that case).
		var connectHandle = 9001L;
		await ConnectionService.Register(
			connectHandle,
			"127.0.0.1", "localhost", "test",
			_ => ValueTask.CompletedTask,
			_ => ValueTask.CompletedTask,
			() => Encoding.UTF8);

		// Issue "connect God" — God has no password, so an empty password is accepted.
		await WebAppFactoryArg.CommandParser.CommandParse(
			connectHandle, ConnectionService, MModule.single("connect God"));

		// ROOM`CONTENTS should have fired with %1="connect" and %0=godRoom.
		var recorded = await Eval("get(#9/LAST_CONN_connect)");
		await Assert.That(recorded).IsEqualTo(godRoom);

		// Cleanup: disconnect the extra handle and remove handler attributes.
		await ConnectionService.Disconnect(connectHandle);
		await Cmd("&ROOM`CONTENTS #9=");
		await Cmd("&LAST_CONN_connect #9=");
	}

	[Test]
	public async ValueTask DisconnectFiresRoomContentsForPlayerRoom()
	{
		// Install handler on #9: record room dbref keyed by cause.
		// "disconnect" contains only letters, valid as an attribute-name suffix.
		await Cmd("&ROOM`CONTENTS #9=&LAST_DISC_[secure(%1)] #9=%0");

		// Resolve God's (#1) current room so we can assert against it after disconnect.
		var mediator = WebAppFactoryArg.Services.GetRequiredService<IMediator>();
		var godNode = await mediator.Send(new GetObjectNodeQuery(new DBRef(1)));
		var godRoom = (await godNode.AsPlayer.Location.WithCancellation(CancellationToken.None)).Object().DBRef.ToString();

		// Register a fresh handle and bind it to God (#1) so we have a LoggedIn connection.
		var disconnectHandle = 9002L;
		await ConnectionService.Register(
			disconnectHandle,
			"127.0.0.1", "localhost", "test",
			_ => ValueTask.CompletedTask,
			_ => ValueTask.CompletedTask,
			() => Encoding.UTF8);
		await ConnectionService.Bind(disconnectHandle, WebAppFactoryArg.ExecutorDBRef);

		// Disconnect the handle — ConnectionStateEventHandler should fire ROOM`CONTENTS
		// for God's room with cause "disconnect".
		await ConnectionService.Disconnect(disconnectHandle);

		// Poll (bounded) until the disconnect notification handler records the room, rather than
		// relying on a fixed sleep that can flake under variable CI timing.
		var recorded = string.Empty;
		for (var attempt = 0; attempt < 50; attempt++)
		{
			recorded = await Eval("get(#9/LAST_DISC_disconnect)");
			if (recorded == godRoom) break;
			await Task.Delay(20);
		}

		// ROOM`CONTENTS should have fired with %1="disconnect" and %0=godRoom.
		await Assert.That(recorded).IsEqualTo(godRoom);

		// Cleanup.
		await Cmd("&ROOM`CONTENTS #9=");
		await Cmd("&LAST_DISC_disconnect #9=");
	}
}
