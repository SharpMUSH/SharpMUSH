using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// <c>GET</c> matches, refuses and moves the way PennMUSH's <c>do_get</c> does
/// (<c>src/move.c:563-705</c>). Every taker is a mortal standing in a God-owned room, so neither the
/// taker's nor the room's privileges can mask a lookup or permission error.
/// </summary>
/// <remarks>
/// <c>[NotInParallel]</c> for the same reason <see cref="ObjectTriadParityTests"/> carries it: the
/// action attributes are queued, and <c>DrainImmediateQueueForTests</c> waits on the session-shared
/// immediate queue.
/// </remarks>
[NotInParallel]
public class GetCommandParityTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private Mediator.IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<Mediator.IMediator>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private ITaskScheduler Scheduler => WebAppFactoryArg.Services.GetRequiredService<ITaskScheduler>();
	private IMUSHCodeParser GodParser => WebAppFactoryArg.CommandParser;

	private async Task God(string command)
		=> await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain(command));

	private async Task<DBRef> Thing(string prefix)
		=> await TestIsolationHelpers.CreateTestThingAsync(GodParser, ConnectionService, prefix);

	private async Task<TestIsolationHelpers.TestPlayer> Mortal(string prefix)
		=> await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, prefix);

	private async Task<string> Eval(string expression)
		=> (await GodParser.FunctionParse(MarkupText.Plain(expression)))!.Message!.ToPlainText().Trim();

	private async Task<string> NameOf(DBRef obj) => await Eval($"[name({obj})]");

	private async Task<string> LocationOf(DBRef obj) => Bare(await Eval($"[loc({obj})]"));

	private static string Bare(string dbref)
	{
		var colon = dbref.IndexOf(':');
		return colon < 0 ? dbref : dbref[..colon];
	}

	/// <summary>Digs a fresh God-owned room and silently gathers every named object into it.</summary>
	private async Task<string> Room(string prefix, params object[] occupants)
	{
		var dig = await GodParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@dig {TestIsolationHelpers.GenerateUniqueName(prefix)}"));
		var room = dig.Message!.ToPlainText().Trim();

		foreach (var occupant in occupants)
		{
			await God($"@teleport/silent {occupant}={room}");
		}

		return room;
	}

	private async Task<List<string>> MessagesWhile(DBRef who, Func<Task> action)
	{
		var recorder = WebAppFactoryArg.Notifications;
		var before = recorder.CountFor(who);
		await action();
		return [.. recorder.For(who).Skip(before)];
	}

	private async Task<List<string>> Get(TestIsolationHelpers.TestPlayer taker, string argument)
		=> await MessagesWhile(taker.DbRef, async () =>
			await GodParser.CommandParse(taker.Handle, ConnectionService, MarkupText.Plain($"get {argument}")));

	/// <summary>Marks the triads a successful GET fires, so their absence can be asserted.</summary>
	private async Task WatchTriads(DBRef item, TestIsolationHelpers.TestPlayer taker)
	{
		await God($"&ASUCCESS {item}=&TAKEN me=yes");
		await God($"&ARECEIVE {taker.DbRef}=&RECEIVED me=yes");
	}

	private async Task AssertNoTriads(DBRef item, TestIsolationHelpers.TestPlayer taker)
	{
		await Scheduler.DrainImmediateQueueForTests();
		await Assert.That(await Eval($"[get({item}/TAKEN)]")).IsEmpty()
			.Because("a refused GET fires no SUCCESS triad");
		await Assert.That(await Eval($"[get({taker.DbRef}/RECEIVED)]")).IsEmpty()
			.Because("a refused GET fires no RECEIVE triad");
	}

	// --- Lookup identity and scope (move.c:566-577) ---------------------------------------------

	/// <summary>
	/// Ordinary GET matches <c>MAT_NEIGHBOR | MAT_CHECK_KEYS | MAT_NEAR | MAT_ENGLISH</c> and adds
	/// <c>MAT_ABSOLUTE</c> only for Long_Fingers (<c>move.c:566, 575-576</c>), so a mortal cannot
	/// reach into another room by dbref.
	/// </summary>
	[Test]
	public async ValueTask MortalCannotGetARemoteObjectByDbref()
	{
		var taker = await Mortal("GetRemoteTaker");
		var item = await Thing("GetRemoteItem");
		await Room("GetRemoteHere", taker.DbRef);
		var elsewhere = await Room("GetRemoteThere", item);
		await WatchTriads(item, taker);

		var seen = await Get(taker, item.ToString());

		await Assert.That(await LocationOf(item)).IsEqualTo(Bare(elsewhere))
			.Because("an object in another room is not near a mortal without Long_Fingers");
		await Assert.That(seen).Contains("I don't see that here.");
		await AssertNoTriads(item, taker);
	}

	/// <summary>Long_Fingers adds <c>MAT_ABSOLUTE</c> and passes <c>MAT_NEAR</c> (<c>move.c:575-576</c>).</summary>
	[Test]
	public async ValueTask LongFingersMortalGetsARemoteObjectByDbref()
	{
		var taker = await Mortal("GetFarTaker");
		var item = await Thing("GetFarItem");
		await Room("GetFarHere", taker.DbRef);
		await Room("GetFarThere", item);
		await God($"@power {taker.DbRef}=Long_Fingers");

		await Get(taker, item.ToString());

		await Assert.That(await LocationOf(item)).IsEqualTo($"#{taker.DbRef.Number}");
	}

	/// <summary>A nearby object is taken, and the item hears "took you." after the move (<c>move.c:676-677</c>).</summary>
	[Test]
	public async ValueTask NearbyGetMovesThenTellsTheItem()
	{
		var taker = await Mortal("GetNearTaker");
		var item = await Thing("GetNearItem");
		await Room("GetNearRoom", taker.DbRef, item);

		var heard = await MessagesWhile(item, async () =>
			await GodParser.CommandParse(taker.Handle, ConnectionService, MarkupText.Plain($"get {await NameOf(item)}")));

		await Assert.That(await LocationOf(item)).IsEqualTo($"#{taker.DbRef.Number}");
		await Assert.That(heard.LastOrDefault()).IsEqualTo($"{taker.Name} took you.")
			.Because("plain GET calls moveto first — whose automatic look the item sees — and notifies after");
		await Assert.That(heard.Count).IsGreaterThan(1)
			.Because("moveto is enter_room, which gives the moved item its automatic look");
	}

	// --- Possessive fallback (move.c:578-642) ---------------------------------------------------

	/// <summary>
	/// The whole argument is tried as an ordinary name first; the possessive path runs only when that
	/// finds nothing (<c>move.c:578</c>), so an item literally named <c>Bob's hat</c> wins.
	/// </summary>
	[Test]
	public async ValueTask ALiteralPossessiveNameBeatsTheContainersContents()
	{
		var taker = await Mortal("GetLiteralTaker");
		var box = await Thing("GetLitBox");
		var hat = await Thing("GetLitHat");
		var (boxName, hatName) = (await NameOf(box), await NameOf(hat));
		var literal = DBRef.Parse((await GodParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@create {boxName}'s {hatName}"))).Message!.ToPlainText().Trim());
		var room = await Room("GetLiteralRoom", taker.DbRef, box, literal);
		await God($"@set {box}=ENTER_OK");
		await God($"@teleport/silent {hat}={box}");

		await Get(taker, $"{boxName}'s {hatName}");

		await Assert.That(await LocationOf(literal)).IsEqualTo($"#{taker.DbRef.Number}")
			.Because("the ordinary match of the full argument comes first");
		await Assert.That(await LocationOf(hat)).IsEqualTo($"#{box.Number}");
		await Assert.That(await LocationOf(box)).IsEqualTo(Bare(room));
	}

	/// <summary>The possessive path still takes from a nearby ENTER_OK container when nothing else matches.</summary>
	[Test]
	public async ValueTask PossessiveGetTakesFromANearbyContainer()
	{
		var taker = await Mortal("GetPossTaker");
		var box = await Thing("GetPossBox");
		var hat = await Thing("GetPossHat");
		await Room("GetPossRoom", taker.DbRef, box);
		await God($"@set {box}=ENTER_OK");
		await God($"@teleport/silent {hat}={box}");

		await Get(taker, $"{await NameOf(box)}'s {await NameOf(hat)}");

		await Assert.That(await LocationOf(hat)).IsEqualTo($"#{taker.DbRef.Number}");
	}

	/// <summary><c>possessive_get</c> off: no possessive fallback at all (<c>move.c:579</c>, <c>:643-645</c>).</summary>
	[Test]
	public async ValueTask PossessiveGetOffDisablesThePossessivePath()
	{
		var taker = await Mortal("GetNoPossTaker");
		var box = await Thing("GetNoPossBox");
		var hat = await Thing("GetNoPossHat");
		await Room("GetNoPossRoom", taker.DbRef, box);
		await God($"@set {box}=ENTER_OK");
		await God($"@teleport/silent {hat}={box}");
		var argument = $"{await NameOf(box)}'s {await NameOf(hat)}";

		List<string> seen;
		using (TestOptionsOverride.Scope(options => options with
		{
			Command = options.Command with { PossessiveGet = false }
		}))
		{
			seen = await Get(taker, argument);
		}

		await Assert.That(await LocationOf(hat)).IsEqualTo($"#{box.Number}");
		await Assert.That(seen).Contains("I don't see that here.");
	}

	/// <summary>
	/// <c>possessive_get_d</c> is <c>POSSGET_ON_DISCONNECTED</c> (<c>conf.h:491</c>): off, nothing is
	/// taken from a disconnected player (<c>move.c:617</c>).
	/// </summary>
	[Test]
	public async ValueTask PossessiveGetFromADisconnectedPlayerNeedsPossessiveGetD()
	{
		var taker = await Mortal("GetDiscTaker");
		var holder = await TestIsolationHelpers.CreateTestPlayerAsync(WebAppFactoryArg.Services, Mediator, "GetDiscHolder");
		var hat = await Thing("GetDiscHat");
		await Room("GetDiscRoom", taker.DbRef, holder);
		await God($"@set {holder}=ENTER_OK");
		await God($"@teleport/silent {hat}={holder}");
		await WatchTriads(hat, taker);
		var argument = $"{await NameOf(holder)}'s {await NameOf(hat)}";

		List<string> seen;
		using (TestOptionsOverride.Scope(options => options with
		{
			Command = options.Command with { PossessiveGetD = false }
		}))
		{
			seen = await Get(taker, argument);
		}

		await Assert.That(await LocationOf(hat)).IsEqualTo($"#{holder.Number}");
		await Assert.That(seen).Contains("You can't take that from there.");
		await AssertNoTriads(hat, taker);

		using (TestOptionsOverride.Scope(options => options with
		{
			Command = options.Command with { PossessiveGetD = true }
		}))
		{
			await Get(taker, argument);
		}

		await Assert.That(await LocationOf(hat)).IsEqualTo($"#{taker.DbRef.Number}")
			.Because("possessive_get_d permits taking from a disconnected player");
	}

	/// <summary>A connected player can be robbed whatever <c>possessive_get_d</c> says (<c>move.c:617</c>).</summary>
	[Test]
	public async ValueTask PossessiveGetFromAConnectedPlayerIgnoresPossessiveGetD()
	{
		var taker = await Mortal("GetConnTaker");
		var holder = await Mortal("GetConnHolder");
		var hat = await Thing("GetConnHat");
		await Room("GetConnRoom", taker.DbRef, holder.DbRef);
		await God($"@set {holder.DbRef}=ENTER_OK");
		await God($"@teleport/silent {hat}={holder.DbRef}");

		using (TestOptionsOverride.Scope(options => options with
		{
			Command = options.Command with { PossessiveGetD = false }
		}))
		{
			await Get(taker, $"{holder.Name}'s {await NameOf(hat)}");
		}

		await Assert.That(await LocationOf(hat)).IsEqualTo($"#{taker.DbRef.Number}");
	}

	// --- Refusals (move.c:571-574, 651-665) -----------------------------------------------------

	/// <summary>
	/// Inside a thing that is neither ENTER_OK nor controlled, GET refuses before matching anything
	/// (<c>move.c:571-574</c>).
	/// </summary>
	[Test]
	public async ValueTask GetInsideALockedUpThingIsDenied()
	{
		var taker = await Mortal("GetEnclosedTaker");
		var cell = await Thing("GetEnclosedCell");
		var item = await Thing("GetEnclosedItem");
		await Room("GetEnclosedRoom", cell);
		await God($"@teleport/silent {taker.DbRef}={cell}");
		await God($"@teleport/silent {item}={cell}");
		await WatchTriads(item, taker);

		var seen = await Get(taker, await NameOf(item));

		await Assert.That(await LocationOf(item)).IsEqualTo($"#{cell.Number}");
		await Assert.That(seen).Contains("Permission denied.");
		await AssertNoTriads(item, taker);
	}

	/// <summary><c>thing == player</c>: "You cannot get yourself!" (<c>move.c:664-666</c>).</summary>
	[Test]
	public async ValueTask GettingYourselfIsRefused()
	{
		var taker = await Mortal("GetSelfTaker");
		var room = await Room("GetSelfRoom", taker.DbRef);
		await God($"&ASUCCESS {taker.DbRef}=&TAKEN me=yes");

		var seen = await Get(taker, taker.Name);

		await Assert.That(seen).Contains("You cannot get yourself!");
		await Assert.That(await LocationOf(taker.DbRef)).IsEqualTo(Bare(room));
		await Scheduler.DrainImmediateQueueForTests();
		await Assert.That(await Eval($"[get({taker.DbRef}/TAKEN)]")).IsEmpty();
	}

	/// <summary><c>Location(player) == thing</c>: "It's all around you!" (<c>move.c:655-657</c>).</summary>
	[Test]
	public async ValueTask GettingTheThingYouAreInsideIsRefused()
	{
		var taker = await Mortal("GetAroundTaker");
		var cell = await Thing("GetAroundCell");
		var room = await Room("GetAroundRoom", cell);
		await God($"@set {cell}=ENTER_OK");
		await God($"@teleport/silent {taker.DbRef}={cell}");
		await God($"@power {taker.DbRef}=Long_Fingers");
		await WatchTriads(cell, taker);

		var seen = await Get(taker, cell.ToString());

		await Assert.That(seen).Contains("It's all around you!");
		await Assert.That(await LocationOf(cell)).IsEqualTo(Bare(room));
		await Assert.That(await LocationOf(taker.DbRef)).IsEqualTo($"#{cell.Number}");
		await AssertNoTriads(cell, taker);
	}

	/// <summary><c>recursive_member(player, thing, 0)</c>: "Bad destination." (<c>move.c:659-661</c>).</summary>
	[Test]
	public async ValueTask GettingSomethingThatHoldsYouIsRefused()
	{
		var taker = await Mortal("GetNestTaker");
		var outer = await Thing("GetNestOuter");
		var inner = await Thing("GetNestInner");
		var room = await Room("GetNestRoom", outer);
		await God($"@set {inner}=ENTER_OK");
		await God($"@teleport/silent {inner}={outer}");
		await God($"@teleport/silent {taker.DbRef}={inner}");
		await God($"@power {taker.DbRef}=Long_Fingers");
		await WatchTriads(outer, taker);

		var seen = await Get(taker, outer.ToString());

		await Assert.That(seen).Contains("Bad destination.");
		await Assert.That(await LocationOf(outer)).IsEqualTo(Bare(room));
		await AssertNoTriads(outer, taker);
	}

	/// <summary>
	/// A move EnterRoom declines reports its refusal and fires neither SUCCESS nor RECEIVE: here a
	/// possessive GET of the thing the taker stands inside, which would carry itself.
	/// </summary>
	[Test]
	public async ValueTask ARefusedMoveReportsNoSuccess()
	{
		var taker = await Mortal("GetLoopTaker");
		var outer = await Thing("GetLoopOuter");
		var inner = await Thing("GetLoopInner");
		await Room("GetLoopRoom", outer);
		await God($"@set {outer}=ENTER_OK");
		await God($"@set {inner}=ENTER_OK");
		await God($"@teleport/silent {inner}={outer}");
		await God($"@teleport/silent {taker.DbRef}={inner}");
		// Controlling the outer container is what lets a taker nested two deep search it at all.
		await God($"@chown {outer}={taker.DbRef}");
		await WatchTriads(inner, taker);

		var (outerHeard, innerHeard) = (WebAppFactoryArg.Notifications.CountFor(outer),
			WebAppFactoryArg.Notifications.CountFor(inner));

		var seen = await Get(taker, $"{outer}'s {await NameOf(inner)}");

		await Assert.That(WebAppFactoryArg.Notifications.For(outer).Skip(outerHeard))
			.DoesNotContain($"{await NameOf(inner)} was taken from you.")
			.Because("nothing was taken, so the container is not told it was");
		await Assert.That(WebAppFactoryArg.Notifications.For(inner).Skip(innerHeard))
			.DoesNotContain($"{taker.Name} took you.")
			.Because("the item did not move, so it is not told it was taken");

		await Assert.That(seen).Contains(ErrorMessages.Notifications.CantGoThatWayContainmentLoop)
			.Because("EnterRoom's refusal reaches the taker");
		await Assert.That(await LocationOf(inner)).IsEqualTo($"#{outer.Number}")
			.Because("moving the taker's own container into the taker is a containment loop");
		await Assert.That(await LocationOf(taker.DbRef)).IsEqualTo($"#{inner.Number}");
		await AssertNoTriads(inner, taker);
	}
}
