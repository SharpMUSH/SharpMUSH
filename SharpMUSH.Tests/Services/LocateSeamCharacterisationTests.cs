using Mediator;
using NSubstitute;
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
	public async Task TheDefaultFlagsReachTheGlobalExitsInTheMasterRoom()
	{
		// The master-room scope is the one exits can only be reached through. It gates on
		// ExitsPreference, which LocateFlags.All does not carry (All has ExitsInTheRoomOfLooker
		// where MAT_EVERYTHING has MAT_EXIT), so no exit list is searched under the default flags.
		var room = _factory.CreateRoom(999, "Shared Room");
		var elsewhere = _factory.CreateRoom(998, "Elsewhere");
		var masterRoom = _factory.CreateRoom(MasterRoomNumber, "Master Room");
		var looker = _factory.CreatePlayer(1, "TestPlayer", room);
		Holds(room);
		Holds(masterRoom, _factory.CreateExit(8, "Global", ["g"], masterRoom, elsewhere));

		var result = await _locateService.Locate(_parser, looker, looker, "Global", LocateFlags.All);

		await Assert.That(result.IsValid()).IsTrue();
		await Assert.That(Found(result)).IsEqualTo(new DBRef(8, 0));
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
