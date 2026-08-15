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
		_mediator.CreateStream(
				Arg.Is<GetContentsQuery>(q => Number(q) == container.Object.DBRef.Number),
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
		var room = _factory.CreateRoom(999, "Shared Room");
		var looker = _factory.CreatePlayer(1, "TestPlayer", room);
		Holds(room, _factory.CreateThing(3, "Widget", room));

		var result = await _locateService.LocateAndNotifyIfInvalid(_parser, looker, looker, "Anvil",
			LocateFlags.All | LocateFlags.OnlyMatchLookerControlledObjects);

		await Assert.That(result.IsNone).IsTrue();
		await AssertNotified(ErrorMessages.Notifications.NoMatch);
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
}
