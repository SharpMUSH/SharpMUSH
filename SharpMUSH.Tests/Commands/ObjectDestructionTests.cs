using System.Reflection;
using System.Runtime.ExceptionServices;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
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
///
/// <para>
/// Caveat worth knowing before adding to this file: the <c>@purge</c> tests are globally
/// destructive by nature. PennMUSH's purge walks the whole database, so these free every
/// GOING_TWICE object in the shared session database, not just their own — including fixtures
/// another test created, destroyed and has not finished asserting on. The window is small and six
/// consecutive full-suite runs across both supported providers were clean, but a test that leaves an
/// object GOING and then reads it back is racing this.
/// </para>
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
		var result = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@create {name}"));
		return DBRef.Parse(result.Message!.ToPlainText().Trim());
	}

	private async Task<DBRef> DigRoomAsync(string prefix)
	{
		var name = TestIsolationHelpers.GenerateUniqueName(prefix);
		var result = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@dig {name}"));
		return DBRef.Parse(result.Message!.ToPlainText().Trim());
	}

	private ValueTask<CallState> RunAsync(string command) =>
		Parser.CommandParse(1, ConnectionService, MarkupText.Plain(command));

	/// <summary>
	/// The reported bug: two <c>@destroy</c>es said "Destroyed." and left the object in the database
	/// forever, flagged GOING GOING_TWICE and visible to <c>@find</c> and <c>examine</c>.
	/// </summary>
	[Test]
	public async Task Destroy_Twice_RemovesTheObjectFromTheDatabase()
	{
		var thing = await CreateThingAsync("DestroyTwice");

		await RunAsync($"@destroy {thing}");

		var afterFirst = (await Mediator.Send(new GetObjectNodeQuery(thing))).Expect<AnySharpObject>();
		await Assert.That(await afterFirst.HasFlag("GOING")).IsTrue();

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

		var beforeLocation = (await Mediator.Send(new GetObjectNodeQuery(occupant))).Expect<AnySharpObject>().AsContent;
		await Assert.That((await beforeLocation.Location()).Object().DBRef.Number).IsEqualTo(container.Number);

		await RunAsync($"@destroy {container}");
		await RunAsync($"@destroy {container}");

		var survivor = (await Mediator.Send(new GetObjectNodeQuery(occupant))).Expect<AnySharpObject>();

		var location = await survivor.AsContent.Location();
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

		var survivor = (await Mediator.Send(new GetObjectNodeQuery(entrance))).Expect<SharpExit>();

		var destination = (await survivor.Home.WithCancellation(CancellationToken.None)).Expect<AnySharpContainer>();
		await Assert.That(destination.Object().DBRef.Number).IsEqualTo(source.Number);
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

		var survivor = (await Mediator.Send(new GetObjectNodeQuery(resident))).Expect<AnySharpObject>();

		// Resolving Home at all is the assertion: a missing home edge throws.
		var newHome = (await survivor.AsContent.Home()).Expect<AnySharpContainer>();
		await Assert.That(newHome.Object().DBRef.Number).IsNotEqualTo(home.Number);
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
		var thingName = (await Mediator.Send(new GetObjectNodeQuery(thing))).Expect<AnySharpObject>().Object().Name;

		try
		{
			await RunAsync($"&OBJECT`DESTROY #{EventHandlerDbRefNumber}="
				+ $"&DESTROYLOG #{EventHandlerDbRefNumber}=%0|%1|%2|%3");

			await RunAsync($"@destroy {thing}");
			await RunAsync($"@destroy {thing}");

			var logged = await Database.GetAttributeAsync(eventHandler, ["DESTROYLOG"]).ToListAsync();
			await Assert.That(logged).IsNotEmpty()
				.Because("the OBJECT`DESTROY handler should have run and written DESTROYLOG");

			var value = logged[^1].Value.ToPlainText();
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

		var roomZero = (await Mediator.Send(new GetObjectNodeQuery(new DBRef(0)))).Expect<AnySharpObject>();
		await Assert.That(await roomZero.HasFlag("GOING")).IsFalse();
	}

	/// <summary>
	/// PennMUSH <c>clear_player()</c>: nothing may be left owned by a player who no longer exists, or
	/// every later read of it throws on the severed ownership edge. With destroy_possessions and
	/// really_safe on, a SAFE possession survives and goes to the probate judge, and the rest are freed.
	/// </summary>
	[Test]
	public async Task Nuke_Twice_RemovesThePlayerAndLeavesNothingOwnedByThem()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerAsync(
			WebAppFactoryArg.Services, Mediator, "NukeTwice");

		var possession = await CreateThingAsync("NukeTwicePossession");
		await RunAsync($"@set {possession}=SAFE");
		await RunAsync($"@chown {possession}={player}");
		var doomed = await CreateThingAsync("NukeTwiceDoomed");
		await RunAsync($"@chown {doomed}={player}");

		await RunAsync($"@nuke {player}");
		await RunAsync($"@nuke {player}");

		await Assert.That((await Mediator.Send(new GetObjectNodeQuery(player))).IsNone).IsTrue();
		await Assert.That((await Mediator.Send(new GetObjectNodeQuery(doomed))).IsNone).IsTrue();

		var survivor = (await Mediator.Send(new GetObjectNodeQuery(possession))).Expect<AnySharpObject>();

		// Resolving Owner at all is the assertion: a severed ownership edge throws.
		var owner = await survivor.Object().Owner.WithCancellation(CancellationToken.None);
		await Assert.That(owner.Object.DBRef.Number).IsEqualTo(ProbateJudgeDbRefNumber);
	}

	/// <summary>
	/// A doomed player's room takes its exits with it when the probate frees it, so an exit the same
	/// player owns is already gone by the time the probate reaches it. Acting on the stale copy failed
	/// its free and then handed the deleted dbref to the probate judge, writing an ownership edge for an
	/// object that no longer exists. A fresh probate judge makes that stray edge countable.
	/// </summary>
	[Test]
	public async Task Nuke_Twice_SkipsPossessionsAnEarlierFreeAlreadyTook()
	{
		var judge = await TestIsolationHelpers.CreateTestPlayerAsync(
			WebAppFactoryArg.Services, Mediator, "NukeCascadeJudge");
		using var probate = TestOptionsOverride.Scope(options => options with
		{
			Command = options.Command with { ProbateJudge = (uint)judge.Number }
		});

		var player = await TestIsolationHelpers.CreateTestPlayerAsync(
			WebAppFactoryArg.Services, Mediator, "NukeCascade");
		var room = await DigRoomAsync("NukeCascadeRoom");

		// The probate walks possessions in dbref order; the exit has to come after its room.
		await RunAsync($"@tel {room}");
		DBRef exit;
		do
		{
			var opened = await RunAsync($"@open {TestIsolationHelpers.GenerateUniqueName("NukeCascadeExit")}");
			exit = DBRef.Parse(opened.Message!.ToPlainText().Trim());
		} while (exit.Number < room.Number);
		await RunAsync("@tel #0");

		await RunAsync($"@chown {room}={player}");
		await RunAsync($"@chown {exit}={player}");

		await RunAsync($"@nuke {player}");
		await RunAsync($"@nuke {player}");

		await Assert.That((await Mediator.Send(new GetObjectNodeQuery(player))).IsNone).IsTrue();
		await Assert.That((await Mediator.Send(new GetObjectNodeQuery(room))).IsNone).IsTrue();
		await Assert.That((await Mediator.Send(new GetObjectNodeQuery(exit))).IsNone).IsTrue();

		var judgePlayer = (await Mediator.Send(new GetObjectNodeQuery(judge))).Expect<SharpPlayer>();
		await Assert.That(await Mediator.Send(new GetOwnedObjectCountQuery(judgePlayer))).IsEqualTo(1)
			.Because("the judge owns only itself; nothing survived to be handed over");
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

		var afterFirstPurge = (await Mediator.Send(new GetObjectNodeQuery(thing))).Expect<AnySharpObject>();
		await Assert.That(await afterFirstPurge.HasFlag("GOING_TWICE")).IsTrue();

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

		var survivor = (await Mediator.Send(new GetObjectNodeQuery(bystander))).Expect<AnySharpObject>();
		await Assert.That(await survivor.HasFlag("GOING_TWICE")).IsFalse();
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

	/// <summary>
	/// Builds the service over a substituted <see cref="IMoveService"/> so evacuation can be made to
	/// fail; everything else is the live session's wiring.
	/// </summary>
	private ObjectDestructionService DestructionServiceWith(IMoveService moves, IMediator? mediator = null)
		=> new(
			mediator ?? Mediator,
			WebAppFactoryArg.Services.GetRequiredService<INotifyService>(),
			moves,
			WebAppFactoryArg.Services.GetRequiredService<IEventService>(),
			WebAppFactoryArg.Services.GetRequiredService<IOptionsMonitor<SharpMUSHOptions>>(),
			NullLogger<ObjectDestructionService>.Instance);

	/// <summary>
	/// The live mediator, except that the listed dbrefs read as missing — the only way to stage a
	/// world where neither probate_judge nor God resolves.
	/// </summary>
	public class HidingMediator : DispatchProxy
	{
		public IMediator Inner { get; set; } = null!;
		public HashSet<int> Hidden { get; set; } = [];

		public static IMediator Over(IMediator inner, params int[] hidden)
		{
			var proxy = Create<IMediator, HidingMediator>();
			var hiding = (HidingMediator)(object)proxy;
			hiding.Inner = inner;
			hiding.Hidden = [.. hidden];
			return proxy;
		}

		protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
		{
			if (args is [GetObjectNodeQuery query, ..] && Hidden.Contains(query.DBRef.Number))
			{
				return ValueTask.FromResult(new AnyOptionalSharpObject(new None()));
			}

			try
			{
				return targetMethod!.Invoke(Inner, args);
			}
			catch (TargetInvocationException exception) when (exception.InnerException is not null)
			{
				ExceptionDispatchInfo.Throw(exception.InnerException);
				throw;
			}
		}
	}

	/// <summary>
	/// Freeing a player hands what they own to the probate judge, falling back to God. When neither
	/// resolves there is nobody to hand it to, and deleting the player anyway would sever every
	/// ownership edge pointing at them. The free is refused instead, leaving the player GOING for a
	/// later purge once the configuration is fixed.
	/// </summary>
	[Test]
	public async Task FreePlayer_WithNoProbatePlayer_LeavesThePlayerStanding()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerAsync(
			WebAppFactoryArg.Services, Mediator, "NoProbate");
		var target = (await Mediator.Send(new GetObjectNodeQuery(player))).Expect<AnySharpObject>();

		// probate_judge is #1 in the test config, so hiding #1 hides both the judge and the fallback.
		var service = DestructionServiceWith(
			WebAppFactoryArg.Services.GetRequiredService<IMoveService>(),
			HidingMediator.Over(Mediator, ProbateJudgeDbRefNumber));

		await Assert.That(await service.FreeObjectAsync(Parser, target)).IsFalse();
		await Assert.That((await Mediator.Send(new GetObjectNodeQuery(player))).IsNone).IsFalse();
	}

	private static IMoveService MoveServiceAnswering(
		Func<AnySharpContainer, Result<Success>> answer, List<int> destinations)
	{
		var moves = Substitute.For<IMoveService>();
		moves.EnterRoom(Arg.Any<IMUSHCodeParser>(), Arg.Any<AnySharpContent>(), Arg.Any<AnySharpContainer>(),
				Arg.Any<bool>(), Arg.Any<DBRef>(), Arg.Any<string>())
			.Returns(call =>
			{
				var destination = call.ArgAt<AnySharpContainer>(2);
				destinations.Add(destination.Object().DBRef.Number);
				return ValueTask.FromResult(answer(destination));
			});
		return moves;
	}

	/// <summary>
	/// DEVIATION from PennMUSH, which cannot hit this: <c>empty_contents</c>'s <c>moveto</c> is a
	/// pointer rewrite over an in-memory database and never fails. Here evacuating is a move that can
	/// be refused, and <c>DeleteObjectCommand</c> would then take the location edge of content still
	/// standing inside — every later read of that content reads a dangling location.
	/// </summary>
	[Test]
	public async Task FreeObject_WhoseContentsCannotBeEvacuated_LeavesTheContainerStanding()
	{
		var container = await CreateThingAsync("EvacFailContainer");
		var occupant = await CreateThingAsync("EvacFailOccupant");
		var home = await DigRoomAsync("EvacFailHome");

		await RunAsync($"@link {occupant}={home}");
		await RunAsync($"@tel {occupant}={container}");

		var attempted = new List<int>();
		var service = DestructionServiceWith(
			MoveServiceAnswering(_ => new Error<string>("Injected evacuation failure"), attempted));

		var target = (await Mediator.Send(new GetObjectNodeQuery(container))).Expect<AnySharpObject>();
		var freed = await service.FreeObjectAsync(Parser, target);

		await Assert.That(freed).IsFalse();
		await Assert.That(await Database.GetBaseObjectNodeAsync(container)).IsNotNull()
			.Because("deleting it would strand the occupant without a location");
		await Assert.That((await Mediator.Send(new GetObjectNodeQuery(occupant))).IsNone).IsFalse();

		// Both the home and the default_home retry were tried before it gave up.
		await Assert.That(attempted.Count).IsEqualTo(2);
	}

	/// <summary>
	/// Probate is the irreversible half of freeing a player, so a player whose contents cannot be
	/// evacuated keeps everything: the refusal leaves nothing handed over, and the retry that does
	/// succeed runs the whole probate once.
	/// </summary>
	[Test]
	public async Task FreePlayer_WhoseContentsCannotBeEvacuated_ProbatesNothing_UntilTheRetry()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerAsync(
			WebAppFactoryArg.Services, Mediator, "EvacFailPlayer");
		var playerNode = (await Mediator.Send(new GetObjectNodeQuery(player))).Expect<SharpPlayer>();
		var channelName = TestIsolationHelpers.GenerateUniqueName("EvacFailChan");
		await Mediator.Send(new SharpMUSH.Library.Commands.Database.CreateChannelCommand(
			MarkupText.Plain(channelName), ["Open"], playerNode));

		var kept = await CreateThingAsync("EvacFailKept");
		await RunAsync($"@set {kept}=SAFE");
		await RunAsync($"@chown {kept}={player}");

		var occupant = await CreateThingAsync("EvacFailPlayerOccupant");
		await RunAsync($"@link {occupant}={await DigRoomAsync("EvacFailPlayerHome")}");
		await RunAsync($"@tel {occupant}={player}");

		var failing = DestructionServiceWith(
			MoveServiceAnswering(_ => new Error<string>("Injected evacuation failure"), []));
		var target = (await Mediator.Send(new GetObjectNodeQuery(player))).Expect<AnySharpObject>();

		await Assert.That(await failing.FreeObjectAsync(Parser, target)).IsFalse();
		await Assert.That(await OwnerNumberAsync(kept)).IsEqualTo(player.Number);
		await Assert.That(await ChannelOwnerNumberAsync(channelName)).IsEqualTo(player.Number);

		var real = WebAppFactoryArg.Services.GetRequiredService<IObjectDestructionService>();
		target = (await Mediator.Send(new GetObjectNodeQuery(player))).Expect<AnySharpObject>();

		await Assert.That(await real.FreeObjectAsync(Parser, target)).IsTrue();
		await Assert.That(await OwnerNumberAsync(kept)).IsEqualTo(ProbateJudgeDbRefNumber);
		await Assert.That(await ChannelOwnerNumberAsync(channelName)).IsEqualTo(ProbateJudgeDbRefNumber);
	}

	private async Task<int> OwnerNumberAsync(DBRef target)
		=> (await (await Mediator.Send(new GetObjectNodeQuery(target))).Expect<AnySharpObject>()
			.Object().Owner.WithCancellation(CancellationToken.None)).Object.DBRef.Number;

	private async Task<int> ChannelOwnerNumberAsync(string channelName)
	{
		var channel = await Mediator.Send(new GetChannelQuery(channelName));
		await Assert.That(channel).IsNotNull();
		return (await channel!.Owner.WithCancellation(CancellationToken.None)).Object.DBRef.Number;
	}

	/// <summary>
	/// <c>empty_contents</c> already falls back to <c>default_home</c> when a content's own home is
	/// unusable (<c>src/destroy.c</c>); a home that refuses the move is the same situation one step
	/// later, so it takes the same fallback rather than aborting the destruction.
	/// </summary>
	[Test]
	public async Task FreeObject_WhoseContentsHomeRefusesThem_RetriesToDefaultHome()
	{
		var container = await CreateThingAsync("EvacRetryContainer");
		var occupant = await CreateThingAsync("EvacRetryOccupant");
		var home = await DigRoomAsync("EvacRetryHome");

		await RunAsync($"@link {occupant}={home}");
		await RunAsync($"@tel {occupant}={container}");

		var defaultHome = (int)WebAppFactoryArg.Services
			.GetRequiredService<IOptionsMonitor<SharpMUSHOptions>>().CurrentValue.Database.DefaultHome;

		var attempted = new List<int>();
		var service = DestructionServiceWith(MoveServiceAnswering(
			destination => destination.Object().DBRef.Number == home.Number
				? new Error<string>("Injected home refusal")
				: new Success(),
			attempted));

		var target = (await Mediator.Send(new GetObjectNodeQuery(container))).Expect<AnySharpObject>();
		var freed = await service.FreeObjectAsync(Parser, target);

		await Assert.That(freed).IsTrue();
		await Assert.That(attempted).IsEquivalentTo(new[] { home.Number, defaultHome });
		await Assert.That(await Database.GetBaseObjectNodeAsync(container)).IsNull();
	}
}
