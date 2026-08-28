using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// <c>fun_nearby</c> (src/fundb.c:896), which SharpMUSH answered with neither of its two halves: it
/// compared <c>Room()</c> instead of <c>nearby()</c>, and it had no permission gate at all (#795).
///
/// <para><c>Room()</c> walks the containment chain to the enclosing room; <c>nearby()</c> compares
/// <em>immediate</em> locations plus the two carrying cases. The difference is visible the moment
/// anything is nested — a coin in a bag is not near the player standing beside the bag — and it is
/// only visible then, which is why the container case below is the load-bearing one.</para>
/// </summary>
public class NearbyFunctionTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser GodParser => WebAppFactoryArg.CommandParser;

	private async Task<string> EvalAs(DBRef executor, string expr)
		=> (await WebAppFactoryArg.FunctionParserFor(executor).FunctionParse(MModule.single(expr)))
			?.Message!.ToPlainText() ?? "<null>";

	private async Task<string> God(string command)
		=> (await GodParser.CommandParse(1, ConnectionService, MModule.single(command)))?.Message?.ToPlainText() ?? "";

	[Test]
	[NotInParallel]
	public async Task NestedContentsAreNotNearbyTheirRoomsOccupants()
	{
		// Tavern
		//  ├── bag
		//  │    └── coin      where_is(coin) == bag
		//  └── bystander      where_is(bystander) == Tavern
		var tavern = DBRef.Parse((await God("@dig NearbyTavern")).Trim().Split(' ')[^1].Trim());
		var bag = DBRef.Parse((await God("@create NearbyBag")).Trim());
		var coin = DBRef.Parse((await God("@create NearbyCoin")).Trim());
		var bystander = DBRef.Parse((await God("@create NearbyBystander")).Trim());

		await God($"@tel #{bag.Number}=#{tavern.Number}");
		await God($"@tel #{bystander.Number}=#{tavern.Number}");
		await God($"@tel #{coin.Number}=#{bag.Number}");

		// The nesting is the whole point of the case, so fail loudly rather than silently testing
		// two objects that both sit in the room.
		// loc() answers #<number>:<creation-ms>, so compare on the dbref number.
		await Assert.That((await EvalAs(new DBRef(1), $"loc(#{coin.Number})")).Split(':')[0])
			.IsEqualTo($"#{bag.Number}")
			.Because("the coin has to be inside the bag for this to distinguish nearby() from Room()");
		await Assert.That((await EvalAs(new DBRef(1), $"loc(#{bag.Number})")).Split(':')[0])
			.IsEqualTo($"#{tavern.Number}");

		// Room() says the same room for both. nearby() compares where_is: the bag, versus the Tavern.
		await Assert.That(await EvalAs(new DBRef(1), $"nearby(#{coin.Number},#{bystander.Number})"))
			.IsEqualTo("0");

		// The bag itself is in the room, so it is near the bystander — the control that shows the 0
		// above is the nesting talking and not a broken lookup.
		await Assert.That(await EvalAs(new DBRef(1), $"nearby(#{bag.Number},#{bystander.Number})"))
			.IsEqualTo("1");
	}

	[Test]
	[NotInParallel]
	public async Task ACarriedObjectIsNearbyItsCarrier()
	{
		// nearby()'s second and third arms: where_is(a) == b, or where_is(b) == a.
		var bag = DBRef.Parse((await God("@create NearbyCarryBag")).Trim());
		var coin = DBRef.Parse((await God("@create NearbyCarryCoin")).Trim());
		await God($"@tel #{coin.Number}=#{bag.Number}");

		await Assert.That(await EvalAs(new DBRef(1), $"nearby(#{coin.Number},#{bag.Number})")).IsEqualTo("1");
		await Assert.That(await EvalAs(new DBRef(1), $"nearby(#{bag.Number},#{coin.Number})")).IsEqualTo("1");
	}

	[Test]
	[NotInParallel]
	public async Task AnExitIsNearbyWhatStandsInItsSourceRoom()
	{
		// where_is(exit) is Home(exit), and Home/Source/Exits are the same field in PennMUSH
		// (dbdefs.h:35-40) — so an exit's where_is is the room it sits in, not where it leads.
		// @open builds the exit in the executor's own location, so the exit's source is God's room and
		// the witness goes there too.
		var there = DBRef.Parse((await God("@dig NearbyExitDest")).Trim().Split(' ')[^1].Trim());
		var godRoom = (await EvalAs(new DBRef(1), "loc(%#)")).Split(':')[0];
		var standing = DBRef.Parse((await God("@create NearbyExitWitness")).Trim());
		await God($"@tel #{standing.Number}={godRoom}");
		await God($"@open NearbyDoor=#{there.Number}");

		await Assert.That(await EvalAs(new DBRef(1), $"nearby(NearbyDoor,#{standing.Number})"))
			.IsEqualTo("1")
			.Because("where_is(exit) is the room it sits in, which is where the witness is standing");
	}

	[Test]
	[NotInParallel]
	public async Task AMortalWithNoStandingGetsTheControlRefusal()
	{
		// Penn refuses unless the executor controls one side, is See_All, or is near one of them:
		// "#-1 NO OBJECTS CONTROLLED". Without the gate, nearby() answers for any two objects the
		// caller can name, which leaks where things are.
		var faraway = DBRef.Parse((await God("@dig NearbyGateRoom")).Trim().Split(' ')[^1].Trim());
		var a = DBRef.Parse((await God("@create NearbyGateA")).Trim());
		var b = DBRef.Parse((await God("@create NearbyGateB")).Trim());
		await God($"@tel #{a.Number}=#{faraway.Number}");
		await God($"@tel #{b.Number}=#{faraway.Number}");

		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "NearbyGate");

		var mortalSees = await EvalAs(mortal.DbRef, $"nearby(#{a.Number},#{b.Number})");

		// God is See_All, so the same question clears the gate and gets a real answer. Without this
		// control, a refusal that came from a failed match would read the same as the gate firing.
		var godSees = await EvalAs(new DBRef(1), $"nearby(#{a.Number},#{b.Number})");

		await Assert.That(mortalSees).IsEqualTo("#-1 NO OBJECTS CONTROLLED");
		await Assert.That(godSees).IsEqualTo("1");
	}

	[Test]
	[NotInParallel]
	public async Task AnExecutorWithStandingStillAnswersForItsOwnSurroundings()
	{
		// The gate passes on nearby(executor, obj) as well as on control, so an ordinary player can
		// still ask about what is in the room with it.
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "NearbyNeighbour");
		var mortalLoc = (await EvalAs(mortal.DbRef, "loc(%#)")).Split(':')[0];

		var neighbour = DBRef.Parse((await God("@create NearbyNeighbourThing")).Trim());
		await God($"@tel #{neighbour.Number}={mortalLoc}");

		await Assert.That(await EvalAs(mortal.DbRef, $"nearby(%#,#{neighbour.Number})")).IsEqualTo("1");
	}

	[Test]
	[NotInParallel]
	public async Task AnUnresolvableArgumentAnswersBareMinusOne()
	{
		// fun_nearby gates before it tests GoodObject, and then writes a bare "#-1" with no reason —
		// the reason already went to the executor, because match_thing is noisy_match_result.
		// %# clears the gate (God controls itself), so what is left is the bad second argument.
		await Assert.That(await EvalAs(new DBRef(1), "nearby(%#,NoSuchThingExistsHere)")).IsEqualTo("#-1");
	}
}
