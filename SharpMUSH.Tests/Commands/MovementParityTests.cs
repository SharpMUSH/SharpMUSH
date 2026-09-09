using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
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
	private IMUSHCodeParser GodParser => WebAppFactoryArg.CommandParser;

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
}
