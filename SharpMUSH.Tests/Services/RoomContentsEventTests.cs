using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Notifications;
using SharpMUSH.Library.ParserInterfaces;
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
		WebAppFactoryArg.CommandParser.CommandParse(1, ConnectionService, MarkupText.Plain(command)).AsTask();

	/// <summary>Evaluates <paramref name="expression"/> once the events queued so far have run.</summary>
	private async Task<string> Eval(string expression)
	{
		await WebAppFactoryArg.QueueBarrierAsync();
		return (await WebAppFactoryArg.FunctionParser.EvaluateAsync(MarkupText.Plain(expression))).ToPlainText();
	}

	[Test]
	public async ValueTask DirectEventServiceTriggerSetsAttribute()
	{
		// Recorded under %0, which only this test passes, so no other test's event can write it.
		var marker = TestIsolationHelpers.GenerateUniqueName("RcDirect");
		await Cmd("&ROOM`CONTENTS #9=&FIRED_[secure(%0)] #9=direct");

		// Immediately read back to confirm the set worked.
		var afterSet = await Eval("get(#9/ROOM`CONTENTS)");
		await Assert.That(afterSet).IsEqualTo("&FIRED_[secure(%0)] #9=direct");

		await EventService.TriggerEventAsync(SharpEvents.RoomContents,
			new DBRef(1),
			marker,     // %0 = the room, here a marker
			"move-in"); // %1 = cause

		// Read back the FIRED attribute on #9, once the queued handler has run.
		var fired = await Eval($"get(#9/FIRED_{marker})");
		await Assert.That(fired).IsEqualTo("direct");

		// Cleanup
		await Cmd("&ROOM`CONTENTS #9=");
		await Cmd($"&FIRED_{marker} #9=");
	}

	[Test]
	public async ValueTask MoveFiresRoomContentsForNewLocation()
	{
		// Install handler: records the room dbref (%0) keyed by cause (%1) so that the
		// two fires (move-in for dest, move-out for origin) don't overwrite each other.
		// secure(%1) is safe: "move-in" and "move-out" contain only letters and a dash,
		// which is valid in a MUSH attribute name.
		// Keyed by the room as well, so another test's move cannot overwrite this one's record.
		await Cmd("&ROOM`CONTENTS #9=&LAST_MOVEIN_[secure(%1)]_[after(first(%0,:),#)] #9=%0");

		// Create a room and a thing with unique names to avoid state pollution.
		var token = TestIsolationHelpers.GenerateUniqueName("rce");
		var roomName = $"RCERoom_{token}";
		var thingName = $"RCEThing_{token}";

		// @dig returns the room dbref.
		var digResult = await WebAppFactoryArg.CommandParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@dig {roomName}"));
		var roomDbref = digResult.Message.ToPlainText()!.Trim();

		// @create returns the thing dbref.
		var createResult = await WebAppFactoryArg.CommandParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@create {thingName}"));
		var thingDbref = createResult.Message.ToPlainText()!.Trim();

		// Sanity: both must look like dbrefs.
		await Assert.That(roomDbref).StartsWith("#");
		await Assert.That(thingDbref).StartsWith("#");

		// Verify the handler attribute was actually set.
		var handlerAttr = await Eval("get(#9/ROOM`CONTENTS)");
		await Assert.That(handlerAttr).IsEqualTo("&LAST_MOVEIN_[secure(%1)]_[after(first(%0,:),#)] #9=%0");

		// Move the thing to the room — this should fire ROOM`CONTENTS for the destination
		// (cause "move-in") and for the old location (cause "move-out").
		await Cmd($"@tel {thingDbref}={roomDbref}");

		// Handler should have written the destination room dbref into LAST_MOVEIN_move-in.
		var roomNumber = roomDbref.Split(':')[0][1..];
		var recorded = await Eval($"get(#9/LAST_MOVEIN_move-in_{roomNumber})");
		await Assert.That(recorded).IsEqualTo(roomDbref);

		// Cleanup handler attributes so they do not affect other tests.
		await Cmd("&ROOM`CONTENTS #9=");
		await Cmd("@wipe #9/LAST_MOVEIN_*");
	}

	[Test]
	public async ValueTask HandlerEnactorIsRealTriggeringObject()
	{
		// Regression test: before the fix, EventService hard-set Enactor = handlerRef (#9),
		// so %# inside any event handler was always #9 rather than the object that caused
		// the event. This test proves the enactor (%#) passed into the handler equals the
		// executor of the @tel command (God, #1), NOT the handler object (#9).

		// Install handler: capture %# (the enactor) into SAW_ENACTOR on #9.
		await Cmd("&ROOM`CONTENTS #9=&SAW_ENACTOR_[after(first(%0,:),#)] #9=%#");

		var token = TestIsolationHelpers.GenerateUniqueName("enc");
		var roomName = $"EncRoom_{token}";
		var thingName = $"EncThing_{token}";

		var digResult = await WebAppFactoryArg.CommandParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@dig {roomName}"));
		var roomDbref = digResult.Message.ToPlainText()!.Trim();

		var createResult = await WebAppFactoryArg.CommandParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@create {thingName}"));
		var thingDbref = createResult.Message.ToPlainText()!.Trim();

		await Assert.That(roomDbref).StartsWith("#");
		await Assert.That(thingDbref).StartsWith("#");

		// @tel is run as God (#1) via CommandParser.CommandParse(handle=1, ...).
		// MoveObjectCommand.Enactor is set to executor.DBRef = #1 by @TELEPORT.
		// After the fix, %# inside the handler must be #1.
		await Cmd($"@tel {thingDbref}={roomDbref}");

		var sawEnactor = await Eval($"get(#9/SAW_ENACTOR_{roomDbref.Split(':')[0][1..]})");

		// The triggering enactor is #1 (God), NOT #9 (the handler object).
		var expectedEnactor = "#1";
		var handlerObject = "#9";
		await Assert.That(sawEnactor).IsEqualTo(expectedEnactor);
		await Assert.That(sawEnactor).IsNotEqualTo(handlerObject);

		// Cleanup.
		await Cmd("&ROOM`CONTENTS #9=");
		await Cmd("@wipe #9/SAW_ENACTOR_*");
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
		var godRoom = (await godNode.Expect<SharpPlayer>().Location.WithCancellation(CancellationToken.None)).Object().DBRef.ToString();

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
		using (var budget = new ExecutionBudget(TimeSpan.FromSeconds(30)))
		using (budget.Enter())
		{
			await WebAppFactoryArg.CommandParser.CommandParse(
				connectHandle, ConnectionService, MarkupText.Plain("connect God"));
		}

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
		var godRoom = (await godNode.Expect<SharpPlayer>().Location.WithCancellation(CancellationToken.None)).Object().DBRef.ToString();

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

	/// <summary>
	/// A resumed session gets the state a connect sends (docs/design/d1/README.md §7.1): a reloaded page
	/// has none of it, and the connection server's replay covers only the frames after its lastSeq.
	/// ROOM`CONTENTS and PLAYER`CHANNELS fire for the player with cause "resume".
	/// </summary>
	[Test]
	public async ValueTask ResumeFiresTheConnectTimeSnapshotEvents()
	{
		// The bundled packages' handlers, put back afterwards for the rest of the session.
		var roomHandler = await Eval("get(#9/ROOM`CONTENTS)");
		var channelsHandler = await Eval("get(#9/PLAYER`CHANNELS)");
		try
		{
			await Cmd("&ROOM`CONTENTS #9=&LAST_RESUME_ROOM_[secure(%1)] #9=%0 %#");
			await Cmd("&PLAYER`CHANNELS #9=&LAST_RESUME_CHAN_[secure(%1)] #9=%0 %#");

			var mediator = WebAppFactoryArg.Services.GetRequiredService<IMediator>();
			var godNode = await mediator.Send(new GetObjectNodeQuery(new DBRef(1)));
			var godRoom = (await godNode.Expect<SharpPlayer>().Location.WithCancellation(CancellationToken.None)).Object().DBRef.ToString();

			await WebAppFactoryArg.Services.GetRequiredService<IPublisher>()
				.Publish(new ConnectionResumedNotification(9003, WebAppFactoryArg.ExecutorDBRef));

			var room = string.Empty;
			var channels = string.Empty;
			for (var attempt = 0; attempt < 50 && (room.Length == 0 || channels.Length == 0); attempt++)
			{
				room = await Eval("get(#9/LAST_RESUME_ROOM_resume)");
				channels = await Eval("get(#9/LAST_RESUME_CHAN_resume)");
				if (room.Length == 0 || channels.Length == 0) await Task.Delay(20);
			}

			await Assert.That(room).IsEqualTo($"{godRoom} #1").Because("the player's room, with the player as enactor");
			await Assert.That(await Eval("num(first(get(#9/LAST_RESUME_CHAN_resume)))")).IsEqualTo("#1");
			await Assert.That(await Eval("last(get(#9/LAST_RESUME_CHAN_resume))")).IsEqualTo("#1");
		}
		finally
		{
			await Cmd($"&ROOM`CONTENTS #9={roomHandler}");
			await Cmd($"&PLAYER`CHANNELS #9={channelsHandler}");
			await Cmd("&LAST_RESUME_ROOM_resume #9=");
			await Cmd("&LAST_RESUME_CHAN_resume #9=");
		}
	}
}
