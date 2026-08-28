using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// PennMUSH's <c>dbwalk</c> (src/fundb.c:687) applies a permission gate and a visibility filter that
/// the 27 SharpMUSH functions built on it applied neither of (issue #833). Any object an executor
/// could name gave up its complete contents, DARK ones included.
///
/// <para>The gate is old and deliberate. Penn's own comment above <c>dbwalk</c> records why: "mortals
/// could get the contents of rooms they didn't control, thus (if they were willing to go through the
/// trouble) they could build a scanner to locate anything they wanted."</para>
///
/// <para>Every refusal below is paired with a control — the same call by someone entitled to an
/// answer — because a bare <c>#-1</c> from a gate and a <c>#-1</c> from a search that was never going
/// to match read identically.</para>
/// </summary>
public class DbWalkPermissionTests
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

	private async Task<DBRef> Dig(string name)
		=> DBRef.Parse((await God($"@dig {name}")).Trim().Split(' ')[^1].Trim());

	private async Task<DBRef> Create(string name) => DBRef.Parse((await God($"@create {name}")).Trim());

	/// <summary>Mortal, its own room, and something in that room — the shared setup for the filter cases.</summary>
	private async Task<(TestIsolationHelpers.TestPlayer Mortal, string Room)> MortalInARoom(string label)
	{
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, label);
		var room = (await EvalAs(mortal.DbRef, "loc(%#)")).Split(':')[0];
		return (mortal, room);
	}

	[Test]
	[NotInParallel]
	public async Task AMortalCannotListTheContentsOfARoomItIsNotIn()
	{
		var faraway = await Dig("WalkGateRoom");
		var thing = await Create("WalkGateThing");
		await God($"@tel #{thing.Number}=#{faraway.Number}");

		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WalkGate");

		// The mortal can neither examine the room nor is standing in it, and is not the enactor of it.
		await Assert.That(await EvalAs(mortal.DbRef, $"lcon(#{faraway.Number})")).IsEqualTo("#-1");

		// God is a wizard, so Can_Examine passes and the same call answers.
		await Assert.That(await EvalAs(new DBRef(1), $"lcon(#{faraway.Number})"))
			.Contains($"#{thing.Number}");
	}

	/// <summary>
	/// The drift PennMUSH's 2001 unification was fixing, and which had reappeared here: "next() and
	/// con() and exit() returning different results/having different permissions than lcon() and
	/// lexits()". All of them are one walk now, so all of them refuse together.
	/// </summary>
	[Test]
	[NotInParallel]
	public async Task ConExitAndNextRefuseWhereLconRefuses()
	{
		var faraway = await Dig("WalkFamilyRoom");
		var thing = await Create("WalkFamilyThing");
		await God($"@tel #{thing.Number}=#{faraway.Number}");

		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WalkFamily");

		await Assert.That(await EvalAs(mortal.DbRef, $"con(#{faraway.Number})")).IsEqualTo("#-1");
		await Assert.That(await EvalAs(mortal.DbRef, $"exit(#{faraway.Number})")).IsEqualTo("#-1");
		await Assert.That(await EvalAs(mortal.DbRef, $"ncon(#{faraway.Number})")).IsEqualTo("#-1");
		await Assert.That(await EvalAs(mortal.DbRef, $"xcon(#{faraway.Number},1,5)")).IsEqualTo("#-1");
		await Assert.That(await EvalAs(mortal.DbRef, $"lvcon(#{faraway.Number})")).IsEqualTo("#-1");

		// next() gates on the *location* of its argument, which is the same room.
		await Assert.That(await EvalAs(mortal.DbRef, $"next(#{thing.Number})")).IsEqualTo("#-1");

		// The controls: God clears the gate for every one of them.
		// con() renders DBRef.ToString(), which is the objid form #N:<creation-ms>, as every form here
		// already did. Compare on the dbref number.
		await Assert.That((await EvalAs(new DBRef(1), $"con(#{faraway.Number})")).Split(':')[0])
			.IsEqualTo($"#{thing.Number}");
		await Assert.That(await EvalAs(new DBRef(1), $"ncon(#{faraway.Number})")).IsEqualTo("1");
	}

	[Test]
	[NotInParallel]
	public async Task StandingInTheRoomIsEnoughToListIt()
	{
		var (mortal, room) = await MortalInARoom("WalkStanding");
		var thing = await Create("WalkStandingThing");
		await God($"@tel #{thing.Number}={room}");

		// Location(executor) == loc, the gate's second arm — no examine rights needed.
		await Assert.That(await EvalAs(mortal.DbRef, $"lcon({room})")).Contains($"#{thing.Number}");
	}

	/// <summary>
	/// <c>first_visible</c> (src/predicat.c:292) hides a <c>DarkLegal</c> object from a looker who is
	/// not See_All, is not the location, and controls neither the location nor the object. This is the
	/// filter the plain <c>lcon()</c> was missing — note it is <em>not</em> the <c>lv*</c> forms'
	/// <c>skipdark</c>, which is a separate, additional pass.
	/// </summary>
	[Test]
	[NotInParallel]
	public async Task ADarkObjectIsHiddenFromAMortalStandingInTheRoom()
	{
		var (mortal, room) = await MortalInARoom("WalkDark");
		var dark = await Create("WalkDarkHidden");
		var plain = await Create("WalkDarkVisible");
		await God($"@tel #{dark.Number}={room}");
		await God($"@tel #{plain.Number}={room}");
		await God($"@set #{dark.Number}=DARK");

		var listed = await EvalAs(mortal.DbRef, $"lcon({room})");

		await Assert.That(listed)
			.Contains($"#{plain.Number}")
			.Because("the mortal must see an ordinary object here for the omission below to mean anything");
		await Assert.That(listed).DoesNotContain($"#{dark.Number}");

		// God is See_All, so the dark object is still listed to someone entitled to see it.
		await Assert.That(await EvalAs(new DBRef(1), $"lcon({room})")).Contains($"#{dark.Number}");
	}

	/// <summary>
	/// PennMUSH documents a bug in <c>first_visible</c> and deliberately keeps it — "this is what
	/// causes DARK objects to show" — so parity means keeping it here: the owner of a DARK object sees
	/// it in its own contents listing.
	/// </summary>
	[Test]
	[NotInParallel]
	public async Task TheOwnerOfADarkObjectStillSeesItListed()
	{
		var (mortal, room) = await MortalInARoom("WalkOwnDark");
		var mine = await Create("WalkOwnDarkThing");
		await God($"@tel #{mine.Number}={room}");
		await God($"@set #{mine.Number}=DARK");
		await God($"@chown #{mine.Number}=#{mortal.DbRef.Number}");

		await Assert.That(await EvalAs(mortal.DbRef, $"lcon({room})")).Contains($"#{mine.Number}");
	}

	/// <summary>
	/// <c>lcon()</c>'s second argument selects a type or a listening filter
	/// (<c>fun_dbwalker</c>, src/fundb.c:779). It was declared and then ignored, so every keyword
	/// listed everything.
	/// </summary>
	[Test]
	[NotInParallel]
	public async Task LconsSecondArgumentSelectsAType()
	{
		var (mortal, room) = await MortalInARoom("WalkKeyword");
		var thing = await Create("WalkKeywordThing");
		await God($"@tel #{thing.Number}={room}");

		// string_prefixe(<keyword>, <given>) asks whether the argument is a prefix OF the keyword, so
		// the singular forms the help documents are the ones that work — "players" is not a prefix of
		// "player" and is rejected, which the last assertion below pins.
		var players = await EvalAs(mortal.DbRef, $"lcon({room},player)");
		var things = await EvalAs(mortal.DbRef, $"lcon({room},thing)");

		await Assert.That(players)
			.Contains($"#{mortal.DbRef.Number}")
			.Because("the mortal is standing in the room and is a player");
		await Assert.That(players).DoesNotContain($"#{thing.Number}");

		await Assert.That(things).Contains($"#{thing.Number}");
		await Assert.That(things).DoesNotContain($"#{mortal.DbRef.Number}");

		// Penn matches these with string_prefixe, so an abbreviation is the same keyword.
		await Assert.That(await EvalAs(mortal.DbRef, $"lcon({room},pl)")).IsEqualTo(players);

		// Anything that is not a prefix of a keyword is #-1 — including the plural of one.
		await Assert.That(await EvalAs(mortal.DbRef, $"lcon({room},zzz)")).IsEqualTo("#-1");
		await Assert.That(await EvalAs(mortal.DbRef, $"lcon({room},players)")).IsEqualTo("#-1");
	}

	/// <summary>
	/// The <c>n*</c> forms report <c>dbwalk</c>'s <c>retcount</c> — every match — while the <c>x*</c>
	/// forms return a window of the same walk. Both now count the same filtered list, so they agree.
	/// </summary>
	[Test]
	[NotInParallel]
	public async Task CountAndWindowAgreeWithTheList()
	{
		var room = await Dig("WalkCountRoom");
		var names = new List<DBRef>();
		for (var i = 0; i < 3; i++)
		{
			var t = await Create($"WalkCountThing{i}");
			await God($"@tel #{t.Number}=#{room.Number}");
			names.Add(t);
		}

		await Assert.That(await EvalAs(new DBRef(1), $"nthings(#{room.Number})")).IsEqualTo("3");

		var listed = (await EvalAs(new DBRef(1), $"lthings(#{room.Number})")).Split(' ');
		var window = (await EvalAs(new DBRef(1), $"xthings(#{room.Number},2,2)")).Split(' ');

		await Assert.That(listed.Length).IsEqualTo(3);
		await Assert.That(window).IsEquivalentTo(listed.Skip(1).Take(2).ToArray());
	}

	[Test]
	[NotInParallel]
	public async Task TheWindowFormsRejectAStartOrCountBelowOne()
	{
		var room = await Dig("WalkRangeRoom");

		await Assert.That(await EvalAs(new DBRef(1), $"xcon(#{room.Number},0,2)"))
			.IsEqualTo("#-1 ARGUMENT OUT OF RANGE");
		await Assert.That(await EvalAs(new DBRef(1), $"xcon(#{room.Number},1,0)"))
			.IsEqualTo("#-1 ARGUMENT OUT OF RANGE");
		await Assert.That(await EvalAs(new DBRef(1), $"xcon(#{room.Number},a,2)"))
			.IsEqualTo("#-1 ARGUMENT MUST BE INTEGER");
	}
}
