using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// The irrevocable half of destruction: <c>@destroy</c> on an already-GOING object, and <c>@purge</c>.
///
/// PennMUSH reference (<c>src/destroy.c</c>):
///   do_destroy()  — "If thing has already been marked for destruction, go ahead and destroy
///                    immediately": free_object(); notify "Destroyed."
///   purge()       — GOING &amp;&amp; !GOING_TWICE → set GOING_TWICE; GOING &amp;&amp; GOING_TWICE → free_object()
///   free_object() — contents sent home, held exits destroyed, exits leading here relinked to their
///                    own source, every dangling reference to the dbref unset.
///
/// Test-config invariants (mushcnf.dst): probate_judge = 1, default_home = 0.
/// </summary>
[NotInParallel]
public class ObjectDestructionTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private ISharpDatabase Database => WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();

	// probate_judge = 1 in mushcnf.dst → God (#1) is the probate player.
	private const int ProbateJudgeDbRefNumber = 1;

	private async Task<DBRef> CreateThingAsync(string prefix)
	{
		var name = TestIsolationHelpers.GenerateUniqueName(prefix);
		var result = await Parser.CommandParse(1, ConnectionService, MModule.single($"@create {name}"));
		return DBRef.Parse(result.Message!.ToPlainText().Trim());
	}

	private async Task<DBRef> DigRoomAsync(string prefix)
	{
		var name = TestIsolationHelpers.GenerateUniqueName(prefix);
		var result = await Parser.CommandParse(1, ConnectionService, MModule.single($"@dig {name}"));
		return DBRef.Parse(result.Message!.ToPlainText().Trim());
	}

	private ValueTask<CallState> RunAsync(string command) =>
		Parser.CommandParse(1, ConnectionService, MModule.single(command));

	/// <summary>
	/// The reported bug: two <c>@destroy</c>es said "Destroyed." and left the object in the database
	/// forever, flagged GOING GOING_TWICE and visible to <c>@find</c> and <c>examine</c>.
	/// </summary>
	[Test]
	public async Task Destroy_Twice_RemovesTheObjectFromTheDatabase()
	{
		var thing = await CreateThingAsync("DestroyTwice");

		await RunAsync($"@destroy {thing}");

		var afterFirst = await Mediator.Send(new GetObjectNodeQuery(thing));
		await Assert.That(afterFirst.IsNone).IsFalse();
		await Assert.That(await afterFirst.Known.HasFlag("GOING")).IsTrue();

		await RunAsync($"@destroy {thing}");

		var afterSecond = await Mediator.Send(new GetObjectNodeQuery(thing));
		await Assert.That(afterSecond.IsNone).IsTrue();
	}

	/// <summary>
	/// A destroyed object must not survive in the raw store either — <c>@find</c> reads the object
	/// table directly, which is how the stuck object stayed visible.
	/// </summary>
	[Test]
	public async Task Destroy_Twice_RemovesTheObjectFromTheObjectTable()
	{
		var thing = await CreateThingAsync("DestroyTable");

		await RunAsync($"@destroy {thing}");
		await RunAsync($"@destroy {thing}");

		var raw = await Database.GetBaseObjectNodeAsync(thing);
		await Assert.That(raw).IsNull();
	}

	[Test]
	public async Task Destroy_Twice_TakesItsAttributesWithIt()
	{
		var thing = await CreateThingAsync("DestroyAttrs");

		await RunAsync($"&TESTATTR {thing}=some value");
		await RunAsync($"&TESTATTR`LEAF {thing}=a leaf value");

		var before = await Database.GetAttributeAsync(thing, ["TESTATTR"]).ToListAsync();
		await Assert.That(before).IsNotEmpty();

		await RunAsync($"@destroy {thing}");
		await RunAsync($"@destroy {thing}");

		var after = await Database.GetAttributeAsync(thing, ["TESTATTR"]).ToListAsync();
		await Assert.That(after).IsEmpty();
	}

	/// <summary>PennMUSH <c>empty_contents()</c>: contents go home rather than vanishing.</summary>
	[Test]
	public async Task Destroy_Container_SendsItsContentsHome()
	{
		var container = await CreateThingAsync("DestroyContainer");
		var occupant = await CreateThingAsync("DestroyOccupant");

		var home = await DigRoomAsync("DestroyOccupantHome");
		await RunAsync($"@link {occupant}={home}");
		await RunAsync($"@tel {occupant}={container}");

		var beforeLocation = (await Mediator.Send(new GetObjectNodeQuery(occupant))).Known.AsContent;
		await Assert.That((await beforeLocation.Location()).Object().DBRef.Number).IsEqualTo(container.Number);

		await RunAsync($"@destroy {container}");
		await RunAsync($"@destroy {container}");

		var survivor = await Mediator.Send(new GetObjectNodeQuery(occupant));
		await Assert.That(survivor.IsNone).IsFalse();

		var location = await survivor.Known.AsContent.Location();
		await Assert.That(location.Object().DBRef.Number).IsEqualTo(home.Number);
	}

	/// <summary>
	/// PennMUSH <c>clear_room()</c>: a room takes its exits with it. In SharpMUSH an exit is content
	/// of its source room, so this is <c>empty_contents()</c>'s "if holding exits, destroy it" branch.
	/// </summary>
	[Test]
	public async Task Destroy_Room_DestroysTheExitsItSources()
	{
		var room = await DigRoomAsync("DestroySourceRoom");
		var elsewhere = await DigRoomAsync("DestroyExitTarget");

		await RunAsync($"@tel {elsewhere}");
		var exitName = TestIsolationHelpers.GenerateUniqueName("DoomedExit");
		var openResult = await RunAsync($"@open {exitName}={room}");
		var exit = DBRef.Parse(openResult.Message!.ToPlainText().Trim());

		// Move the exit into the room that is about to die, so the room is its source.
		await RunAsync($"@tel {room}");
		var relocated = await RunAsync($"@open {TestIsolationHelpers.GenerateUniqueName("RoomExit")}={elsewhere}");
		var roomExit = DBRef.Parse(relocated.Message!.ToPlainText().Trim());
		await RunAsync($"@tel {elsewhere}");

		await RunAsync($"@destroy {room}");
		await RunAsync($"@destroy {room}");

		await Assert.That((await Mediator.Send(new GetObjectNodeQuery(room))).IsNone).IsTrue();
		await Assert.That((await Mediator.Send(new GetObjectNodeQuery(roomExit))).IsNone).IsTrue();

		// The exit that merely *led* to the room survives; see the relink test below.
		await Assert.That((await Mediator.Send(new GetObjectNodeQuery(exit))).IsNone).IsFalse();
	}

	/// <summary>
	/// PennMUSH <c>free_object()</c>: "If our destination is destroyed, then we relink to the source
	/// room (so that the exit can't be stolen)."
	/// </summary>
	[Test]
	public async Task Destroy_Room_RelinksTheExitsThatLedThere()
	{
		var doomed = await DigRoomAsync("DestroyEntranceTarget");
		var source = await DigRoomAsync("DestroyEntranceSource");

		await RunAsync($"@tel {source}");
		var openResult = await RunAsync($"@open {TestIsolationHelpers.GenerateUniqueName("Entrance")}={doomed}");
		var entrance = DBRef.Parse(openResult.Message!.ToPlainText().Trim());
		await RunAsync("@tel #0");

		await RunAsync($"@destroy {doomed}");
		await RunAsync($"@destroy {doomed}");

		var survivor = await Mediator.Send(new GetObjectNodeQuery(entrance));
		await Assert.That(survivor.IsNone).IsFalse();

		var destination = await survivor.AsExit.Home.WithCancellation(CancellationToken.None);
		await Assert.That(destination.IsNone).IsFalse();
		await Assert.That(destination.WithoutNone().Object().DBRef.Number).IsEqualTo(source.Number);
	}

	/// <summary>
	/// PennMUSH <c>free_object()</c>: <c>Home(i) = DEFAULT_HOME</c>. Without this the home edge is
	/// severed and every later read of the dependent throws.
	/// </summary>
	[Test]
	public async Task Destroy_Home_RehomesWhateverLivedThere()
	{
		var home = await DigRoomAsync("DestroyHomeRoom");
		var resident = await CreateThingAsync("DestroyHomeResident");

		await RunAsync($"@link {resident}={home}");
		await RunAsync($"@tel {resident}=#0");

		await RunAsync($"@destroy {home}");
		await RunAsync($"@destroy {home}");

		var survivor = await Mediator.Send(new GetObjectNodeQuery(resident));
		await Assert.That(survivor.IsNone).IsFalse();

		// Resolving Home at all is the assertion: a missing home edge throws.
		var newHome = await survivor.Known.AsContent.Home();
		await Assert.That(newHome.IsNone).IsFalse();
		await Assert.That(newHome.WithoutNone().Object().DBRef.Number).IsNotEqualTo(home.Number);
	}

	/// <summary>
	/// PennMUSH <c>free_object()</c> queues <c>OBJECT`DESTROY</c> with everything about the object it
	/// can still name, "since the event will deal with an object that doesn't exist anymore".
	/// sharpevents.md already documented the argument list — objid, origname, type, owner, parent,
	/// zone, enactor always #-1 — while nothing ever fired it.
	/// </summary>
	[Test]
	public async Task Destroy_Twice_FiresTheObjectDestroyEvent()
	{
		// event_handler = 9 (the seeded Event Handler) in the test config.
		const int EventHandlerDbRefNumber = 9;
		var eventHandler = new DBRef(EventHandlerDbRefNumber);

		var thing = await CreateThingAsync("DestroyEvent");
		var thingName = (await Mediator.Send(new GetObjectNodeQuery(thing))).Known.Object().Name;

		try
		{
			await RunAsync($"&OBJECT`DESTROY #{EventHandlerDbRefNumber}="
				+ $"&DESTROYLOG #{EventHandlerDbRefNumber}=%0|%1|%2|%3");

			await RunAsync($"@destroy {thing}");
			await RunAsync($"@destroy {thing}");

			var logged = await Database.GetAttributeAsync(eventHandler, ["DESTROYLOG"]).ToListAsync();
			await Assert.That(logged).IsNotEmpty()
				.Because("the OBJECT`DESTROY handler should have run and written DESTROYLOG");

			var value = MModule.plainText(logged[^1].Value);
			var fields = value.Split('|');

			await Assert.That(fields.Length).IsEqualTo(4);
			await Assert.That(fields[0]).StartsWith($"#{thing.Number}");
			await Assert.That(fields[1]).IsEqualTo(thingName);
			await Assert.That(fields[2]).IsEqualTo("THING");
			await Assert.That(fields[3]).StartsWith("#1");
		}
		finally
		{
			await RunAsync($"@wipe #{EventHandlerDbRefNumber}/OBJECT`DESTROY");
			await RunAsync($"@wipe #{EventHandlerDbRefNumber}/DESTROYLOG");
		}
	}

	/// <summary>
	/// PennMUSH <c>special_object()</c>. Destroying #0 would take the whole grid with it, so it is
	/// refused at the marking stage and again at the freeing stage.
	/// </summary>
	[Test]
	public async Task Destroy_SpecialObject_IsRefused()
	{
		await RunAsync("@destroy #0");

		var roomZero = await Mediator.Send(new GetObjectNodeQuery(new DBRef(0)));
		await Assert.That(roomZero.IsNone).IsFalse();
		await Assert.That(await roomZero.Known.HasFlag("GOING")).IsFalse();
	}

	/// <summary>
	/// PennMUSH <c>clear_player()</c>: nothing may be left owned by a player who no longer exists, or
	/// every later read of it throws on the severed ownership edge. Possessions marked GOING at
	/// pre-destroy time are deliberately still owned by the doomed player at this point, so the
	/// hand-off to the probate judge has to happen at free time as well.
	/// </summary>
	[Test]
	public async Task Nuke_Twice_RemovesThePlayerAndLeavesNothingOwnedByThem()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerAsync(
			WebAppFactoryArg.Services, Mediator, "NukeTwice");

		var possession = await CreateThingAsync("NukeTwicePossession");
		await RunAsync($"@chown {possession}={player}");

		await RunAsync($"@nuke {player}");
		await RunAsync($"@nuke {player}");

		await Assert.That((await Mediator.Send(new GetObjectNodeQuery(player))).IsNone).IsTrue();

		var survivor = await Mediator.Send(new GetObjectNodeQuery(possession));
		await Assert.That(survivor.IsNone).IsFalse();

		// Resolving Owner at all is the assertion: a severed ownership edge throws.
		var owner = await survivor.Known.Object().Owner.WithCancellation(CancellationToken.None);
		await Assert.That(owner.Object.DBRef.Number).IsEqualTo(ProbateJudgeDbRefNumber);
	}

	/// <summary>
	/// PennMUSH <c>purge()</c> is deliberately two-pass: everything dies on the *second* purge after
	/// <c>@destroy</c>, which is what leaves room for <c>@undestroy</c>.
	/// </summary>
	[Test]
	public async Task Purge_TakesTwoPasses_AdvancingThenFreeing()
	{
		var thing = await CreateThingAsync("PurgeTwoPass");

		await RunAsync($"@destroy {thing}");

		await RunAsync("@purge");

		var afterFirstPurge = await Mediator.Send(new GetObjectNodeQuery(thing));
		await Assert.That(afterFirstPurge.IsNone).IsFalse();
		await Assert.That(await afterFirstPurge.Known.HasFlag("GOING_TWICE")).IsTrue();

		await RunAsync("@purge");

		var afterSecondPurge = await Mediator.Send(new GetObjectNodeQuery(thing));
		await Assert.That(afterSecondPurge.IsNone).IsTrue();
	}

	/// <summary>An object that was never <c>@destroy</c>ed is untouched by a purge.</summary>
	[Test]
	public async Task Purge_LeavesObjectsThatWereNeverDestroyedAlone()
	{
		var bystander = await CreateThingAsync("PurgeBystander");

		await RunAsync("@purge");
		await RunAsync("@purge");

		var survivor = await Mediator.Send(new GetObjectNodeQuery(bystander));
		await Assert.That(survivor.IsNone).IsFalse();
		await Assert.That(await survivor.Known.HasFlag("GOING_TWICE")).IsFalse();
	}

	/// <summary>
	/// <c>@undestroy</c> between the two purge passes has to actually save the object — the whole
	/// reason PennMUSH spreads destruction over two passes.
	/// </summary>
	[Test]
	public async Task Purge_AfterUndestroy_SparesTheObject()
	{
		var thing = await CreateThingAsync("PurgeUndestroy");

		await RunAsync($"@destroy {thing}");
		await RunAsync("@purge");
		await RunAsync($"@undestroy {thing}");
		await RunAsync("@purge");
		await RunAsync("@purge");

		var survivor = await Mediator.Send(new GetObjectNodeQuery(thing));
		await Assert.That(survivor.IsNone).IsFalse();
	}
}
