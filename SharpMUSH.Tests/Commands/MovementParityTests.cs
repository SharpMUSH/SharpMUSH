using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Behaviors;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using ZiggyCreatures.Caching.Fusion;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// Movement produces the messages PennMUSH produces, in PennMUSH's order.
/// Reference: <c>src/move.c</c> <c>moveit</c> / <c>enter_room</c>.
/// </summary>
/// <remarks>
/// <para>
/// Most tests here call <see cref="IMoveService.EnterRoom"/> directly and hand it this class's own
/// parser, so none of them can see whether <c>GOTO</c> passes the caller's parser or builds a fresh
/// one — a fresh parser carries fresh counters, which would silently defeat both the
/// <c>MoveDepth</c> cap and the function-recursion guard that
/// <see cref="TheAutomaticLookRunsOnTheCallersRecursionCounters"/> pins, while every one of those
/// tests still passed. <see cref="GotoHandsTheMovePipelineTheCallersCounters"/> is the
/// command-level shape that protects that layer.
/// </para>
/// </remarks>
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
	/// Everything here renders objids — <c>@dig</c>'s answer, <c>loc()</c>, and a triad's <c>%0</c>
	/// alike — but the creation stamp of a room dug in one test is noise when it is compared against a
	/// dbref captured in another, so both sides of a comparison go through this.
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

		// The control: a command that failed outright would satisfy the silence above on its own.
		await Assert.That(await LocationOf(mover.DbRef.ToString())).IsEqualTo(BareDbref(to))
			.Because("the wizard is silent because they are DARK, not because they never moved");
	}

	[Test]
	public async ValueTask ANonHearingThingFiresItsActionAttributeButSendsNoMessage()
	{
		var room = await Dig("SilentThingRoom");
		var destination = await Dig("SilentThingDest");
		var watcher = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "SilentWatch");
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {watcher.DbRef}={destination}"));

		// OLEAVE goes to the room being LEFT (moveit's `loc` is `old`), so a watcher standing in the
		// destination is not evidence about it either way — the departure needs its own witness.
		var leftBehind = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "SilentStay");
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {leftBehind.DbRef}={room}"));

		var thing = await TestIsolationHelpers.CreateTestThingAsync(
			GodParser, ConnectionService, "SilentThing");
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {thing}={room}"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"&AENTER {destination}=&ARRIVED me=yes"));

		var departureBefore = WebAppFactoryArg.Notifications.CountFor(leftBehind.DbRef);

		var arrival = await MessagesWhile(watcher.DbRef, async () =>
			await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport {thing}={destination}")));

		var departure = WebAppFactoryArg.Notifications.For(leftBehind.DbRef).Skip(departureBefore).ToList();

		// The non-hearer branch suppresses the enter and leave messages specifically. The MOVE
		// triad is gated on nomovemsgs, not on hearing, so assert the two separately rather than
		// letting one absent string stand for both.
		await Assert.That(arrival.Any(m => m.Contains("has arrived."))).IsFalse();
		await Assert.That(departure.Any(m => m.Contains("has left."))).IsFalse();

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
	/// Drives <see cref="IMoveService.EnterRoom"/> rather than a command, so the assertion is about
	/// the pipeline itself rather than about either command's wiring onto it.
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

		var result = await MoveService.SafeTel(GodParser, (await Node(mover.DbRef)).AsContent,
			(await Node(elsewhere)).AsContainer, noMoveMsgs: true, mover.DbRef, "test");

		// The control: luggage stays on the mover whenever the teleport does not happen at all, so
		// the move has to be shown to have happened before its location says anything about safe_tel.
		await Assert.That(result.IsT0).IsTrue();
		await Assert.That(await LocationOf(mover.DbRef.ToString())).IsEqualTo(BareDbref(elsewhere));

		await Assert.That(await LocationOf(sticky.ToString()))
			.IsEqualTo(BareDbref(mover.DbRef.ToString()));
	}

	/// <summary>
	/// <c>send_contents</c> (<c>move.c:187</c>): the drop-to is not where a STICKY object goes. It
	/// goes home, and the room's own drop-to takes everything else.
	/// </summary>
	[Test]
	public async ValueTask AVacatedStickyRoomSendsAStickyItemHomeRatherThanThroughItsDropTo()
	{
		var room = await Dig("StickyLuggageRoom");
		var dropTo = await Dig("StickyLuggageTarget");
		var itemHome = await Dig("StickyLuggageHome");
		var leavingFor = await Dig("StickyLuggageExitRoom");

		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {room}=STICKY"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@link {room}={dropTo}"));

		var sticky = await TestIsolationHelpers.CreateTestThingAsync(
			GodParser, ConnectionService, "StickyLuggage");
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@link {sticky}={itemHome}"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {sticky}=STICKY"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {sticky}={room}"));

		var plain = await TestIsolationHelpers.CreateTestThingAsync(
			GodParser, ConnectionService, "StickyLuggageCompanion");
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {plain}={room}"));

		var mover = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "StickyLuggageMover");
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {mover.DbRef}={room}"));

		await MoveService.EnterRoom(GodParser, (await Node(mover.DbRef)).AsContent,
			(await Node(leavingFor)).AsContainer, noMoveMsgs: true, mover.DbRef, "test");

		await Assert.That(await LocationOf(sticky.ToString())).IsEqualTo(BareDbref(itemHome));
		await Assert.That(await LocationOf(plain.ToString())).IsEqualTo(BareDbref(dropTo));
	}

	/// <summary>
	/// <c>maybe_dropto</c> (<c>move.c:212-215</c>): one Dropper still standing in the room and
	/// nothing leaves, however STICKY the room is and wherever it drops to.
	/// </summary>
	[Test]
	public async ValueTask AVacatedStickyRoomKeepsItsContentsWhileADropperRemains()
	{
		var room = await Dig("DropperStaysRoom");
		var dropTo = await Dig("DropperStaysTarget");
		var leavingFor = await Dig("DropperStaysExitRoom");

		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {room}=STICKY"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@link {room}={dropTo}"));

		var luggage = await TestIsolationHelpers.CreateTestThingAsync(
			GodParser, ConnectionService, "DropperStaysLuggage");
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {luggage}={room}"));

		var stayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DropperStayer");
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {stayer.DbRef}={room}"));

		var mover = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DropperStaysMover");
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {mover.DbRef}={room}"));

		await MoveService.EnterRoom(GodParser, (await Node(mover.DbRef)).AsContent,
			(await Node(leavingFor)).AsContainer, noMoveMsgs: true, mover.DbRef, "test");

		await Assert.That(await LocationOf(luggage.ToString())).IsEqualTo(BareDbref(room));
		await Assert.That(await LocationOf(stayer.DbRef.ToString())).IsEqualTo(BareDbref(room));
	}

	/// <summary>
	/// <c>safe_tel</c>'s first condition (<c>move.c:311</c>, <c>!controls(player, tmp)</c>): a STICKY
	/// object the mover controls travels with the mover even across owners.
	/// </summary>
	[Test]
	public async ValueTask TeleportingAcrossOwnersKeepsStickyObjectsTheMoverControls()
	{
		var owner = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "ControlledStickyOwner");
		var mover = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "ControlledStickyMover");

		var home = await Dig("ControlledStickyHome");
		var start = await Dig("ControlledStickyStart");
		var elsewhere = await Dig("ControlledStickyElsewhere");

		await GodParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@chown/preserve {elsewhere}={owner.DbRef}"));

		var sticky = await TestIsolationHelpers.CreateTestThingAsync(
			GodParser, ConnectionService, "ControlledStickyItem");
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@link {sticky}={home}"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {sticky}=STICKY"));
		await GodParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@chown/preserve {sticky}={mover.DbRef}"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {mover.DbRef}={start}"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {sticky}={mover.DbRef}"));

		var result = await MoveService.SafeTel(GodParser, (await Node(mover.DbRef)).AsContent,
			(await Node(elsewhere)).AsContainer, noMoveMsgs: true, mover.DbRef, "test");

		// The control: luggage stays on the mover whenever the teleport does not happen at all, so
		// the move has to be shown to have happened before its location says anything about safe_tel.
		await Assert.That(result.IsT0).IsTrue();
		await Assert.That(await LocationOf(mover.DbRef.ToString())).IsEqualTo(BareDbref(elsewhere));

		await Assert.That(await LocationOf(sticky.ToString()))
			.IsEqualTo(BareDbref(mover.DbRef.ToString()));
	}

	/// <summary>
	/// <c>safe_tel</c>'s last condition (<c>move.c:311</c>, <c>Home(tmp) != player</c>): a STICKY
	/// object homed to the mover is not sent home, because home is where it already is.
	/// </summary>
	/// <remarks>
	/// Sending it anyway would leave it in the same place, so the guard is invisible in the
	/// object's location. It is visible in the triads: the skipped <c>enter_room</c> would fire the
	/// object's own AMOVE (<c>move.c:96</c>), which is what this asserts did not happen. That needs
	/// <c>nomovemsgs</c> off, since <c>moveit</c> gates the move triad on it.
	/// </remarks>
	[Test]
	public async ValueTask TeleportingAcrossOwnersKeepsStickyObjectsHomedToTheMover()
	{
		var owner = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "HomedStickyOwner");
		var mover = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "HomedStickyMover");

		var start = await Dig("HomedStickyStart");
		var elsewhere = await Dig("HomedStickyElsewhere");

		await GodParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@chown/preserve {elsewhere}={owner.DbRef}"));

		// Owned by God, so the mover does not control it; homed to the mover, so it stays anyway.
		var sticky = await TestIsolationHelpers.CreateTestThingAsync(
			GodParser, ConnectionService, "HomedStickyItem");
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@link {sticky}={mover.DbRef}"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {sticky}=STICKY"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {mover.DbRef}={start}"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {sticky}={mover.DbRef}"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"&AMOVE {sticky}=&MOVED me=yes"));

		await MoveService.SafeTel(GodParser, (await Node(mover.DbRef)).AsContent,
			(await Node(elsewhere)).AsContainer, noMoveMsgs: false, mover.DbRef, "test");
		await Scheduler.DrainImmediateQueueForTests();

		await Assert.That(await LocationOf(sticky.ToString()))
			.IsEqualTo(BareDbref(mover.DbRef.ToString()));

		var moved = await GodParser.FunctionParse(MarkupText.Plain($"[get({sticky}/MOVED)]"));
		await Assert.That(moved!.Message!.ToPlainText().Trim()).IsEqualTo(string.Empty);

		// Positive control: an empty MOVED only means the skip fired if the AMOVE would otherwise
		// have run. Move the item itself and the triad must set it.
		await MoveService.EnterRoom(GodParser, (await Node(sticky.ToString())).AsContent,
			(await Node(elsewhere)).AsContainer, noMoveMsgs: false, mover.DbRef, "test");
		await Scheduler.DrainImmediateQueueForTests();

		var movedNow = await GodParser.FunctionParse(MarkupText.Plain($"[get({sticky}/MOVED)]"));
		await Assert.That(movedNow!.Message!.ToPlainText().Trim()).IsEqualTo("yes");
	}

	/// <summary>
	/// <c>could_doit</c> (<c>src/predicat.c:75</c>) is the exit's basic lock, and a failed one runs
	/// <c>fail_lock(player, exit, Basic_Lock, …)</c> (<c>src/move.c:516</c>) — which evaluates the
	/// <c>@fail</c> attribute rather than echoing its stored text.
	/// </summary>
	[Test]
	public async ValueTask AnExitWhoseBasicLockFailsRunsTheFailureTriad()
	{
		var (mover, from, _, exit) = await Corridor("Locked");
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@lock {exit}=#0"));
		await GodParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&FAILURE {exit}=The door is [switch(1,1,stuck)]."));

		var seen = await MessagesWhile(mover.DbRef, async () =>
			await GodParser.CommandParse(mover.Handle, ConnectionService, MarkupText.Plain("out")));

		// Evaluated, not echoed raw.
		await Assert.That(seen.Any(m => m == "The door is stuck.")).IsTrue();
		await Assert.That(await LocationOf(mover.DbRef.ToString())).IsEqualTo(BareDbref(from));
	}

	/// <summary>
	/// The leave lock on the room the mover is standing in is evaluated before the exit's own lock
	/// (<c>src/move.c:441</c>), and fails through <c>LFAIL</c>.
	/// </summary>
	[Test]
	public async ValueTask AFailedLeaveLockRunsTheLeaveFailureTriad()
	{
		var (mover, from, _, _) = await Corridor("LeaveLocked");
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@lock/leave {from}=#0"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"&LFAIL {from}=The walls hold you."));

		var seen = await MessagesWhile(mover.DbRef, async () =>
			await GodParser.CommandParse(mover.Handle, ConnectionService, MarkupText.Plain("out")));

		await Assert.That(seen.Any(m => m == "The walls hold you.")).IsTrue();
		await Assert.That(await LocationOf(mover.DbRef.ToString())).IsEqualTo(BareDbref(from));
	}

	/// <summary>
	/// <c>did_it_with(player, exit, "SUCCESS", …)</c> then <c>did_it(player, exit, "DROP", …, var_dest)</c>
	/// (<c>src/move.c:480-484</c>).
	/// </summary>
	[Test]
	public async ValueTask AnExitFiresItsSuccessTriadAndItsDropTriadInTheDestination()
	{
		var (mover, _, to, exit) = await Corridor("Triads");
		var greeter = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "TriadGreet");
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {greeter.DbRef}={to}"));

		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"&SUCCESS {exit}=You slip through."));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"&ODROP {exit}=slips in."));

		var moverSaw = await MessagesWhile(mover.DbRef, async () =>
			await GodParser.CommandParse(mover.Handle, ConnectionService, MarkupText.Plain("out")));
		await Assert.That(moverSaw.Any(m => m == "You slip through.")).IsTrue();

		// @odrop on an exit is shown where the mover arrives (move.c:483, loc = var_dest).
		var greeterSaw = WebAppFactoryArg.Notifications.For(greeter.DbRef).ToList();
		await Assert.That(greeterSaw.Any(m => m == $"{mover.Name} slips in.")).IsTrue();
	}

	/// <summary>
	/// Every write must name the container it came out of. A <c>MoveObjectCommand</c> sent without
	/// one falls back to the global <c>CacheTags.ObjectContents</c> tag, which drops every
	/// container's cached contents on every step through an exit.
	/// </summary>
	/// <remarks>
	/// This has to observe the INVALIDATION, not the value. <c>CacheTags.ObjectContents</c> is
	/// over-invalidation, never wrong answers, so nothing observable downstream distinguishes the two:
	/// an <c>lcon()</c> re-reads and returns the identical list either way, and a bystander's cache
	/// entry is repopulated on the next read regardless. The declared tag set is the observable, and
	/// <c>engine-data-trunk.md</c> makes it the contract — caching policy is data on the request.
	/// </remarks>
	[Test]
	public async ValueTask AMoveExpiresOnlyTheTwoContainersItNames()
	{
		var origin = DBRef.Parse(await Dig("TagOrigin"));
		var destination = DBRef.Parse(await Dig("TagDest"));
		var thing = await TestIsolationHelpers.CreateTestThingAsync(GodParser, ConnectionService, "TagThing");
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {thing}={origin}"));

		var mover = (await Mediator.Send(new GetObjectNodeQuery(thing))).Known.AsContent;
		var into = (await Mediator.Send(new GetObjectNodeQuery(destination))).Known.AsContainer;

		var move = new MoveObjectCommand(mover, into, origin);

		await Assert.That(move.CacheTags).Contains(CacheKeys.ContentsTag(origin.Number))
			.Because("the origin's contents list is what the move actually changes");
		await Assert.That(move.CacheTags).Contains(CacheKeys.ContentsTag(destination.Number));
		await Assert.That(move.CacheTags).DoesNotContain(CacheTags.ObjectContents)
			.Because("expiring every container in the game is what naming the origin exists to avoid");
		await Assert.That(move.CacheKeys).Contains(CacheKeys.Contents(origin));
		await Assert.That(move.CacheKeys).Contains(CacheKeys.Contents(destination));
	}

	/// <summary>
	/// <c>GOTO</c> hands <see cref="IMoveService.EnterRoom"/> the caller's parser, so the counters
	/// that bound an evaluation still bound the move it makes.
	/// </summary>
	/// <remarks>
	/// This is the one assertion in the class that can see the command's wiring. Every other test
	/// calls <c>EnterRoom</c> itself, so a <c>GOTO</c> that built a fresh parser — fresh counters,
	/// no bound at all — would leave all of them green.
	/// </remarks>
	[Test]
	public async ValueTask GotoHandsTheMovePipelineTheCallersCounters()
	{
		var (mover, from, to, _) = await Corridor("CounterThread");

		ParserState AsMover(InvocationCounter depth) => GodParser.CurrentState with
		{
			Executor = mover.DbRef,
			Enactor = mover.DbRef,
			Caller = mover.DbRef,
			MoveDepth = depth
		};

		// One frame past enter_room's cap (move.c:232): the move must be abandoned.
		var pastCap = new InvocationCounter();
		for (var i = 0; i < 16; i++)
		{
			pastCap.Increment();
		}

		await GodParser.Push(AsMover(pastCap)).CommandParse(MarkupText.Plain("goto out"));

		await Assert.That(await LocationOf(mover.DbRef.ToString()))
			.IsEqualTo(BareDbref(from))
			.Because("GOTO must spend the caller's MoveDepth, not a counter of its own");

		// Control: the same walk on a fresh counter goes through, so the refusal above is the
		// counter and not the corridor.
		await GodParser.Push(AsMover(new InvocationCounter())).CommandParse(MarkupText.Plain("goto out"));

		await Assert.That(await LocationOf(mover.DbRef.ToString())).IsEqualTo(BareDbref(to));
	}

	/// <summary>
	/// <c>follower_command</c> (<c>src/move.c:1458</c>) re-issues the leader's <c>GOTO</c> for every
	/// follower who was standing with them.
	/// </summary>
	[Test]
	public async ValueTask AFollowerTrailsItsLeaderThroughAnExit()
	{
		var (leader, from, to, _) = await Corridor("Follow");
		var follower = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "FollowTrail");

		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {follower.DbRef}={from}"));
		await GodParser.CommandParse(follower.Handle, ConnectionService, MarkupText.Plain($"follow {leader.Name}"));
		await GodParser.CommandParse(leader.Handle, ConnectionService, MarkupText.Plain("out"));
		await Scheduler.DrainImmediateQueueForTests();

		await Assert.That(await LocationOf(follower.DbRef.ToString())).IsEqualTo(BareDbref(to));
	}

	/// <summary>
	/// <c>Connected(follower) || IsThing(follower)</c> (<c>src/move.c:1481</c>): a player who is not
	/// logged in is left where they stood rather than walked around the game by whoever they last
	/// followed. The FOLLOWERS attribute outlives the connection, so nothing else stops this.
	/// </summary>
	[Test]
	public async ValueTask ALoggedOutFollowerIsLeftBehind()
	{
		var (leader, from, _, _) = await Corridor("FollowOffline");
		var follower = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "FollowOfflineTrail");

		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {follower.DbRef}={from}"));
		await GodParser.CommandParse(follower.Handle, ConnectionService, MarkupText.Plain($"follow {leader.Name}"));

		await ConnectionService.Disconnect(follower.Handle);

		await GodParser.CommandParse(leader.Handle, ConnectionService, MarkupText.Plain("out"));
		await Scheduler.DrainImmediateQueueForTests();

		await Assert.That(await LocationOf(follower.DbRef.ToString()))
			.IsEqualTo(BareDbref(from))
			.Because("follower_command skips a follower that is neither connected nor a thing");
	}

	/// <summary>
	/// <c>Dark(Location(follower)) &amp;&amp; !Light(leader)</c> (<c>src/move.c:1482</c>): a departure
	/// the follower could not have seen is not one they can follow. The leader here is not
	/// <c>DarkLegal</c> — the room is what hides them.
	/// </summary>
	[Test]
	public async ValueTask AFollowerInADarkRoomDoesNotSeeAnUnlitLeaderLeave()
	{
		var (leader, from, _, _) = await Corridor("FollowDark");
		var follower = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "FollowDarkTrail");

		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {follower.DbRef}={from}"));
		await GodParser.CommandParse(follower.Handle, ConnectionService, MarkupText.Plain($"follow {leader.Name}"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {from}=DARK"));

		await GodParser.CommandParse(leader.Handle, ConnectionService, MarkupText.Plain("out"));
		await Scheduler.DrainImmediateQueueForTests();

		await Assert.That(await LocationOf(follower.DbRef.ToString()))
			.IsEqualTo(BareDbref(from))
			.Because("the leader is neither lit nor visible, so there was nothing to follow");
	}

	/// <summary>
	/// <c>@teleport/silent</c> passes <c>nomovemsgs</c> to <c>safe_tel</c> and skips the <c>TPORT</c>
	/// triad (<c>src/wiz.c:568-579</c>). It reaches neither the <c>ENTER</c> triad nor the automatic
	/// look, both of which are inside <c>enter_room</c> below that flag.
	/// </summary>
	[Test]
	public async ValueTask SilentTeleportSuppressesTheMoveTriadButNotTheEnterTriad()
	{
		var destination = await Dig("SilentSplit");
		var mover = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "SilentSplitMover");

		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"&AMOVE {mover.DbRef}=&MOVED me=yes"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"&AENTER {destination}=&ENTERED me=yes"));

		await GodParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@teleport/silent {mover.DbRef}={destination}"));
		await Scheduler.DrainImmediateQueueForTests();

		var moved = await GodParser.FunctionParse(MarkupText.Plain($"[get({mover.DbRef}/MOVED)]"));
		var entered = await GodParser.FunctionParse(MarkupText.Plain($"[get({destination}/ENTERED)]"));

		await Assert.That(moved!.Message!.ToPlainText().Trim()).IsEmpty();
		await Assert.That(entered!.Message!.ToPlainText().Trim()).IsEqualTo("yes");
	}

	/// <summary>
	/// <c>did_it_with(victim, victim, "TPORT", …, "OTPORT", …, "ATPORT", …)</c>
	/// (<c>src/wiz.c:578</c>) — the attributes are <c>TPORT</c>, not <c>TELEPORT</c>.
	/// </summary>
	[Test]
	public async ValueTask TeleportFiresTheTportTriad()
	{
		var destination = await Dig("TportDest");
		var mover = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "TportMover");
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"&TPORT {mover.DbRef}=The world folds."));

		var seen = await MessagesWhile(mover.DbRef, async () =>
			await GodParser.CommandParse(1, ConnectionService,
				MarkupText.Plain($"@teleport {mover.DbRef}={destination}")));

		await Assert.That(seen.Any(m => m == "The world folds.")).IsTrue();
	}

	/// <summary>
	/// <c>did_it_with(player, exit, "SUCCESS", …, NOTHING, Location(player), NOTHING, …)</c>
	/// (<c>src/move.c:480-482</c>): the exit's success triad runs with the room being LEFT in
	/// <c>%0</c>, not with the destination and not with nothing.
	/// </summary>
	[Test]
	public async ValueTask AnExitsSuccessTriadSeesTheRoomBeingLeftInEnvZero()
	{
		var (mover, from, to, exit) = await Corridor("SuccessEnv");
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"&SUCCESS {exit}=%0"));

		var seen = await MessagesWhile(mover.DbRef, async () =>
			await GodParser.CommandParse(mover.Handle, ConnectionService, MarkupText.Plain("out")));

		await Assert.That(seen.Any(m => BareDbref(m.Trim()) == BareDbref(from)))
			.IsTrue()
			.Because($"%0 must be the departure room {BareDbref(from)}, not {BareDbref(to)} or empty");
	}

	/// <summary>
	/// <c>recursive_member(destination, victim, 0) || (victim == destination)</c> (<c>src/wiz.c:440</c>)
	/// answers "Bad destination." and returns — before <c>OXTPORT</c>, so a refused teleport never
	/// tells the room that someone left.
	/// </summary>
	[Test]
	public async ValueTask ARefusedTeleportAnnouncesNoDeparture()
	{
		var god = (await Node("#1")).Object().DBRef;
		var room = await Dig("BadDestRoom");
		var box = await TestIsolationHelpers.CreateTestThingAsync(GodParser, ConnectionService, "BadDestBox");
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {box}={room}"));

		var watcher = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "BadDestWatcher");
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {watcher.DbRef}={room}"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"&OXTPORT {box}=folds out of the world."));

		var watcherBefore = WebAppFactoryArg.Notifications.CountFor(watcher.DbRef);

		var godSaw = await MessagesWhile(god, async () =>
			await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport {box}={box}")));

		var watcherSaw = WebAppFactoryArg.Notifications.For(watcher.DbRef).Skip(watcherBefore).ToList();

		await Assert.That(godSaw.Any(m => m == ErrorMessages.Notifications.BadDestination)).IsTrue();
		await Assert.That(watcherSaw.Any(m => m.Contains("folds out of the world."))).IsFalse();
		await Assert.That(await LocationOf(box.ToString())).IsEqualTo(BareDbref(room));
	}

	/// <summary>
	/// <c>src/wiz.c:585-588</c>: the TELEPORTER is told "Teleported.", and only when the victim is
	/// someone other than themselves.
	/// </summary>
	[Test]
	public async ValueTask TeleportingSomeoneElseConfirmsToTheTeleporterAndNotToThemselves()
	{
		var god = (await Node("#1")).Object().DBRef;
		var destination = await Dig("ConfirmDest");
		var mover = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "ConfirmMover");

		var godSaw = await MessagesWhile(god, async () =>
			await GodParser.CommandParse(1, ConnectionService,
				MarkupText.Plain($"@teleport {mover.DbRef}={destination}")));

		await Assert.That(godSaw.Any(m => m == ErrorMessages.Notifications.Teleported)).IsTrue();

		var elsewhere = await Dig("ConfirmSelf");
		var moverSaw = await MessagesWhile(mover.DbRef, async () =>
			await GodParser.CommandParse(mover.Handle, ConnectionService,
				MarkupText.Plain($"@teleport {elsewhere}")));

		await Assert.That(moverSaw.Any(m => m == ErrorMessages.Notifications.Teleported))
			.IsFalse()
			.Because("victim == player is exactly the case wiz.c:585 excludes");

		// The control: a @teleport that was refused outright would say nothing either.
		await Assert.That(await LocationOf(mover.DbRef.ToString())).IsEqualTo(BareDbref(elsewhere))
			.Because("the silence has to come from the victim==player branch, not from a failed move");
	}

	/// <summary>
	/// <c>src/wiz.c:483</c>: sending a player TO a player lands them beside that player.
	/// <c>@teleport/inside</c> is what asks for the containment instead.
	/// </summary>
	[Test]
	public async ValueTask TeleportingAPlayerToAPlayerLandsBesideThemUnlessInside()
	{
		var room = await Dig("InsideRoom");
		var host = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "InsideHost");
		var guest = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "InsideGuest");

		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {host.DbRef}={room}"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport {guest.DbRef}={host.DbRef}"));

		await Assert.That(await LocationOf(guest.DbRef.ToString()))
			.IsEqualTo(BareDbref(room))
			.Because("without /inside the guest arrives in the host's room, not in the host");

		await GodParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@teleport/inside {guest.DbRef}={host.DbRef}"));

		await Assert.That(await LocationOf(guest.DbRef.ToString()))
			.IsEqualTo(BareDbref(host.DbRef.ToString()));
	}

	/// <summary>
	/// <c>Tel_Anywhere(x) = Hasprivs(x) || has_power_by_name(x, "TPORT_ANYWHERE")</c>
	/// (<c>hdrs/mushdb.h:17-18</c>), where <c>Hasprivs</c> is God, Wizard <b>or Royalty</b>
	/// (<c>hdrs/dbdefs.h:195</c>). The refusal at <c>wiz.c:446</c> — a mortal cannot make a player a
	/// destination for another player at all — must therefore let Royalty and a
	/// <c>Tport_Anywhere</c>-power holder through exactly as it lets a Wizard through, even though
	/// neither is a Wizard.
	/// </summary>
	[Test]
	[Arguments("ROYALTY", null)]
	[Arguments(null, "Tport_Anywhere")]
	public async ValueTask ARoyaltyOrTportAnywherePlayerPassesTheSamePlayerToPlayerCheckAsAWizard(
		string? flag, string? power)
	{
		var room = await Dig("TelAnywhereRoom");
		var host = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "TelAnywhereHost");
		var mover = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "TelAnywhereMover");

		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {host.DbRef}={room}"));

		if (flag is not null)
		{
			await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {mover.DbRef}={flag}"));
		}

		if (power is not null)
		{
			await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@power {mover.DbRef}={power}"));
		}

		// wiz.c:446 fires on a plain mortal self-teleporting onto another player: neither flag nor
		// power is Wizard, so a predicate that only tested IsWizard() would refuse this exactly as it
		// would refuse a mortal.
		var moverSaw = await MessagesWhile(mover.DbRef, async () =>
			await GodParser.CommandParse(mover.Handle, ConnectionService,
				MarkupText.Plain($"@teleport me={host.DbRef}")));

		await Assert.That(moverSaw.Any(m => m == ErrorMessages.Notifications.BadDestination))
			.IsFalse()
			.Because("Royalty and a Tport_Anywhere holder are Tel_Anywhere just like a Wizard");
		await Assert.That(await LocationOf(mover.DbRef.ToString()))
			.IsEqualTo(BareDbref(room))
			.Because("without /inside the mover lands beside the host, in the host's room");
	}

	/// <summary>
	/// The mortal control for <see cref="ARoyaltyOrTportAnywherePlayerPassesTheSamePlayerToPlayerCheckAsAWizard"/>:
	/// without Wizard, Royalty or <c>Tport_Anywhere</c>, <c>wiz.c:446</c> refuses before the move.
	/// </summary>
	[Test]
	public async ValueTask AMortalCannotTeleportSelfOntoAnotherPlayer()
	{
		var room = await Dig("TelAnywhereMortalRoom");
		var host = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "TelAnywhereMortalHost");
		var mover = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "TelAnywhereMortalMover");

		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {host.DbRef}={room}"));

		var moverSaw = await MessagesWhile(mover.DbRef, async () =>
			await GodParser.CommandParse(mover.Handle, ConnectionService,
				MarkupText.Plain($"@teleport me={host.DbRef}")));

		await Assert.That(moverSaw.Any(m => m == ErrorMessages.Notifications.BadDestination)).IsTrue();
	}

	/// <summary>
	/// <c>do_enter</c> hands the whole move to <c>safe_tel</c> (<c>src/move.c:962</c>), which reaches
	/// <c>moveit</c>'s enter triad exactly once. A command that fires the triad itself as well shows
	/// up here as a doubled message.
	/// </summary>
	[Test]
	public async ValueTask EnteringAContainerFiresItsEnterTriadExactlyOnce()
	{
		var room = await Dig("EnterOnce");
		var mover = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "EnterOnceMover");
		var box = await TestIsolationHelpers.CreateTestThingAsync(GodParser, ConnectionService, "EnterBox");

		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {box}={room}"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {mover.DbRef}={room}"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {box}=ENTER_OK"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"&ENTER {box}=You squeeze inside."));

		var seen = await MessagesWhile(mover.DbRef, async () =>
			await GodParser.CommandParse(mover.Handle, ConnectionService, MarkupText.Plain($"enter {box}")));

		await Assert.That(seen.Count(m => m == "You squeeze inside.")).IsEqualTo(1);
		await Assert.That(await LocationOf(mover.DbRef.ToString())).IsEqualTo(BareDbref(box.ToString()));
	}

	/// <summary>
	/// <c>fail_lock(player, thing, Enter_Lock, "Permission denied.", NOTHING)</c>
	/// (<c>src/move.c:954</c>): the <c>EFAIL</c> attribute is evaluated, not echoed, and
	/// <c>OEFAIL</c> reaches the room the mover is standing in.
	/// </summary>
	[Test]
	public async ValueTask AFailedEnterLockRunsTheEnterFailureTriad()
	{
		var room = await Dig("EfailRoom");
		var mover = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "EfailMover");
		var watcher = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "EfailWatch");
		var box = await TestIsolationHelpers.CreateTestThingAsync(GodParser, ConnectionService, "EfailBox");

		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {box}={room}"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {mover.DbRef}={room}"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {watcher.DbRef}={room}"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {box}=ENTER_OK"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@lock/enter {box}=#0"));
		await GodParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&EFAIL {box}=The lid is [switch(1,1,shut)]."));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"&OEFAIL {box}=rattles the lid."));

		var watcherBefore = WebAppFactoryArg.Notifications.CountFor(watcher.DbRef);

		var seen = await MessagesWhile(mover.DbRef, async () =>
			await GodParser.CommandParse(mover.Handle, ConnectionService, MarkupText.Plain($"enter {box}")));

		await Assert.That(seen.Any(m => m == "The lid is shut.")).IsTrue();
		await Assert.That(WebAppFactoryArg.Notifications.For(watcher.DbRef).Skip(watcherBefore)
			.Any(m => m.Contains("rattles the lid."))).IsTrue();
		await Assert.That(await LocationOf(mover.DbRef.ToString())).IsEqualTo(BareDbref(room));
	}

	/// <summary>
	/// With no <c>EFAIL</c> to evaluate, <c>fail_lock</c>'s default is <c>"Permission denied."</c>
	/// (<c>src/move.c:954</c>) — and a container that is neither <c>ENTER_OK</c> nor controlled takes
	/// exactly that path rather than a separate message of its own (<c>move.c:952-955</c>).
	/// </summary>
	[Test]
	public async ValueTask EnteringSomethingNotEnterOkIsTheSameRefusalAsAFailedEnterLock()
	{
		var room = await Dig("NotEnterOkRoom");
		var mover = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "NotEnterOkMover");
		var box = await TestIsolationHelpers.CreateTestThingAsync(GodParser, ConnectionService, "NotEnterOkBox");

		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {box}={room}"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {mover.DbRef}={room}"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"&EFAIL {box}=Not a chance."));

		var seen = await MessagesWhile(mover.DbRef, async () =>
			await GodParser.CommandParse(mover.Handle, ConnectionService, MarkupText.Plain($"enter {box}")));

		await Assert.That(seen.Any(m => m == "Not a chance.")).IsTrue()
			.Because("move.c:952-955 is one condition and one fail_lock, so EFAIL covers both halves");
		await Assert.That(await LocationOf(mover.DbRef.ToString())).IsEqualTo(BareDbref(room));
	}

	/// <summary>
	/// <c>do_enter</c> adds <c>MAT_ABSOLUTE</c> to its match flags only for <c>Hasprivs</c>
	/// (<c>src/move.c:930-931</c>). Without that gate <c>enter #N</c> is a free teleport: any mortal
	/// who learns the dbref of an <c>ENTER_OK</c> thing anywhere in the game walks into it.
	/// </summary>
	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async ValueTask OnlyAPrivilegedPlayerMayEnterARemoteThingByDbref(bool privileged)
	{
		var here = await Dig("RemoteEnterHere");
		var elsewhere = await Dig("RemoteEnterElsewhere");
		var mover = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "RemoteEnterMover");
		var box = await TestIsolationHelpers.CreateTestThingAsync(GodParser, ConnectionService, "RemoteEnterBox");

		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {box}={elsewhere}"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {mover.DbRef}={here}"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {box}=ENTER_OK"));

		if (privileged)
		{
			await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {mover.DbRef}=WIZARD"));
		}

		var seen = await MessagesWhile(mover.DbRef, async () =>
			await GodParser.CommandParse(mover.Handle, ConnectionService,
				MarkupText.Plain($"enter #{box.Number}")));

		if (privileged)
		{
			await Assert.That(await LocationOf(mover.DbRef.ToString()))
				.IsEqualTo(BareDbref(box.ToString()))
				.Because("Hasprivs carries MAT_ABSOLUTE and is exempt from the same-room test (move.c:930-931, 946-948)");
		}
		else
		{
			await Assert.That(seen.Any(m => m == ErrorMessages.Notifications.CantSeeThat)).IsTrue()
				.Because("without MAT_ABSOLUTE the dbref is not a name a mortal can match at all");
			await Assert.That(await LocationOf(mover.DbRef.ToString()))
				.IsEqualTo(BareDbref(here))
				.Because("a mortal naming a remote ENTER_OK thing by dbref must not move");
		}
	}

	/// <summary>
	/// <c>MAT_PLAYER</c> resolves <c>*Name</c> game-wide, so stripping <c>MAT_ABSOLUTE</c> is not on
	/// its own enough: <c>src/move.c:946-948</c> refuses any non-<c>Hasprivs</c> enter whose target is
	/// not in the mover's own location, whatever scope matched it.
	/// </summary>
	[Test]
	public async ValueTask AMortalCannotEnterARemotePlayerMatchedByStarName()
	{
		var here = await Dig("StarEnterHere");
		var elsewhere = await Dig("StarEnterElsewhere");
		var mover = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "StarEnterMover");
		var host = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "StarEnterHost");

		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {host.DbRef}={elsewhere}"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {mover.DbRef}={here}"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {host.DbRef}=ENTER_OK"));

		var seen = await MessagesWhile(mover.DbRef, async () =>
			await GodParser.CommandParse(mover.Handle, ConnectionService,
				MarkupText.Plain($"enter *{host.Name}")));

		await Assert.That(seen.Any(m => m == ErrorMessages.Notifications.DontSeeThatHere)).IsTrue()
			.Because("move.c:947 answers a remote enter with \"I don't see that here.\"");
		await Assert.That(await LocationOf(mover.DbRef.ToString()))
			.IsEqualTo(BareDbref(here))
			.Because("only Hasprivs may enter something that is not in the mover's location");
	}

	/// <summary>
	/// <c>do_leave</c> is <c>enter_room(player, Location(loc), …)</c> (<c>src/move.c:986</c>), whose
	/// leave triad fires once and whose automatic look is the command's only look.
	/// </summary>
	[Test]
	public async ValueTask LeavingAContainerFiresItsLeaveTriadExactlyOnce()
	{
		var roomName = TestIsolationHelpers.GenerateUniqueName("LeaveOnce");
		var room = (await GodParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@dig {roomName}"))).Message!.ToPlainText().Trim();
		var mover = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "LeaveOnceMover");
		var box = await TestIsolationHelpers.CreateTestThingAsync(GodParser, ConnectionService, "LeaveBox");

		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {box}={room}"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {box}=ENTER_OK"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"&LEAVE {box}=You climb out."));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {mover.DbRef}={box}"));

		var seen = await MessagesWhile(mover.DbRef, async () =>
			await GodParser.CommandParse(mover.Handle, ConnectionService, MarkupText.Plain("leave")));

		await Assert.That(seen.Count(m => m == "You climb out.")).IsEqualTo(1);

		// enter_room's look is the only one: a trailing `look` in the command would name the room twice.
		await Assert.That(seen.Count(m => m.StartsWith(roomName, StringComparison.Ordinal))).IsEqualTo(1);
		await Assert.That(await LocationOf(mover.DbRef.ToString())).IsEqualTo(BareDbref(room));
	}

	/// <summary>
	/// <c>IsRoom(loc)</c>, <c>NoLeave(loc)</c> and a failed leave lock are the same
	/// <c>fail_lock(player, loc, Leave_Lock, "You can't leave.", NOTHING)</c> (<c>src/move.c:981-983</c>),
	/// so each runs the <c>LFAIL</c> triad rather than a message of its own.
	/// </summary>
	[Test]
	[Arguments("NO_LEAVE")]
	[Arguments(null)]
	public async ValueTask LeavingSomethingThatRefusesRunsTheLeaveFailureTriad(string? flag)
	{
		var room = await Dig("LfailRoom");
		var mover = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "LfailMover");
		var box = await TestIsolationHelpers.CreateTestThingAsync(GodParser, ConnectionService, "LfailBox");

		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {box}={room}"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {box}=ENTER_OK"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"&LFAIL {box}=The lid won't budge."));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {mover.DbRef}={box}"));

		if (flag is null)
		{
			await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@lock/leave {box}=#0"));
		}
		else
		{
			await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {box}={flag}"));
		}

		var seen = await MessagesWhile(mover.DbRef, async () =>
			await GodParser.CommandParse(mover.Handle, ConnectionService, MarkupText.Plain("leave")));

		await Assert.That(seen.Any(m => m == "The lid won't budge.")).IsTrue();
		await Assert.That(await LocationOf(mover.DbRef.ToString())).IsEqualTo(BareDbref(box.ToString()));
	}

	/// <summary>
	/// <c>do_move</c>'s home branch (<c>src/move.c:407-418</c>): one broadcast to the room, three
	/// identical lines to the mover, then <c>safe_tel</c>.
	/// </summary>
	[Test]
	public async ValueTask GoingHomeSaysSoThreeTimesAndTellsTheRoom()
	{
		var room = await Dig("HomeStart");
		var home = await Dig("HomeTarget");
		var mover = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "HomeMover");
		var watcher = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "HomeWatch");

		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@link {mover.DbRef}={home}"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {mover.DbRef}={room}"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {watcher.DbRef}={room}"));

		var watcherBefore = WebAppFactoryArg.Notifications.CountFor(watcher.DbRef);

		var moverSaw = await MessagesWhile(mover.DbRef, async () =>
			await GodParser.CommandParse(mover.Handle, ConnectionService, MarkupText.Plain("home")));

		await Assert.That(moverSaw.Count(m => m == ErrorMessages.Notifications.NoPlaceLikeHome)).IsEqualTo(3);
		await Assert.That(WebAppFactoryArg.Notifications.For(watcher.DbRef).Skip(watcherBefore)
			.Any(m => m.Contains($"{mover.Name} goes home."))).IsTrue();
		await Assert.That(await LocationOf(mover.DbRef.ToString())).IsEqualTo(BareDbref(home));
	}

	/// <summary>
	/// <c>move.c:407</c> gates the broadcast on both the mover and the room it stands in being lit;
	/// a Dark mover leaves without announcing it.
	/// </summary>
	[Test]
	public async ValueTask ADarkMoverGoesHomeWithoutTellingTheRoom()
	{
		var room = await Dig("DarkHomeStart");
		var home = await Dig("DarkHomeTarget");
		var mover = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DarkHomeMover");
		var watcher = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DarkHomeWatch");

		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@link {mover.DbRef}={home}"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {mover.DbRef}={room}"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {watcher.DbRef}={room}"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {mover.DbRef}=DARK"));

		var watcherBefore = WebAppFactoryArg.Notifications.CountFor(watcher.DbRef);

		await GodParser.CommandParse(mover.Handle, ConnectionService, MarkupText.Plain("home"));

		await Assert.That(WebAppFactoryArg.Notifications.For(watcher.DbRef).Skip(watcherBefore)
			.Any(m => m.Contains("goes home."))).IsFalse();
		await Assert.That(await LocationOf(mover.DbRef.ToString())).IsEqualTo(BareDbref(home));
	}
}
