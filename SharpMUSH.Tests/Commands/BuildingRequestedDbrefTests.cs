using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// <c>@create &lt;name&gt;=&lt;cost&gt;,&lt;dbref&gt;</c> and <c>create(name,cost,dbref)</c> both reach
/// PennMUSH's <c>make_first_free_wrapper</c> (<c>src/destroy.c:928-949</c>) before anything is built:
/// the <c>Pick_Dbref</c> power or wizardry, then a strict <c>#nnn</c> naming a slot that is actually
/// free. SharpMUSH declared the argument and never read it, so a build that asked for a recycled slot
/// silently landed on whatever <c>next_dbref</c> handed out next.
/// <para>SharpMUSH keeps no garbage rows, so "on the free list" reads here as "a hole": a dbref below
/// the allocation counter that holds no object. A refusal creates nothing at all.</para>
/// </summary>
public class BuildingRequestedDbrefTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	private async Task<string> Run(long handle, string command)
		=> (await Parser.CommandParse(handle, ConnectionService, MarkupText.Plain(command)))?.Message?.ToPlainText()
			?? string.Empty;

	/// <summary>Creates a thing as God and frees its dbref again — <c>@destroy</c> twice is Penn's immediate free.</summary>
	private async Task<DBRef> Hole(string uid)
	{
		var doomed = DBRef.Parse(await Run(1, $"@create BrdHole{uid}"));
		await Run(1, $"@destroy {doomed}");
		await Run(1, $"@destroy {doomed}");

		await Assert.That(await Mediator.Send(new GetObjectNodeQuery(new DBRef(doomed.Number))) is None).IsTrue()
			.Because("the hole has to be a hole for the rest of the test to mean anything");

		return doomed;
	}

	/// <summary>How many objects currently answer to <paramref name="name"/>, as God sees them.</summary>
	private async Task<string[]> Named(string name)
		=> (await Run(1, $"think lsearch(all,name,{name})")).Split(' ', StringSplitOptions.RemoveEmptyEntries);

	/// <summary>The dbref numbers of everything answering to <paramref name="name"/> — lsearch reports objids.</summary>
	private async Task<int[]> NumbersNamed(string name)
		=> [.. (await Named(name)).Select(found => DBRef.Parse(found).Number)];

	/// <summary>A build that happened: a real dbref, and not one of the <c>#-1</c> refusals.</summary>
	private static bool Built(string reported) => reported.StartsWith('#') && !reported.StartsWith("#-");

	/// <summary>The request a caller makes, phrased for the command and for the function.</summary>
	private Task<string> Build(long handle, bool throughTheFunction, string name, string dbref)
		=> throughTheFunction
			? Run(handle, $"think create({name},,{dbref})")
			: Run(handle, $"@create {name}=,{dbref}");

	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async ValueTask AWizardBuildsIntoTheHoleItAsksFor(bool throughTheFunction)
	{
		var uid = Guid.NewGuid().ToString("N")[..8];
		var hole = await Hole(uid);

		var name = $"BrdTaken{uid}";
		var built = DBRef.Parse(await Build(1, throughTheFunction, name, $"#{hole.Number}"));

		await Assert.That(built.Number).IsEqualTo(hole.Number)
			.Because("destroy.c:945 moves the requested slot to the head of the free list, so new_object() returns it");
		await Assert.That((await Mediator.Send(new GetObjectNodeQuery(built))).Expect<AnySharpObject>().Object().Name)
			.IsEqualTo(name);
	}

	/// <summary>
	/// <c>!IsGarbage(thing)</c> at destroy.c:939. Penn refuses and <c>do_create</c> returns NOTHING; it
	/// never builds somewhere else instead, which is the whole point of asking for a dbref.
	/// </summary>
	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async ValueTask AnOccupiedDbrefIsRefusedAndBuildsNothing(bool throughTheFunction)
	{
		var uid = Guid.NewGuid().ToString("N")[..8];
		var occupant = DBRef.Parse(await Run(1, $"@create BrdOccupant{uid}"));

		var name = $"BrdOccupied{uid}";
		await Assert.That(await Build(1, throughTheFunction, name, $"#{occupant.Number}"))
			.IsEqualTo(ErrorMessages.Returns.InvalidDbref);
		await Assert.That((await Named(name)).Length).IsEqualTo(0)
			.Because("a refused request must not quietly allocate a different object");
	}

	/// <summary>
	/// <c>GoodObject</c> at destroy.c:939 bounds the request by <c>db_top</c>; here the equivalent bound
	/// is the allocation counter, so a dbref that has never been handed out is refused rather than
	/// jumping the counter to reach it.
	/// </summary>
	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async ValueTask ADbrefBeyondTheCounterIsRefusedAndBuildsNothing(bool throughTheFunction)
	{
		var uid = Guid.NewGuid().ToString("N")[..8];
		var name = $"BrdBeyond{uid}";

		await Assert.That(await Build(1, throughTheFunction, name, "#999999"))
			.IsEqualTo(ErrorMessages.Returns.InvalidDbref);
		await Assert.That((await Named(name)).Length).IsEqualTo(0);
	}

	/// <summary>
	/// <c>parse_dbref</c> (<c>src/parse.c:120-138</c>) insists on <c>#nnn</c> and returns NOTHING for
	/// anything else, including a bare number.
	/// </summary>
	[Test]
	[Arguments(true, "42")]
	[Arguments(false, "42")]
	[Arguments(true, "#notanumber")]
	[Arguments(false, "#notanumber")]
	public async ValueTask AMalformedDbrefIsRefusedAndBuildsNothing(bool throughTheFunction, string malformed)
	{
		var uid = Guid.NewGuid().ToString("N")[..8];
		var name = $"BrdMalformed{uid}";

		await Assert.That(await Build(1, throughTheFunction, name, malformed))
			.IsEqualTo(ErrorMessages.Returns.InvalidDbref);
		await Assert.That((await Named(name)).Length).IsEqualTo(0);
	}

	/// <summary>
	/// destroy.c:935 — <c>Wizard(player) || has_power_by_name(player, "Pick_Dbref")</c>. A mortal with
	/// neither is refused before the slot is even looked at, and the slot stays free for someone who may
	/// have it.
	/// </summary>
	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async ValueTask AMortalWithoutThePowerIsRefusedAndTheHoleSurvives(bool throughTheFunction)
	{
		var uid = Guid.NewGuid().ToString("N")[..8];
		var hole = await Hole(uid);
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "BrdMortal");

		var refused = $"BrdRefused{uid}";
		await Assert.That(await Build(mortal.Handle, throughTheFunction, refused, $"#{hole.Number}"))
			.IsEqualTo(ErrorMessages.Returns.PermissionDenied);
		await Assert.That((await Named(refused)).Length).IsEqualTo(0)
			.Because("Penn refuses before new_object(), so a mortal's request builds nothing at all");

		var allowed = $"BrdAllowed{uid}";
		await Assert.That(DBRef.Parse(await Build(1, throughTheFunction, allowed, $"#{hole.Number}")).Number)
			.IsEqualTo(hole.Number)
			.Because("the refusal must not have consumed the slot");
	}

	/// <summary>The other half of destroy.c:935: the power alone is enough, without wizardry.</summary>
	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async ValueTask AMortalHoldingPickDbrefsMayBuildIntoAHole(bool throughTheFunction)
	{
		var uid = Guid.NewGuid().ToString("N")[..8];
		var hole = await Hole(uid);
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "BrdPicker");
		await Run(1, $"@power {mortal.DbRef}=Pick_DBRefs");

		var name = $"BrdPicked{uid}";
		await Assert.That(DBRef.Parse(await Build(mortal.Handle, throughTheFunction, name, $"#{hole.Number}")).Number)
			.IsEqualTo(hole.Number);
	}

	/// <summary>
	/// The right side of <c>@create</c> is <c>CMD_T_RS_ARGS</c> in Penn (<c>src/command.c:124</c>).
	/// Without the split the cost and the dbref arrived as one argument and neither could be read.
	/// </summary>
	[Test]
	public async ValueTask TheCommandsRightSideSplitsCostFromDbref()
	{
		var uid = Guid.NewGuid().ToString("N")[..8];
		var hole = await Hole(uid);

		var built = DBRef.Parse(await Run(1, $"@create BrdSplit{uid}=10,#{hole.Number}"));

		await Assert.That(built.Number).IsEqualTo(hole.Number);
	}

	/// <summary>
	/// <c>do_dig</c> pushes <c>argv[5]</c>, <c>argv[4]</c>, <c>argv[3]</c> onto the free list before
	/// <c>new_object()</c> (<c>create.c:480-490</c>), so the room takes the fourth argument, the exit to
	/// it the fifth and the exit back the sixth. <c>fun_dig</c> hands the same <c>args</c> straight to
	/// <c>do_dig</c> (<c>fundb.c:2177-2189</c>).
	/// </summary>
	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async ValueTask ADigTakesAllThreeDbrefsItAsksFor(bool throughTheFunction)
	{
		var uid = Guid.NewGuid().ToString("N")[..8];
		var roomHole = await Hole($"{uid}R");
		var toHole = await Hole($"{uid}T");
		var fromHole = await Hole($"{uid}F");

		var dug = DBRef.Parse(await Run(1, throughTheFunction
			? $"think dig(BrdDigRoom{uid},BrdDigTo{uid},BrdDigFrom{uid},#{roomHole.Number},#{toHole.Number},#{fromHole.Number})"
			: $"@dig BrdDigRoom{uid}=BrdDigTo{uid},BrdDigFrom{uid},#{roomHole.Number},#{toHole.Number},#{fromHole.Number}"));

		await Assert.That(dug.Number).IsEqualTo(roomHole.Number)
			.Because("argv[3] is pushed last and so comes off the free list first");
		await Assert.That(await NumbersNamed($"BrdDigTo{uid}")).IsEquivalentTo([toHole.Number]);
		await Assert.That(await NumbersNamed($"BrdDigFrom{uid}")).IsEquivalentTo([fromHole.Number]);
	}

	/// <summary>
	/// A push that cannot be honoured returns before <c>new_object()</c> (<c>create.c:483-489</c>), so a
	/// bad exit dbref leaves no room behind either.
	/// </summary>
	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async ValueTask ADigRefusedOverOneDbrefDigsNothingAtAll(bool throughTheFunction)
	{
		var uid = Guid.NewGuid().ToString("N")[..8];
		var roomHole = await Hole($"{uid}R");
		var occupant = DBRef.Parse(await Run(1, $"@create BrdDigOccupant{uid}"));

		await Assert.That(await Run(1, throughTheFunction
				? $"think dig(BrdDigBadRoom{uid},BrdDigBadTo{uid},,#{roomHole.Number},notadbref)"
				: $"@dig BrdDigBadRoom{uid}=BrdDigBadTo{uid},,#{roomHole.Number},notadbref"))
			.IsEqualTo(ErrorMessages.Returns.InvalidDbref);
		await Assert.That((await Named($"BrdDigBadRoom{uid}")).Length).IsEqualTo(0);
		await Assert.That((await Named($"BrdDigBadTo{uid}")).Length).IsEqualTo(0);

		// The hole the dig asked for is still a hole, and so is usable afterwards.
		await Assert.That(DBRef.Parse(await Run(1, $"@dig BrdDigLater{uid}=,,#{roomHole.Number}")).Number)
			.IsEqualTo(roomHole.Number);
		await Assert.That((await Named($"BrdDigOccupant{uid}")).Length).IsEqualTo(1)
			.Because("nothing in this test should have touched the occupant");
		await Assert.That(occupant.Number).IsNotEqualTo(roomHole.Number);
	}

	/// <summary>
	/// <c>do_open</c>'s <c>links[4]</c> and <c>links[5]</c> (<c>create.c:219-226</c>) — the forward exit
	/// and the exit back. <c>fun_open</c> has one, <c>args[3]</c> (<c>fundb.c:2144-2173</c>).
	/// </summary>
	[Test]
	public async ValueTask AnOpenTakesTheDbrefsItAsksFor()
	{
		var uid = Guid.NewGuid().ToString("N")[..8];
		var forwardHole = await Hole($"{uid}A");
		var backHole = await Hole($"{uid}B");
		var destination = DBRef.Parse(await Run(1, $"@dig BrdOpenDest{uid}"));

		var forward = DBRef.Parse(await Run(1,
			$"@open BrdOpenTo{uid}={destination},BrdOpenFrom{uid},,#{forwardHole.Number},#{backHole.Number}"));

		await Assert.That(forward.Number).IsEqualTo(forwardHole.Number);
		await Assert.That(await NumbersNamed($"BrdOpenFrom{uid}")).IsEquivalentTo([backHole.Number]);

		var functionHole = await Hole($"{uid}C");
		await Assert.That(DBRef.Parse(await Run(1,
				$"think open(BrdOpenFn{uid},,,#{functionHole.Number})")).Number)
			.IsEqualTo(functionHole.Number)
			.Because("fun_open's fourth argument is the requested dbref");
	}

	/// <summary>
	/// <c>cmd_clone</c> passes <c>args_right[2]</c> as <c>newdbref</c> (<c>src/cmds.c:382-386</c>) and
	/// <c>fun_clone</c> passes <c>args[2]</c> (<c>fundb.c:2192-2212</c>). <c>@clone</c> declared the slot
	/// as "cost", which it never was, and read neither.
	/// </summary>
	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async ValueTask ACloneTakesTheDbrefItAsksFor(bool throughTheFunction)
	{
		var uid = Guid.NewGuid().ToString("N")[..8];
		var hole = await Hole(uid);
		var original = DBRef.Parse(await Run(1, $"@create BrdCloneSource{uid}"));

		var name = $"BrdCloned{uid}";
		var clone = DBRef.Parse(await Run(1, throughTheFunction
			? $"think clone({original},{name},#{hole.Number})"
			: $"@clone {original}={name},#{hole.Number}"));

		await Assert.That(clone.Number).IsEqualTo(hole.Number);
		await Assert.That((await Mediator.Send(new GetObjectNodeQuery(clone))).Expect<AnySharpObject>().Object().Name)
			.IsEqualTo(name);
	}

	/// <summary>
	/// <c>make_first_free_wrapper</c> asks <c>IsGarbage</c> and pushes the slot onto the free list
	/// <i>before</i> <c>new_object()</c> (<c>destroy.c:939-947</c>, <c>create.c:480-490</c>), so every
	/// id a dig names is settled before the room exists. Checking only permission and syntax up front
	/// dug the room and then failed on the exit.
	/// </summary>
	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async ValueTask ADigWithAnOccupiedExitDbrefDigsNothingAtAll(bool throughTheFunction)
	{
		var uid = Guid.NewGuid().ToString("N")[..8];
		var roomHole = await Hole($"{uid}R");
		var occupant = DBRef.Parse(await Run(1, $"@create BrdDigTaken{uid}"));

		await Assert.That(await Run(1, throughTheFunction
				? $"think dig(BrdDigTakenRoom{uid},BrdDigTakenTo{uid},,#{roomHole.Number},#{occupant.Number})"
				: $"@dig BrdDigTakenRoom{uid}=BrdDigTakenTo{uid},,#{roomHole.Number},#{occupant.Number}"))
			.IsEqualTo(ErrorMessages.Returns.InvalidDbref);
		await Assert.That((await Named($"BrdDigTakenRoom{uid}")).Length).IsEqualTo(0)
			.Because("the room dbref was free, but the exit's was not, and Penn pushes both before it digs");
		await Assert.That((await Named($"BrdDigTakenTo{uid}")).Length).IsEqualTo(0);

		// The room's hole was never consumed by the refusal.
		await Assert.That(DBRef.Parse(await Run(1, $"@dig BrdDigTakenLater{uid}=,,#{roomHole.Number}")).Number)
			.IsEqualTo(roomHole.Number);
	}

	/// <summary>
	/// A dig refused over its room dbref says so. The lifted <c>@dig</c> body reported every refusal
	/// from the room's creation as an exhausted quota, which is wrong when the quota admitted the
	/// attempt and only the requested slot was turned down.
	/// </summary>
	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async ValueTask ADigRefusedOverItsRoomDbrefSaysSoAndNotTheQuota(bool throughTheFunction)
	{
		var uid = Guid.NewGuid().ToString("N")[..8];
		var occupant = DBRef.Parse(await Run(1, $"@create BrdDigRoomTaken{uid}"));

		await Assert.That(await Run(1, throughTheFunction
				? $"think dig(BrdDigRoomTakenNew{uid},,,#{occupant.Number})"
				: $"@dig BrdDigRoomTakenNew{uid}=,,#{occupant.Number}"))
			.IsEqualTo(ErrorMessages.Returns.InvalidDbref);
		await Assert.That((await Named($"BrdDigRoomTakenNew{uid}")).Length).IsEqualTo(0);
	}

	/// <summary>
	/// Penn's free list silently tolerates the same slot pushed twice and hands the second object
	/// whatever came next (<c>make_first_free</c>, <c>destroy.c</c>, returns 1 when the object is
	/// already at the head). Building somewhere other than where you asked is what a requested dbref
	/// exists to prevent, so a repeat is refused outright here.
	/// </summary>
	[Test]
	public async ValueTask ADigNamingOneDbrefTwiceIsRefused()
	{
		var uid = Guid.NewGuid().ToString("N")[..8];
		var hole = await Hole(uid);

		await Assert.That(await Run(1,
				$"@dig BrdDigTwiceRoom{uid}=BrdDigTwiceTo{uid},,#{hole.Number},#{hole.Number}"))
			.IsEqualTo(ErrorMessages.Returns.InvalidDbref);
		await Assert.That((await Named($"BrdDigTwiceRoom{uid}")).Length).IsEqualTo(0);
		await Assert.That((await Named($"BrdDigTwiceTo{uid}")).Length).IsEqualTo(0);
	}

	/// <summary>
	/// A build refused for the dbref says so, and is not flattened into the quota's answer. Cloning
	/// mapped every refusal it was handed onto <c>#-1 BUILDING QUOTA EXHAUSTED</c>, which is the wrong
	/// thing to tell a wizard whose slot was simply taken.
	/// </summary>
	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async ValueTask ACloneRefusedOverItsDbrefSaysSoAndNotTheQuota(bool throughTheFunction)
	{
		var uid = Guid.NewGuid().ToString("N")[..8];
		var original = DBRef.Parse(await Run(1, $"@create BrdCloneBadSource{uid}"));
		var occupant = DBRef.Parse(await Run(1, $"@create BrdCloneOccupant{uid}"));

		var name = $"BrdCloneRefused{uid}";
		await Assert.That(await Run(1, throughTheFunction
				? $"think clone({original},{name},#{occupant.Number})"
				: $"@clone {original}={name},#{occupant.Number}"))
			.IsEqualTo(ErrorMessages.Returns.InvalidDbref);
		await Assert.That((await Named(name)).Length).IsEqualTo(0);
	}

	/// <summary>
	/// One hole admits one object however many callers ask for it at once: the check and the write share
	/// a transaction inside the provider (<c>AllocateDbrefAt</c>), so the losers are told the id was not
	/// free rather than all being handed the same one.
	/// </summary>
	[Test]
	public async ValueTask ConcurrentBuildsCannotBothTakeTheSameHole()
	{
		var uid = Guid.NewGuid().ToString("N")[..8];
		var hole = await Hole(uid);

		var results = await Task.WhenAll(
			Enumerable.Range(0, 6).Select(i => Run(1, $"@create BrdRace{uid}_{i}=,#{hole.Number}")));

		await Assert.That(results.Count(r => Built(r) && DBRef.Parse(r).Number == hole.Number)).IsEqualTo(1)
			.Because("exactly one caller may take the slot");
		await Assert.That(results.Count(r => r == ErrorMessages.Returns.InvalidDbref)).IsEqualTo(5);
	}

	/// <summary>
	/// Two multi-object digs whose requested slots cross: each wants the other's room dbref for its
	/// exit. The availability check and the whole build it admits are taken together, so one dig gets
	/// both of its objects and the other builds nothing — rather than both committing a room and then
	/// failing on an exit, which is what an availability check that reserved nothing allowed.
	/// <para>Only a build that names its dbrefs can take a hole at all: the counter never hands one
	/// back, so nothing else contends for these slots.</para>
	/// </summary>
	[Test]
	public async ValueTask ConcurrentMultiObjectBuildsDoNotHalfCommit()
	{
		var uid = Guid.NewGuid().ToString("N")[..8];
		var first = await Hole($"{uid}A");
		var second = await Hole($"{uid}B");

		var results = await Task.WhenAll(
			Run(1, $"@dig BrdCrossRoomA{uid}=BrdCrossToA{uid},,#{first.Number},#{second.Number}"),
			Run(1, $"@dig BrdCrossRoomB{uid}=BrdCrossToB{uid},,#{second.Number},#{first.Number}"));

		await Assert.That(results.Count(Built)).IsEqualTo(1)
			.Because("one of the two digs owns both slots for the whole of its build");
		await Assert.That(results.Count(r => r == ErrorMessages.Returns.InvalidDbref)).IsEqualTo(1);

		var rooms = (await Named($"BrdCrossRoomA{uid}")).Length + (await Named($"BrdCrossRoomB{uid}")).Length;
		var exits = (await Named($"BrdCrossToA{uid}")).Length + (await Named($"BrdCrossToB{uid}")).Length;
		await Assert.That(rooms).IsEqualTo(1);
		await Assert.That(exits).IsEqualTo(1)
			.Because("the dig that was admitted finished; the one that was refused left nothing behind");
	}

	/// <summary>
	/// <c>do_create</c> calls <c>make_first_free_wrapper</c> — which asks <c>IsGarbage</c> as well as
	/// the power — before <c>can_pay_fees</c> (<c>create.c:561-565</c>). A builder who is both out of
	/// quota and asking for a taken slot is told which one they asked for, not which one they ran out
	/// of. The single-object paths skipped the availability half entirely, so the quota answered first.
	/// </summary>
	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async ValueTask AnOccupiedDbrefIsReportedAheadOfAnExhaustedQuota(bool throughTheFunction)
	{
		var uid = Guid.NewGuid().ToString("N")[..8];
		var occupant = DBRef.Parse(await Run(1, $"@create BrdOrderOccupant{uid}"));

		// A wizard, so the Pick_DBRefs half passes, but with no quota left to spend.
		var wizard = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "BrdOrder");
		await Run(1, $"@power {wizard.DbRef}=Pick_DBRefs");
		var player = (await Mediator.Send(new GetObjectNodeQuery(wizard.DbRef))).Expect<SharpPlayer>();
		await Run(1, $"@quota/set {wizard.DbRef}={await Mediator.Send(new GetOwnedObjectCountQuery(player))}");

		var name = $"BrdOrder{uid}";
		await Assert.That(await Build(wizard.Handle, throughTheFunction, name, $"#{occupant.Number}"))
			.IsEqualTo(ErrorMessages.Returns.InvalidDbref)
			.Because("create.c:561 settles the slot before :565 asks can_pay_fees");
		await Assert.That((await Named(name)).Length).IsEqualTo(0);
	}

	/// <summary>Asking for nothing is still the ordinary path: the counter hands out the next dbref.</summary>
	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async ValueTask AnAbsentDbrefArgumentStillAllocatesNormally(bool throughTheFunction)
	{
		var uid = Guid.NewGuid().ToString("N")[..8];
		var name = $"BrdPlain{uid}";

		var built = DBRef.Parse(throughTheFunction
			? await Run(1, $"think create({name})")
			: await Run(1, $"@create {name}"));

		await Assert.That((await Mediator.Send(new GetObjectNodeQuery(built))).Expect<AnySharpObject>().Object().Name)
			.IsEqualTo(name);
	}
}
