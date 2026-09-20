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
