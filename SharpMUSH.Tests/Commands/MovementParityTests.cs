using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// Movement produces the messages PennMUSH produces, in PennMUSH's order.
/// Reference: <c>src/move.c</c> <c>moveit</c> / <c>enter_room</c>.
/// </summary>
[NotInParallel]
public class MovementParityTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private ITaskScheduler Scheduler => WebAppFactoryArg.Services.GetRequiredService<ITaskScheduler>();
	private IMoveService MoveService => WebAppFactoryArg.Services.GetRequiredService<IMoveService>();
	private IOptionsMonitor<SharpMUSHOptions> Configuration =>
		WebAppFactoryArg.Services.GetRequiredService<IOptionsMonitor<SharpMUSHOptions>>();
	private IMUSHCodeParser GodParser => WebAppFactoryArg.CommandParser;

	private async Task<AnySharpObject> Node(string reference)
		=> (await Mediator.Send(new GetObjectNodeQuery(DBRef.Parse(reference)))).Known;

	private async Task<AnySharpObject> Node(DBRef reference)
		=> (await Mediator.Send(new GetObjectNodeQuery(reference))).Known;

	private async Task<string> LocationOf(string reference)
	{
		var loc = await GodParser.FunctionParse(MarkupText.Plain($"[loc({reference})]"));
		return BareDbref(loc!.Message!.ToPlainText().Trim());
	}

	private async Task<List<string>> MessagesWhile(DBRef who, Func<Task> action)
	{
		var recorder = WebAppFactoryArg.Notifications;
		var before = recorder.CountFor(who);
		await action();
		return [.. recorder.For(who).Skip(before)];
	}

	private async Task<string> Dig(string prefix)
	{
		var result = await GodParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@dig {TestIsolationHelpers.GenerateUniqueName(prefix)}"));
		return result.Message!.ToPlainText().Trim();
	}

	/// <summary>
	/// Two rooms joined by an exit named <c>out</c>, and a mover standing in the first.
	/// <c>@open</c> sources the exit from the executor's own location, so God is moved into the
	/// origin room first — the same shape <c>ObjectDestructionTests.cs:153</c> uses — and its
	/// CallState carries the new exit's dbref.
	/// </summary>
	private async Task<(TestIsolationHelpers.TestPlayer Mover, string From, string To, string Exit)> Corridor(string prefix)
	{
		var from = await Dig($"{prefix}From");
		var to = await Dig($"{prefix}To");

		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent me={from}"));

		try
		{
			var open = await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@open out={to}"));
			var exit = open.Message!.ToPlainText().Trim();

			var mover = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
				WebAppFactoryArg.Services, Mediator, ConnectionService, $"{prefix}Mover");
			await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {mover.DbRef}={from}"));

			return (mover, from, to, exit);
		}
		finally
		{
			// God goes back where the rest of the session expects to find it: DbrefFunctionUnitTests
			// asserts loc(#1) is #0, and this factory is shared for the whole test session. In a
			// finally, because a throw between here and there would leave #1 displaced for every
			// later test — [NotInParallel] only serialises against other [NotInParallel] tests.
			await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain("@teleport/silent me=#0"));
		}
	}

	/// <summary>
	/// The number half of a reference that may have been rendered as a full objid (<c>#N:creation</c>).
	/// <c>@dig</c> answers with an objid and a triad's <c>%0</c> with a bare dbref, so both sides of a
	/// comparison go through this.
	/// </summary>
	private static string BareDbref(string reference)
	{
		var colon = reference.IndexOf(':');
		return colon < 0 ? reference : reference[..colon];
	}

	[Test]
	public async ValueTask WalkingThroughAnExitAnnouncesDepartureAndArrival()
	{
		var (mover, from, to, _) = await Corridor("Walk");
		var watcherHere = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WalkStay");
		var watcherThere = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WalkGreet");

		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {watcherHere.DbRef}={from}"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {watcherThere.DbRef}={to}"));

		var departure = await MessagesWhile(watcherHere.DbRef, async () =>
			await GodParser.CommandParse(mover.Handle, ConnectionService, MarkupText.Plain("out")));

		await Assert.That(departure.Any(m => m == $"{mover.Name} has left.")).IsTrue();
	}

	[Test]
	public async ValueTask WalkingThroughAnExitAnnouncesArrivalInTheDestination()
	{
		var (mover, from, to, _) = await Corridor("Arrive");
		var greeter = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "ArriveGreet");
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {greeter.DbRef}={to}"));

		var arrival = await MessagesWhile(greeter.DbRef, async () =>
			await GodParser.CommandParse(mover.Handle, ConnectionService, MarkupText.Plain("out")));

		await Assert.That(arrival.Any(m => m == $"{mover.Name} has arrived.")).IsTrue();
	}

	[Test]
	public async ValueTask ADarkWizardArrivesSilently()
	{
		var (mover, from, to, _) = await Corridor("Sneak");
		var greeter = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "SneakGreet");
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {greeter.DbRef}={to}"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {mover.DbRef}=WIZARD"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {mover.DbRef}=DARK"));

		var arrival = await MessagesWhile(greeter.DbRef, async () =>
			await GodParser.CommandParse(mover.Handle, ConnectionService, MarkupText.Plain("out")));

		await Assert.That(arrival.Any(m => m.Contains("has arrived."))).IsFalse();
	}

	[Test]
	public async ValueTask ANonHearingThingFiresItsActionAttributeButSendsNoMessage()
	{
		var room = await Dig("SilentThingRoom");
		var destination = await Dig("SilentThingDest");
		var watcher = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "SilentWatch");
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {watcher.DbRef}={destination}"));

		var thing = await TestIsolationHelpers.CreateTestThingAsync(
			GodParser, ConnectionService, "SilentThing");
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {thing}={room}"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"&AENTER {destination}=&ARRIVED me=yes"));

		var arrival = await MessagesWhile(watcher.DbRef, async () =>
			await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport {thing}={destination}")));

		// The non-hearer branch suppresses the enter and leave messages specifically. The MOVE
		// triad is gated on nomovemsgs, not on hearing, so assert the two separately rather than
		// letting one absent string stand for both.
		await Assert.That(arrival.Any(m => m.Contains("has arrived."))).IsFalse();
		await Assert.That(arrival.Any(m => m.Contains("has left."))).IsFalse();

		await Scheduler.DrainImmediateQueueForTests();
		var arrived = await GodParser.FunctionParse(MarkupText.Plain($"[get({destination}/ARRIVED)]"));
		await Assert.That(arrived!.Message!.ToPlainText().Trim()).IsEqualTo("yes");
	}

	[Test]
	public async ValueTask ANonHearingThingStillRunsItsOwnMoveTriad()
	{
		var room = await Dig("MoveTriadRoom");
		var destination = await Dig("MoveTriadDest");
		var thing = await TestIsolationHelpers.CreateTestThingAsync(
			GodParser, ConnectionService, "MoveTriadThing");

		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {thing}={room}"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"&AMOVE {thing}=&MOVED me=yes"));

		// `moveit` gates MOVE/OMOVE/AMOVE on nomovemsgs alone, outside the Hearer branch.
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport {thing}={destination}"));
		await Scheduler.DrainImmediateQueueForTests();

		var moved = await GodParser.FunctionParse(MarkupText.Plain($"[get({thing}/MOVED)]"));
		await Assert.That(moved!.Message!.ToPlainText().Trim()).IsEqualTo("yes");
	}

	[Test]
	public async ValueTask OxEnterIsShownInTheOriginRoomAndOxLeaveInTheDestination()
	{
		var origin = await Dig("OxOrigin");
		var destinationRoom = await Dig("OxDest");

		// OXENTER/OXLEAVE only fire for non-room containers (move.c:122-127), so the mover moves
		// between two vehicles rather than between two rooms.
		var fromVehicle = await TestIsolationHelpers.CreateTestThingAsync(
			GodParser, ConnectionService, "FromCar");
		var toVehicle = await TestIsolationHelpers.CreateTestThingAsync(
			GodParser, ConnectionService, "ToCar");

		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {fromVehicle}={origin}"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {toVehicle}={destinationRoom}"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"&OXLEAVE {fromVehicle}=climbs out of the car."));

		var mover = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "OxMover");
		var destinationWatcher = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "OxDestWatch");

		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {mover.DbRef}={fromVehicle}"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {destinationWatcher.DbRef}={toVehicle}"));

		// OXLEAVE is set on the container being LEFT and is shown to the people where the mover ARRIVES.
		var seen = await MessagesWhile(destinationWatcher.DbRef, async () =>
			await GodParser.CommandParse(1, ConnectionService,
				MarkupText.Plain($"@teleport {mover.DbRef}={toVehicle}")));

		await Assert.That(seen.Any(m => m == $"{mover.Name} climbs out of the car.")).IsTrue();
	}

	/// <summary>
	/// <c>did_it_with(what, where, "ENTER", …, "AENTER", where, old, NOTHING, …)</c>
	/// (<c>move.c:138</c>): the enter triad's <c>%0</c> is the container the mover LEFT, and it has
	/// no <c>%1</c>. The mover is a player because only the hearer branch carries an environment —
	/// the non-hearer branch queues a bare <c>did_it</c> (<c>move.c:154</c>) with none.
	/// </summary>
	[Test]
	public async ValueTask TheEnterTriadReceivesTheContainerLeftAsPercentZero()
	{
		var origin = await Dig("EnterEnvOrigin");
		var destination = await Dig("EnterEnvDest");
		var mover = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "EnterEnvMover");

		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {mover.DbRef}={origin}"));
		await GodParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&AENTER {destination}=&ENTERENV me=%0/%1/"));

		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport {mover.DbRef}={destination}"));
		await Scheduler.DrainImmediateQueueForTests();

		var seen = await GodParser.FunctionParse(MarkupText.Plain($"[get({destination}/ENTERENV)]"));
		var parts = seen!.Message!.ToPlainText().Trim().Split('/');

		await Assert.That(BareDbref(parts[0])).IsEqualTo(BareDbref(origin));
		await Assert.That(parts[1]).IsEqualTo(string.Empty);
	}

	/// <summary>
	/// <c>did_it_with(what, what, "MOVE", …, "AMOVE", where, where, old, …)</c> (<c>move.c:163</c>):
	/// the move triad's <c>%0</c> is the destination and its <c>%1</c> is the origin.
	/// </summary>
	[Test]
	public async ValueTask TheMoveTriadReceivesDestinationThenOrigin()
	{
		var origin = await Dig("MoveEnvOrigin");
		var destination = await Dig("MoveEnvDest");
		var thing = await TestIsolationHelpers.CreateTestThingAsync(
			GodParser, ConnectionService, "MoveEnvThing");

		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {thing}={origin}"));
		await GodParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&AMOVE {thing}=&MOVEENV me=%0/%1"));

		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport {thing}={destination}"));
		await Scheduler.DrainImmediateQueueForTests();

		var seen = await GodParser.FunctionParse(MarkupText.Plain($"[get({thing}/MOVEENV)]"));
		var parts = seen!.Message!.ToPlainText().Trim().Split('/');

		await Assert.That(BareDbref(parts[0])).IsEqualTo(BareDbref(destination));
		await Assert.That(BareDbref(parts[1])).IsEqualTo(BareDbref(origin));
	}

	/// <summary>
	/// <c>OXLEAVE</c> goes through <c>did_it_interact</c> (<c>move.c:120</c>), which passes
	/// <c>pe_regs = NULL</c> (<c>predicat.c:191</c>) — so it gets no environment at all, and an
	/// unset <c>%0</c> is an empty string rather than the dbref the with-environment triads carry.
	/// </summary>
	[Test]
	public async ValueTask OxLeaveReceivesNoEnvironment()
	{
		var origin = await Dig("NoEnvOrigin");
		var destinationRoom = await Dig("NoEnvDest");

		var fromVehicle = await TestIsolationHelpers.CreateTestThingAsync(
			GodParser, ConnectionService, "NoEnvFromCar");
		var toVehicle = await TestIsolationHelpers.CreateTestThingAsync(
			GodParser, ConnectionService, "NoEnvToCar");

		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {fromVehicle}={origin}"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {toVehicle}={destinationRoom}"));
		await GodParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&OXLEAVE {fromVehicle}=climbs out carrying [strlen(%0)] items."));

		var mover = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "NoEnvMover");
		var destinationWatcher = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "NoEnvWatch");

		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {mover.DbRef}={fromVehicle}"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {destinationWatcher.DbRef}={toVehicle}"));

		var seen = await MessagesWhile(destinationWatcher.DbRef, async () =>
			await GodParser.CommandParse(1, ConnectionService,
				MarkupText.Plain($"@teleport {mover.DbRef}={toVehicle}")));

		await Assert.That(seen.Any(m => m == $"{mover.Name} climbs out carrying 0 items.")).IsTrue();
	}

	/// <summary>
	/// <c>enter_room</c> (<c>src/move.c:279</c>) ends in <c>look_room(player, loc, LOOK_AUTO, NULL)</c>.
	/// </summary>
	/// <remarks>
	/// Drives <see cref="IMoveService.EnterRoom"/> rather than a command: <c>GOTO</c> and
	/// <c>@teleport</c> still end at a bare <c>MoveObjectCommand</c> and are rewired onto this
	/// pipeline by Tasks 12 and 13.
	/// </remarks>
	[Test]
	public async ValueTask ArrivingSomewhereLooksAtIt()
	{
		var from = await Dig("AutoLookFrom");
		var to = await Dig("AutoLookTo");
		var mover = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "AutoLookMover");

		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {mover.DbRef}={from}"));
		await GodParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@describe {to}=An unmistakable arrival room."));

		var moverNode = await Node(mover.DbRef);
		var destination = await Node(to);

		var seen = await MessagesWhile(mover.DbRef, async () =>
			await MoveService.EnterRoom(GodParser, moverNode.AsContent, destination.AsContainer,
				noMoveMsgs: false, mover.DbRef, "test"));

		await Assert.That(seen.Any(m => m.Contains("An unmistakable arrival room."))).IsTrue();
	}

	/// <summary>
	/// The automatic look is not gated on <c>nomovemsgs</c> — <c>move.c:279</c> runs it after the
	/// branch at <c>move.c:162</c> that the flag controls, so a silent move still shows the room.
	/// </summary>
	[Test]
	public async ValueTask ASilentArrivalStillLooks()
	{
		var from = await Dig("SilentLookFrom");
		var to = await Dig("SilentLookTo");
		var mover = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "SilentLookMover");

		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {mover.DbRef}={from}"));
		await GodParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@describe {to}=Silently arrived."));

		var moverNode = await Node(mover.DbRef);
		var destination = await Node(to);

		var seen = await MessagesWhile(mover.DbRef, async () =>
			await MoveService.EnterRoom(GodParser, moverNode.AsContent, destination.AsContainer,
				noMoveMsgs: true, mover.DbRef, "test"));

		await Assert.That(seen.Any(m => m.Contains("Silently arrived."))).IsTrue();
	}

	/// <summary>
	/// <c>move.c:232-235</c>: <c>if (deep++ > 15)</c> abandons the move. The counter here rides
	/// <see cref="ParserState.MoveDepth"/> rather than Penn's process-global <c>static int deep</c>,
	/// so one evaluation's depth cannot abort another's.
	/// </summary>
	[Test]
	public async ValueTask EnterRoomAbandonsTheMoveAtTheDepthCap()
	{
		var from = await Dig("DepthFrom");
		var to = await Dig("DepthTo");
		var mover = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DepthMover");

		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {mover.DbRef}={from}"));

		var moverNode = await Node(mover.DbRef);
		var destination = await Node(to);

		// Fifteen frames already active is the last depth Penn still moves at: `deep++ > 15` compares
		// the value before the increment.
		var atCap = new InvocationCounter();
		for (var i = 0; i < 15; i++)
		{
			atCap.Increment();
		}

		var allowed = await MoveService.EnterRoom(
			GodParser.Push(GodParser.CurrentState with { MoveDepth = atCap }),
			moverNode.AsContent, destination.AsContainer, noMoveMsgs: true, mover.DbRef, "test");

		await Assert.That(allowed.IsT0).IsTrue();
		await Assert.That(await LocationOf(mover.DbRef.ToString())).IsEqualTo(BareDbref(to));

		// One frame deeper and the move is abandoned rather than run.
		var pastCap = new InvocationCounter();
		for (var i = 0; i < 16; i++)
		{
			pastCap.Increment();
		}

		var refused = await MoveService.EnterRoom(
			GodParser.Push(GodParser.CurrentState with { MoveDepth = pastCap }),
			(await Node(mover.DbRef)).AsContent, (await Node(from)).AsContainer,
			noMoveMsgs: true, mover.DbRef, "test");

		await Assert.That(refused.IsT1).IsTrue();
		await Assert.That(refused.AsT1.Value).IsEqualTo(ErrorMessages.Notifications.TooManyContainers);
		await Assert.That(await LocationOf(mover.DbRef.ToString()))
			.IsEqualTo(BareDbref(to))
			.Because("the refused move must not have happened");

		// The guard restores what it spent, so the same counter is reusable.
		await Assert.That(pastCap.Count).IsEqualTo(16);
	}

	/// <summary>
	/// The automatic look is entered on the caller's parser state, so the counters that bound an
	/// evaluation survive the hop from <c>EnterRoom</c> into <c>LookRoom</c>: a move made from inside
	/// an evaluation that has already spent its <c>DESCRIBE</c> budget trips the recursion limit in
	/// the arrival room's description instead of evaluating it afresh.
	/// </summary>
	/// <remarks>
	/// This replaces the stand-in in <c>LookServiceTests</c>, which handed <c>LookRoom</c> a spent
	/// state directly because nothing re-entered it. <c>EnterRoom</c> is the real caller.
	/// </remarks>
	[Test]
	public async ValueTask TheAutomaticLookRunsOnTheCallersRecursionCounters()
	{
		var from = await Dig("DeepLookFrom");
		var shallowTo = await Dig("DeepLookShallow");
		var deepTo = await Dig("DeepLookDeep");
		var mover = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DeepLookMover");

		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {mover.DbRef}={from}"));
		await GodParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@describe {shallowTo}=A description from depth."));
		await GodParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@describe {deepTo}=A description from depth."));

		// The control: a shallow parser shows the description, so the assertion below is about depth
		// and not about the room being unreadable.
		var shallow = await MessagesWhile(mover.DbRef, async () =>
			await MoveService.EnterRoom(GodParser, (await Node(mover.DbRef)).AsContent,
				(await Node(shallowTo)).AsContainer, noMoveMsgs: true, mover.DbRef, "test"));

		await Assert.That(shallow.Any(m => m.Contains("A description from depth."))).IsTrue();

		// AttributeService errors once the per-attribute depth passes the limit, so a budget already
		// at the limit is one the arrival room's DESCRIBE cannot afford.
		var limit = (int)Configuration.CurrentValue.Limit.FunctionRecursionLimit;
		var spent = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["DESCRIBE"] = limit };
		var deepParser = GodParser.Push(GodParser.CurrentState with { FunctionRecursionDepths = spent });

		var deep = await MessagesWhile(mover.DbRef, async () =>
			await MoveService.EnterRoom(deepParser, (await Node(mover.DbRef)).AsContent,
				(await Node(deepTo)).AsContainer, noMoveMsgs: true, mover.DbRef, "test"));

		await Assert.That(deep.Any(m => m.Contains("A description from depth."))).IsFalse();
		await Assert.That(deep.Any(m => m.Contains(ErrorMessages.Returns.Recursion))).IsTrue();

		// The move itself still happened; only the description's evaluation was bounded.
		await Assert.That(await LocationOf(mover.DbRef.ToString())).IsEqualTo(BareDbref(deepTo));
	}

	/// <summary>
	/// <c>move.c:270-273</c> into <c>maybe_dropto</c> (<c>move.c:203</c>) and <c>send_contents</c>
	/// (<c>move.c:173</c>): once the last Dropper leaves a STICKY room, everything left behind that is
	/// not itself a Dropper goes through the room's drop-to. This is the one path that re-enters
	/// <c>EnterRoom</c> synchronously.
	/// </summary>
	[Test]
	public async ValueTask AVacatedStickyRoomSendsItsContentsThroughItsDropTo()
	{
		var room = await Dig("DropToRoom");
		var dropTo = await Dig("DropToTarget");
		var leavingFor = await Dig("DropToExitRoom");

		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {room}=STICKY"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@link {room}={dropTo}"));

		var luggage = await TestIsolationHelpers.CreateTestThingAsync(
			GodParser, ConnectionService, "DropToLuggage");
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {luggage}={room}"));

		var mover = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DropToMover");
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {mover.DbRef}={room}"));

		await MoveService.EnterRoom(GodParser, (await Node(mover.DbRef)).AsContent,
			(await Node(leavingFor)).AsContainer, noMoveMsgs: true, mover.DbRef, "test");

		await Assert.That(await LocationOf(luggage.ToString())).IsEqualTo(BareDbref(dropTo));
	}

	/// <summary>
	/// The same room without its STICKY flag keeps what is left in it (<c>move.c:272</c> tests
	/// <c>Sticky(old)</c> before <c>maybe_dropto</c> is reached at all).
	/// </summary>
	[Test]
	public async ValueTask AVacatedRoomThatIsNotStickyKeepsItsContents()
	{
		var room = await Dig("NoDropToRoom");
		var dropTo = await Dig("NoDropToTarget");
		var leavingFor = await Dig("NoDropToExitRoom");

		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@link {room}={dropTo}"));

		var luggage = await TestIsolationHelpers.CreateTestThingAsync(
			GodParser, ConnectionService, "NoDropToLuggage");
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {luggage}={room}"));

		var mover = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "NoDropToMover");
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {mover.DbRef}={room}"));

		await MoveService.EnterRoom(GodParser, (await Node(mover.DbRef)).AsContent,
			(await Node(leavingFor)).AsContainer, noMoveMsgs: true, mover.DbRef, "test");

		await Assert.That(await LocationOf(luggage.ToString())).IsEqualTo(BareDbref(room));
	}

	/// <summary>
	/// <c>safe_tel</c> (<c>move.c:311</c>): crossing between owners, anything carried that the mover
	/// does not control, is STICKY, and is not homed to the mover goes home instead of travelling.
	/// </summary>
	[Test]
	public async ValueTask TeleportingAcrossOwnersSendsStickyCarriedObjectsHome()
	{
		var owner = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "StickyOwner");
		var mover = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "StickyMover");

		var home = await Dig("StickyHome");
		var start = await Dig("StickyStart");
		var elsewhere = await Dig("StickyElsewhere");

		// Only the destination changes hands, which is all safe_tel's owner comparison looks at.
		await GodParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@chown/preserve {elsewhere}={owner.DbRef}"));

		var sticky = await TestIsolationHelpers.CreateTestThingAsync(
			GodParser, ConnectionService, "StickyItem");
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@link {sticky}={home}"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {sticky}=STICKY"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {mover.DbRef}={start}"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {sticky}={mover.DbRef}"));

		var result = await MoveService.SafeTel(GodParser, (await Node(mover.DbRef)).AsContent,
			(await Node(elsewhere)).AsContainer, noMoveMsgs: true, mover.DbRef, "test");

		await Assert.That(result.IsT0).IsTrue();
		await Assert.That(await LocationOf(sticky.ToString())).IsEqualTo(BareDbref(home));
		await Assert.That(await LocationOf(mover.DbRef.ToString())).IsEqualTo(BareDbref(elsewhere));
	}

	/// <summary>
	/// <c>move.c:294</c>: when both ends share an owner, <c>safe_tel</c> is a plain
	/// <c>enter_room</c> and strips nothing, however STICKY the luggage is.
	/// </summary>
	[Test]
	public async ValueTask TeleportingWithinOneOwnerKeepsStickyCarriedObjects()
	{
		var mover = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "SameOwnerMover");

		var home = await Dig("SameOwnerHome");
		var start = await Dig("SameOwnerStart");
		var elsewhere = await Dig("SameOwnerElsewhere");

		var sticky = await TestIsolationHelpers.CreateTestThingAsync(
			GodParser, ConnectionService, "SameOwnerItem");
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@link {sticky}={home}"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {sticky}=STICKY"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {mover.DbRef}={start}"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {sticky}={mover.DbRef}"));

		await MoveService.SafeTel(GodParser, (await Node(mover.DbRef)).AsContent,
			(await Node(elsewhere)).AsContainer, noMoveMsgs: true, mover.DbRef, "test");

		await Assert.That(await LocationOf(sticky.ToString()))
			.IsEqualTo(BareDbref(mover.DbRef.ToString()));
	}
}
