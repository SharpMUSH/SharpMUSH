using Mediator;
using NSubstitute;
using OneOf;
using OneOf.Types;
using System.Collections.Concurrent;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// Characterisation tests driven through <see cref="ILocateService.Locate"/> — the seam every caller
/// actually uses. <see cref="LocateServiceCompatibilityTests"/> drives Match_List directly, which is
/// how a Match_List that returns the right answer and a LocateMatch that discards it both passed.
///
/// Each test states PennMUSH's behaviour (src/match.c) rather than SharpMUSH's current behaviour.
/// </summary>
public class LocateSeamCharacterisationTests
{
	private readonly IMediator _mediator = Substitute.For<IMediator>();
	private readonly INotifyService _notifyService = Substitute.For<INotifyService>();
	private readonly IPermissionService _permissionService = Substitute.For<IPermissionService>();
	private readonly IMUSHCodeParser _parser = Substitute.For<IMUSHCodeParser>();

	private readonly LocateService _locateService;
	private readonly TestObjectFactory _factory = new();
	private readonly int MasterRoomNumber;

	public LocateSeamCharacterisationTests()
	{
		var configFile = Path.Combine(AppContext.BaseDirectory, "Configuration", "Testfile", "mushcnf.dst");
		var options = ReadPennMushConfig.Create(configFile);
		var wrapper = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
		wrapper.CurrentValue.Returns(options);
		MasterRoomNumber = Convert.ToInt32(options.Database.MasterRoom);

		_locateService = new LocateService(_mediator, _notifyService, _permissionService, wrapper);

		_mediator.CreateStream(Arg.Any<GetContentsQuery>(), Arg.Any<CancellationToken>())
			.Returns(_ => AsyncEnumerable.Empty<AnySharpContent>());
		_mediator.CreateStream(Arg.Any<GetPlayerQuery>(), Arg.Any<CancellationToken>())
			.Returns(_ => AsyncEnumerable.Empty<SharpPlayer>());

		_permissionService.Controls(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>()).Returns(true);
		_permissionService.CanInteract(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(),
			Arg.Any<IPermissionService.InteractType>()).Returns(true);
		_permissionService.CanExamine(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>()).Returns(true);

		// The noisy entry points read parser.CurrentState to name the notification's sender.
		_parser.CurrentState.Returns(new ParserState(
			Registers: new ConcurrentStack<Dictionary<string, MString>>([[]]),
			IterationRegisters: [],
			RegexRegisters: [],
			SwitchStack: [],
			ExecutionStack: [],
			EnvironmentRegisters: [],
			CurrentEvaluation: null,
			ParserFunctionDepth: 0,
			Function: null,
			Command: null,
			CommandInvoker: _ => ValueTask.FromResult(new Option<CallState>(new None())),
			Switches: [],
			Arguments: [],
			Executor: null,
			Enactor: null,
			Caller: null,
			Handle: null));
	}

	/// <summary>Contents of one container; anything else stays empty.</summary>
	private void Holds(SharpRoom container, params AnySharpObject[] contents) =>
		Holds(container.Object.DBRef.Number, contents);

	private void Holds(AnySharpObject container, params AnySharpObject[] contents) =>
		Holds(container.Object().DBRef.Number, contents);

	private void Holds(int number, params AnySharpObject[] contents) =>
		_mediator.CreateStream(
				Arg.Is<GetContentsQuery>(q => Number(q) == number),
				Arg.Any<CancellationToken>())
			.Returns(_ => contents.Select(x => x.AsContent).ToAsyncEnumerable());

	private static int Number(GetContentsQuery q) => q.DBRef.Match(d => d, c => c.Object().DBRef).Number;

	private static DBRef Found(AnyOptionalSharpObjectOrError result) =>
		result.WithoutError().WithoutNone().Object().DBRef;

	private async Task AssertNotified(string message) =>
		await _notifyService.Received(1).Notify(
			Arg.Any<AnySharpObject>(),
			Arg.Is<OneOf<MString, string>>(w => w.IsT1 && w.AsT1 == message),
			Arg.Any<AnySharpObject>(),
			Arg.Any<INotifyService.NotificationType>());

	private int ContentsReadsOf(SharpRoom container) =>
		_mediator.ReceivedCalls()
			.Count(c => c.GetMethodInfo().Name == nameof(IMediator.CreateStream)
									&& c.GetArguments()[0] is GetContentsQuery q
									&& Number(q) == container.Object.DBRef.Number);

	[Test]
	public async Task ARoomIsReadOnceEvenThoughTwoScopesDrawFromIt()
	{
		// The neighbour scope wants the room's non-exits and the exit scope wants its exits, so the room
		// was queried twice per search. GetContentsQuery's only cache tag is ObjectContents, which any
		// object moving anywhere in the game invalidates, so the second read is not reliably free.
		var room = _factory.CreateRoom(999, "Shared Room");
		var elsewhere = _factory.CreateRoom(998, "Elsewhere");
		var looker = _factory.CreatePlayer(1, "TestPlayer", room);
		Holds(room,
			_factory.CreateThing(3, "Sword", room),
			_factory.CreateExit(8, "North", ["n"], room, elsewhere));

		var result = await _locateService.Locate(_parser, looker, looker, "North", LocateFlags.All);

		await Assert.That(Found(result)).IsEqualTo(new DBRef(8, 0));
		await Assert.That(ContentsReadsOf(room)).IsEqualTo(1);
	}

	[Test]
	public async Task AnObjectRefusedForControlSaysSoRatherThanClaimingItIsNotThere()
	{
		// match.c's nocontrol: the search still fails, but "I don't see that here" is a lie when the
		// object is in front of you and the answer is that it is not yours.
		var room = _factory.CreateRoom(999, "Shared Room");
		var looker = _factory.CreatePlayer(1, "TestPlayer", room);
		var widget = _factory.CreateThing(3, "Widget", room);
		Holds(room, widget);

		_permissionService.Controls(Arg.Any<AnySharpObject>(), Arg.Is<AnySharpObject>(o => o.Id() == widget.Id()))
			.Returns(false);

		var result = await _locateService.LocateAndNotifyIfInvalid(_parser, looker, looker, "Widget",
			LocateFlags.All | LocateFlags.OnlyMatchLookerControlledObjects);

		await Assert.That(result.IsValid()).IsFalse();
		await AssertNotified(ErrorMessages.Notifications.PermissionDenied);
	}

	[Test]
	public async Task AnObjectThatSimplyIsNotThereStillSaysSo()
	{
		// match.c:485 ends on "I can't see that here." Notifications.NoMatch ("I don't see that here.")
		// is do_look's string; both live in ErrorMessages and the wrong one was wired up.
		var room = _factory.CreateRoom(999, "Shared Room");
		var looker = _factory.CreatePlayer(1, "TestPlayer", room);
		Holds(room, _factory.CreateThing(3, "Widget", room));

		var result = await _locateService.LocateAndNotifyIfInvalid(_parser, looker, looker, "Anvil",
			LocateFlags.All | LocateFlags.OnlyMatchLookerControlledObjects);

		await Assert.That(result.IsNone).IsTrue();
		await AssertNotified(ErrorMessages.Notifications.CantSeeThat);
	}

	[Test]
	public async Task TheLocationsOwnNameIsMatchedOnlyUnderMatchAgainstLookerLocationName()
	{
		// MAT_CONTAINER matches the looker's location *by its own name*; MAT_NEIGHBOR matches what is
		// inside it. MatchAgainstLookerLocationName was wired to the second, so the scope its name
		// describes did not exist and the one it gated was another flag's.
		var room = _factory.CreateRoom(999, "Shared Room");
		var looker = _factory.CreatePlayer(1, "TestPlayer", room);
		Holds(room, _factory.CreateThing(3, "Sword", room));

		var asNeighbour = await _locateService.Locate(_parser, looker, looker, "Shared Room",
			LocateFlags.MatchObjectsInLookerLocation);
		var asContainer = await _locateService.Locate(_parser, looker, looker, "Shared Room",
			LocateFlags.MatchAgainstLookerLocationName);

		await Assert.That(asNeighbour.IsNone).IsTrue();
		await Assert.That(Found(asContainer)).IsEqualTo(room.Object.DBRef);
	}

	[Test]
	public async Task HereDoesNotAnswerForARoomLooker()
	{
		// match.c takes Location(where) for "here" and NOTHING when where is a room, so a room asked for
		// "here" falls through to normal matching rather than answering with itself.
		var room = _factory.CreateRoom(999, "Shared Room");
		var executor = _factory.CreatePlayer(1, "TestPlayer", room);
		Holds(room);

		var result = await _locateService.Locate(_parser, room, executor, "here",
			LocateFlags.MatchHereForLookerLocation | LocateFlags.PreferLockPass);

		await Assert.That(result.IsNone).IsTrue();
	}

	[Test]
	public async Task NamingAScopeSuppressesTheDefaultInjection()
	{
		// fun_locate injects the default set only when nothing but the four modifier flags was given.
		// The old test asked whether four unrelated flags were absent, so asking for the inventory alone
		// quietly searched the room as well.
		var room = _factory.CreateRoom(999, "Shared Room");
		var looker = _factory.CreatePlayer(1, "TestPlayer", room);
		Holds(room, _factory.CreateThing(3, "Sword", room));

		var inventoryOnly = await _locateService.Locate(_parser, looker, looker, "Sword",
			LocateFlags.MatchObjectsInLookerInventory);
		var modifiersOnly = await _locateService.Locate(_parser, looker, looker, "Sword",
			LocateFlags.PreferLockPass);

		await Assert.That(inventoryOnly.IsNone).IsTrue();
		await Assert.That(Found(modifiersOnly)).IsEqualTo(new DBRef(3, 0));
	}

	[Test]
	public async Task AnEnglishOrdinalPicksTheNthMatchRatherThanFailing()
	{
		// match.c: MATCHED sets bestmatch and done=1 on curr == final. Match_List does this correctly;
		// LocateMatch's `if (final != 0 || curr < 1) return new None()` then discards it.
		var room = _factory.CreateRoom(999, "Shared Room");
		var looker = _factory.CreatePlayer(1, "TestPlayer", room);
		Holds(room,
			_factory.CreateThing(3, "Sword", room),
			_factory.CreateThing(4, "Sword", room),
			_factory.CreateThing(5, "Sword", room));

		var result = await _locateService.Locate(_parser, looker, looker, "2nd Sword", LocateFlags.All);

		await Assert.That(result.IsValid()).IsTrue();
		await Assert.That(Found(result)).IsEqualTo(new DBRef(4, 0));
	}

	[Test]
	public async Task ASingleMatchIsNeverAmbiguousEvenWithoutATypePreference()
	{
		// match.c only tests right_type when curr > 1. DbrefFunctions.ParseLocateParameters sets
		// NoTypePreference for every locate() call that is not "*", so this is the common path.
		var room = _factory.CreateRoom(999, "Shared Room");
		var looker = _factory.CreatePlayer(1, "TestPlayer", room);
		Holds(room, _factory.CreateThing(3, "Sword", room));

		var result = await _locateService.Locate(_parser, looker, looker, "Sword",
			LocateFlags.All | LocateFlags.NoTypePreference);

		await Assert.That(result.IsError).IsFalse();
		await Assert.That(Found(result)).IsEqualTo(new DBRef(3, 0));
	}

	[Test]
	public async Task ATypePreferenceIsAPreferenceNotAFilter()
	{
		// MATCH_TYPE returns -1 (truthy) for a wrong-type object unless MAT_TYPE is set, so it stays
		// a candidate and merely loses in choose_thing. Match_List filters it out unconditionally.
		var room = _factory.CreateRoom(999, "Shared Room");
		var looker = _factory.CreatePlayer(1, "TestPlayer", room);
		Holds(room, _factory.CreatePlayer(7, "Sword", room));

		var result = await _locateService.Locate(_parser, looker, looker, "Sword",
			LocateFlags.All | LocateFlags.ThingsPreference);

		await Assert.That(result.IsValid()).IsTrue();
		await Assert.That(Found(result)).IsEqualTo(new DBRef(7, 0));
	}

	[Test]
	public async Task OnlyMatchTypePreferenceIsTheFilter()
	{
		// MAT_TYPE — the flag that makes MATCH_TYPE return 0 and skip the candidate. Read nowhere
		// in LocateService today; this pins the half of the split that must keep working.
		var room = _factory.CreateRoom(999, "Shared Room");
		var looker = _factory.CreatePlayer(1, "TestPlayer", room);
		Holds(room, _factory.CreatePlayer(7, "Sword", room));

		var result = await _locateService.Locate(_parser, looker, looker, "Sword",
			LocateFlags.All | LocateFlags.ThingsPreference | LocateFlags.OnlyMatchTypePreference);

		await Assert.That(result.IsNone).IsTrue();
	}

	[Test]
	public async Task TheDefaultFlagsFindAnExitInTheRoom()
	{
		// Passes today, and only through the neighbour scope — an exit in the room is in that room's
		// contents. Kept as the regression guard for the scope-concatenation refactor.
		var room = _factory.CreateRoom(999, "Shared Room");
		var elsewhere = _factory.CreateRoom(998, "Elsewhere");
		var looker = _factory.CreatePlayer(1, "TestPlayer", room);
		Holds(room, _factory.CreateExit(8, "North", ["n"], room, elsewhere));

		var result = await _locateService.Locate(_parser, looker, looker, "North", LocateFlags.All);

		await Assert.That(result.IsValid()).IsTrue();
		await Assert.That(Found(result)).IsEqualTo(new DBRef(8, 0));
	}

	[Test]
	public async Task TheMasterRoomsGlobalExitsNeedMatchGlobalExits()
	{
		// MAT_GLOBAL is its own flag and is not in MAT_EVERYTHING, so All must not reach the master room
		// — the scope used to be gated on HasFlag(All), which is a different question and answered yes.
		var room = _factory.CreateRoom(999, "Shared Room");
		var elsewhere = _factory.CreateRoom(998, "Elsewhere");
		var masterRoom = _factory.CreateRoom(MasterRoomNumber, "Master Room");
		var looker = _factory.CreatePlayer(1, "TestPlayer", room);
		Holds(room);
		Holds(masterRoom, _factory.CreateExit(8, "Global", ["g"], masterRoom, elsewhere));

		var withoutTheFlag = await _locateService.Locate(_parser, looker, looker, "Global", LocateFlags.All);
		var withTheFlag = await _locateService.Locate(_parser, looker, looker, "Global",
			LocateFlags.All | LocateFlags.MatchGlobalExits);

		await Assert.That(withoutTheFlag.IsNone).IsTrue();
		await Assert.That(Found(withTheFlag)).IsEqualTo(new DBRef(8, 0));
	}

	[Test]
	public async Task AnExactMatchSuppressesLaterPartialMatches()
	{
		// MATCH_LIST guards the partial branch with (!exact || !GoodObject(bestmatch)). Without it a
		// later prefix hit still runs choose_thing and still bumps curr, so an exact match reads as
		// ambiguous.
		var room = _factory.CreateRoom(999, "Shared Room");
		var looker = _factory.CreatePlayer(1, "TestPlayer", room);
		Holds(room,
			_factory.CreateThing(3, "Sword", room),
			_factory.CreateThing(9, "Swordfish", room));

		var result = await _locateService.Locate(_parser, looker, looker, "Sword", LocateFlags.All);

		await Assert.That(result.IsError).IsFalse();
		await Assert.That(Found(result)).IsEqualTo(new DBRef(3, 0));
	}

	[Test]
	public async Task MeResolvesTheLookerNotTheExecutor()
	{
		// Locate(looker, executor, ...) calls LocateMatch(executor, looker, ...) — the two arguments
		// are swapped, so the "me" and "here" branches answer for the executor while the contents
		// scopes search the looker.
		var room = _factory.CreateRoom(999, "Shared Room");
		var looker = _factory.CreatePlayer(1, "Looker", room);
		var executor = _factory.CreatePlayer(2, "Executor", room);

		var result = await _locateService.Locate(_parser, looker, executor, "me", LocateFlags.All);

		await Assert.That(result.IsValid()).IsTrue();
		await Assert.That(Found(result)).IsEqualTo(new DBRef(1, 0));
	}

	[Test]
	public async Task HereResolvesTheLookersLocation()
	{
		var room = _factory.CreateRoom(999, "Shared Room");
		var otherRoom = _factory.CreateRoom(997, "Other Room");
		var looker = _factory.CreatePlayer(1, "Looker", room);
		var executor = _factory.CreatePlayer(2, "Executor", otherRoom);

		var result = await _locateService.Locate(_parser, looker, executor, "here", LocateFlags.All);

		await Assert.That(result.IsValid()).IsTrue();
		await Assert.That(Found(result)).IsEqualTo(room.Object.DBRef);
	}

	[Test]
	public async Task AFilterOnItsOwnIsNotAScopeAndStillGetsTheDefaults()
	{
		// MAT_CONTENTS narrows whatever the scopes turn up to the looker's own contents; it names nowhere
		// to look. Treating it as a scope suppressed the default injection and left the search with no
		// list at all — which is what the CARRY^<name> lock key passes, so every one of them failed.
		var room = _factory.CreateRoom(999, "Shared Room");
		var looker = _factory.CreatePlayer(1, "TestPlayer", room);
		var carried = _factory.CreateThing(3, "Sword", room);
		Holds(looker, carried);
		Holds(room, looker);

		var result = await _locateService.Locate(_parser, looker, looker, "Sword",
			LocateFlags.OnlyMatchObjectsInLookerInventory);

		await Assert.That(Found(result)).IsEqualTo(new DBRef(3, 0));
	}

	[Test]
	public async Task MyRestrictsTheSearchToTheInventory()
	{
		// parse_english's "my " clears MAT_NEIGHBOR (match.c:534). The mask named the exit bit twice and
		// the neighbour bit not at all, so "my sword" went on searching the room.
		var room = _factory.CreateRoom(999, "Shared Room");
		var looker = _factory.CreatePlayer(1, "TestPlayer", room);
		Holds(looker); // empty inventory
		Holds(room, looker, _factory.CreateThing(3, "Sword", room));

		var result = await _locateService.Locate(_parser, looker, looker, "my Sword", LocateFlags.All);

		await Assert.That(result.IsNone).IsTrue();
	}

	[Test]
	public async Task TowardKeepsTheExitScopeItSelects()
	{
		// parse_english's "toward " clears MAT_NEIGHBOR | MAT_POSSESSION | MAT_CONTAINER (match.c:540) and
		// pointedly not MAT_EXIT. The mask cleared MAT_EXIT — the one scope the adjective exists to pick —
		// so no "toward <exit>" could ever match.
		var room = _factory.CreateRoom(999, "Shared Room");
		var elsewhere = _factory.CreateRoom(998, "Elsewhere");
		var looker = _factory.CreatePlayer(1, "TestPlayer", room);
		Holds(looker);
		Holds(room, looker, _factory.CreateExit(8, "North", ["n"], room, elsewhere));

		var result = await _locateService.Locate(_parser, looker, looker, "toward North", LocateFlags.All);

		await Assert.That(Found(result)).IsEqualTo(new DBRef(8, 0));
	}

	[Test]
	public async Task MatTypeWithNoPreferredTypeStillMatchesEverything()
	{
		// PennMUSH's NOTYPE is 0xFFFF (flags.h:189), so `type & Typeof(match)` is truthy for anything and
		// MAT_TYPE with no type named filters nothing. SharpObjectTypes.None is the same sentinel spelt as
		// zero, which masks to the opposite answer: locate(...,"Fe") walked no exit list and
		// locate(...,"F*") refused "me".
		var room = _factory.CreateRoom(999, "Shared Room");
		var elsewhere = _factory.CreateRoom(998, "Elsewhere");
		var looker = _factory.CreatePlayer(1, "TestPlayer", room);
		Holds(looker);
		Holds(room, looker, _factory.CreateExit(8, "North", ["n"], room, elsewhere));

		// exactly what ParseLocateParameters("Fe") and ("F*") produce
		var exit = await _locateService.Locate(_parser, looker, looker, "North",
			LocateFlags.OnlyMatchTypePreference | LocateFlags.ExitsInTheRoomOfLooker | LocateFlags.NoTypePreference);
		var me = await _locateService.Locate(_parser, looker, looker, "me",
			LocateFlags.All | LocateFlags.OnlyMatchTypePreference | LocateFlags.NoTypePreference);

		await Assert.That(Found(exit)).IsEqualTo(new DBRef(8, 0));
		await Assert.That(Found(me)).IsEqualTo(new DBRef(1, 0));
	}


	[Test]
	[Arguments("Big Sword", "Sword", true)]
	[Arguments("Big Sword", "Big", true)]
	[Arguments("Big Sword", "word", false)]
	[Arguments("BBS - Myrddin's Global BBS", "Myrddin", true)]
	[Arguments("BBS - Myrddin's Global BBS", "s", true)]
	[Arguments("mbboard", "bboard", false)]
	[Arguments("oak door", "do", true)]
	[Arguments("oak door", "oor", false)]
	public async Task PartialMatchingFollowsStringMatchWordBoundaries(string objectName, string search, bool found)
	{
		// PennMUSH's string_match (strutil.c) tests whether the search term prefixes *any word* of the
		// name, not just the whole string — it rescans at each isalnum boundary. Testing only the first
		// word left every multi-word object reachable by its leading word or in full and no other way,
		// which is not how players refer to things.
		var room = _factory.CreateRoom(999, "Shared Room");
		var looker = _factory.CreatePlayer(1, "TestPlayer", room);
		Holds(room, looker, _factory.CreateThing(3, objectName, room));

		var result = await _locateService.Locate(_parser, looker, looker, search, LocateFlags.All);

		await Assert.That(result.IsValid()).IsEqualTo(found);
	}

	[Test]
	public async Task ParseEnglishRestoresANonOrdinalDigitToken()
	{
		// match.c:590 — '0th'/'12nd' "wasn't really a count adjective. Reset and press on." And
		// match.c:560 quick-exits a count with no noun after it. Both used to consume the token, so a
		// thing named "5 Swords" was searched for as "Swords" and never found.
		var room = _factory.CreateRoom(999, "Shared Room");
		var looker = _factory.CreatePlayer(1, "TestPlayer", room);
		Holds(looker);
		Holds(room, looker, _factory.CreateThing(3, "5 Swords", room), _factory.CreateThing(4, "2nd", room));

		var noSuffix = await _locateService.Locate(_parser, looker, looker, "5 Swords", LocateFlags.All);
		var countNoNoun = await _locateService.Locate(_parser, looker, looker, "2nd", LocateFlags.All);

		await Assert.That(Found(noSuffix)).IsEqualTo(new DBRef(3, 0));
		await Assert.That(Found(countNoNoun)).IsEqualTo(new DBRef(4, 0));
	}

	[Test]
	public async Task NearbyUsesTheSourceRoomOfAnExit()
	{
		// where_is() returns Home(thing) for an exit, and Home/Source/Exits are all db[x].exits
		// (dbdefs.h:35-40) — so it is the room the exit sits in, not where it leads. Destination() is the
		// separate db[x].location field. SharpExit.Location is documented as Source(), so FriendlyWhereIs
		// is already the right answer here.
		var source = _factory.CreateRoom(999, "Source");
		var destination = _factory.CreateRoom(998, "Destination");
		var exit = _factory.CreateExit(8, "North", ["n"], source, destination);
		var traveller = _factory.CreatePlayer(1, "Traveller", destination);
		var stayer = _factory.CreatePlayer(2, "Stayer", source);

		await Assert.That(await LocateService.Nearby(exit, stayer)).IsTrue();
		await Assert.That(await LocateService.Nearby(exit, traveller)).IsFalse();
	}

	[Test]
	public async Task ARoomIsNearbyWhatItContainsButNotAnotherRoom()
	{
		// nearby() early-returns for two rooms; the room-vs-content arms are the ones where where_is's
		// NOTHING and FriendlyWhereIs's own-dbref could have differed, and do not.
		var roomA = _factory.CreateRoom(999, "A");
		var roomB = _factory.CreateRoom(998, "B");
		var inA = _factory.CreatePlayer(1, "InA", roomA);

		await Assert.That(await LocateService.Nearby(roomA, roomB)).IsFalse();
		await Assert.That(await LocateService.Nearby(roomA, inA)).IsTrue();
		await Assert.That(await LocateService.Nearby(inA, roomA)).IsTrue();
	}

	[Test]
	[Arguments("99999999999999999999th Sword")]
	[Arguments("12345678901st Sword")]
	[Arguments("\u0663rd Sword")]
	public async Task AnOrdinalTooBigOrNotAsciiIsNotACount(string search)
	{
		// NthRegex's \d accepts every Unicode decimal digit and caps no length, so int.Parse used to
		// answer these with OverflowException/FormatException out of a path any player can type.
		// match.c's strtoul saturates and then fails the suffix test, so the token is restored and the
		// whole string stands as the name.
		//
		// The decoy named "Sword" is what makes this discriminating. Asserting "found nothing" would
		// pass either way — a leading token wrongly consumed leaves an ordinal search for "Sword" that
		// also finds nothing. With the decoy present, consuming the token finds *it* (or, for a count
		// that survives, nothing), where restoring the token must find the object actually so named.
		var room = _factory.CreateRoom(999, "Shared Room");
		var looker = _factory.CreatePlayer(1, "TestPlayer", room);
		Holds(looker);
		Holds(room, looker,
			_factory.CreateThing(3, "Sword", room),
			_factory.CreateThing(4, search, room));

		var result = await _locateService.Locate(_parser, looker, looker, search, LocateFlags.All);

		await Assert.That(Found(result)).IsEqualTo(new DBRef(4, 0));
	}
}
