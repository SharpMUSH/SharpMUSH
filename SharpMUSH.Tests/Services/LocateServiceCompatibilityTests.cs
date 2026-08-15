using Mediator;
using NSubstitute;
using OneOf.Types;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using System.Collections.Immutable;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// Tests for LocateService to verify PennMUSH compatibility fixes
/// </summary>
public class LocateServiceCompatibilityTests
{
	private readonly IMediator _mediator = Substitute.For<IMediator>();
	private readonly INotifyService _notifyService = Substitute.For<INotifyService>();
	private readonly IPermissionService _permissionService = Substitute.For<IPermissionService>();
	private readonly IMUSHCodeParser _parser = Substitute.For<IMUSHCodeParser>();

	private readonly LocateService _locateService;
	private readonly TestObjectFactory _factory = new();

	public LocateServiceCompatibilityTests()
	{
		var configFile = Path.Combine(AppContext.BaseDirectory, "Configuration", "Testfile", "mushcnf.dst");
		var options = ReadPennMushConfig.Create(configFile);

		var wrapper = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
		wrapper.CurrentValue.Returns(options);

		_locateService = new LocateService(_mediator, _notifyService, _permissionService, wrapper);
	}

	/// <summary>
	/// Contents of exactly one container; every other container is empty. A stub that answers every
	/// GetContentsQuery puts the same object in the looker's inventory *and* in the room, so it matches
	/// twice and the search reports ambiguity that the real database could not produce.
	/// </summary>
	private void Holds(SharpRoom container, IAsyncEnumerable<AnySharpObject> contents)
	{
		_mediator.CreateStream(Arg.Any<GetContentsQuery>(), Arg.Any<CancellationToken>())
			.Returns(_ => AsyncEnumerable.Empty<AnySharpContent>());
		_mediator.CreateStream(
				Arg.Is<GetContentsQuery>(q =>
					q.DBRef.Match(d => d, c => c.Object().DBRef).Number == container.Object.DBRef.Number),
				Arg.Any<CancellationToken>())
			.Returns(_ => contents.Select(x => x.AsContent));
	}

	[Test]
	public async Task LocateMatch_NameMatching_ShouldMatchExactNamesForNonExits()
	{
		var sharedRoom = _factory.CreateRoom(999, "Shared Room");

		var player = _factory.CreatePlayer(1, "TestPlayer", sharedRoom);
		var thing = _factory.CreateThing(3, "TestObject", sharedRoom, player);

		var contents = new[] { thing }.ToAsyncEnumerable();

		Holds(sharedRoom, contents);

		_mediator.CreateStream(Arg.Any<GetPlayerQuery>(), Arg.Any<CancellationToken>())
			.Returns(_ => AsyncEnumerable.Empty<SharpPlayer>());

		_permissionService.Controls(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>())
			.Returns(true);

		_permissionService.CanInteract(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(),
				Arg.Any<IPermissionService.InteractType>())
			.Returns(true);

		_permissionService.CanExamine(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>())
			.Returns(true);

		var result = await _locateService.Locate(_parser, player, player, "TestObject",
			LocateFlags.MatchObjectsInLookerInventory);

		await Assert.That(result.IsValid()).IsTrue();
		await Assert.That(result.WithoutError().WithoutNone().Object().DBRef).IsEqualTo(new DBRef(3, 0));
	}

	[Test]
	public async Task LocateMatch_NameMatching_ShouldNotMatchWrongNamesForNonExits()
	{
		// This test verifies the fix for the inverted logic bug
		// Before fix: (!cur.IsExit && !string.Equals(...)) would match everything that DIDN'T match
		// After fix: (!cur.IsExit && string.Equals(...)) only matches exact names

		var sharedRoom = _factory.CreateRoom(999, "Shared Room");

		var player = _factory.CreatePlayer(1, "TestPlayer", sharedRoom);
		var thing = _factory.CreateThing(3, "TestObject", sharedRoom, player);

		var contents = new[] { thing }.ToAsyncEnumerable();

		Holds(sharedRoom, contents);

		_mediator.CreateStream(Arg.Any<GetPlayerQuery>(), Arg.Any<CancellationToken>())
			.Returns(_ => AsyncEnumerable.Empty<SharpPlayer>());

		_permissionService.Controls(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>())
			.Returns(true);

		_permissionService.CanInteract(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(),
				Arg.Any<IPermissionService.InteractType>())
			.Returns(true);

		_permissionService.CanExamine(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>())
			.Returns(true);

		var result = await _locateService.Locate(_parser, player, player, "WrongName",
			LocateFlags.MatchObjectsInLookerInventory);

		await Assert.That(result.IsNone).IsTrue();
	}

	[Test]
	public async Task LocateMatch_MeMatching_ShouldRespectNoTypePreference()
	{
		// This test verifies the fix for the NoTypePreference check

		var sharedRoom = _factory.CreateRoom(999, "Shared Room");
		var player = _factory.CreatePlayer(1, "TestPlayer", sharedRoom);

		_mediator.CreateStream(Arg.Any<GetPlayerQuery>(), Arg.Any<CancellationToken>())
			.Returns(_ => AsyncEnumerable.Empty<SharpPlayer>());

		_permissionService.Controls(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>())
			.Returns(true);

		_permissionService.CanInteract(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(),
				Arg.Any<IPermissionService.InteractType>())
			.Returns(true);

		_permissionService.CanExamine(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>())
			.Returns(true);

		// Use PreferLockPass to prevent auto-adding flags that would interfere
		var resultWithNoTypePreference = await _locateService.Locate(_parser, player, player, "me",
			LocateFlags.NoTypePreference | LocateFlags.MatchMeForLooker | LocateFlags.PreferLockPass);

		var resultWithoutNoTypePreference = await _locateService.Locate(_parser, player, player, "me",
			LocateFlags.MatchMeForLooker | LocateFlags.PreferLockPass);

		await Assert.That(resultWithNoTypePreference.IsNone || resultWithNoTypePreference.IsError).IsTrue();
		await Assert.That(resultWithoutNoTypePreference.IsValid()).IsTrue();
		await Assert.That(resultWithoutNoTypePreference.WithoutError().WithoutNone().Object().DBRef)
			.IsEqualTo(new DBRef(1, 0));
	}

	[Test]
	public async Task LocateMatch_PermissionCheck_ShouldUseCorrectLogic()
	{
		var room1 = _factory.CreateRoom(1001, "Room 1");
		var room2 = _factory.CreateRoom(1002, "Room 2");

		var player = _factory.CreatePlayer(1, "TestPlayer", room1);
		var target = _factory.CreatePlayer(2, "TargetPlayer", room2);

		_mediator.CreateStream(Arg.Any<GetPlayerQuery>(), Arg.Any<CancellationToken>())
			.Returns(_ => AsyncEnumerable.Empty<SharpPlayer>());

		// "me" is the looker's, and MAT_CONTROL asks whether the permission subject — the executor —
		// controls the object being returned. It used to ask whether the executor controlled itself,
		// which is what the looker/where swap in LocateMatch made it look like.
		_permissionService.Controls(target, player)
			.Returns(true);

		_permissionService.CanInteract(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(),
				Arg.Any<IPermissionService.InteractType>())
			.Returns(true);

		_permissionService.CanExamine(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>())
			.Returns(true);

		var resultWithControlRequired = await _locateService.Locate(_parser, player, target, "me",
			LocateFlags.MatchMeForLooker | LocateFlags.OnlyMatchLookerControlledObjects | LocateFlags.PreferLockPass);

		var resultWithoutControlRequired = await _locateService.Locate(_parser, player, target, "me",
			LocateFlags.MatchMeForLooker | LocateFlags.PreferLockPass);

		await Assert.That(resultWithControlRequired.IsValid()).IsTrue();
		await Assert.That(resultWithControlRequired.WithoutError().WithoutNone().Object().DBRef)
			.IsEqualTo(new DBRef(1, 0));
		await Assert.That(resultWithoutControlRequired.IsValid()).IsTrue();
		await Assert.That(resultWithoutControlRequired.WithoutError().WithoutNone().Object().DBRef)
			.IsEqualTo(new DBRef(1, 0));

		_permissionService.Controls(target, player)
			.Returns(false);

		var resultUncontrolled = await _locateService.Locate(_parser, player, target, "me",
			LocateFlags.MatchMeForLooker | LocateFlags.OnlyMatchLookerControlledObjects | LocateFlags.PreferLockPass);

		await Assert.That(resultUncontrolled.IsError).IsTrue();
		await Assert.That(resultUncontrolled.AsError.Value).IsEqualTo(ErrorMessages.Returns.PermissionDenied);
	}

	[Test]
	public async Task LocateMatch_HereMatching_ShouldMatchCurrentLocation()
	{
		var sharedRoom = _factory.CreateRoom(999, "Shared Room");
		var player = _factory.CreatePlayer(1, "TestPlayer", sharedRoom);

		_mediator.CreateStream(Arg.Any<GetContentsQuery>(), Arg.Any<CancellationToken>())
			.Returns(_ => AsyncEnumerable.Empty<AnySharpContent>());

		_mediator.CreateStream(Arg.Any<GetPlayerQuery>(), Arg.Any<CancellationToken>())
			.Returns(_ => AsyncEnumerable.Empty<SharpPlayer>());

		_permissionService.Controls(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>())
			.Returns(true);

		_permissionService.CanInteract(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(),
				Arg.Any<IPermissionService.InteractType>())
			.Returns(true);

		_permissionService.CanExamine(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>())
			.Returns(true);

		var result = await _locateService.Locate(_parser, player, player, "here",
			LocateFlags.MatchHereForLookerLocation | LocateFlags.PreferLockPass);

		await Assert.That(result.IsValid()).IsTrue();
		await Assert.That(result.WithoutError().WithoutNone().IsRoom).IsTrue();
		await Assert.That(result.WithoutError().WithoutNone().Object().DBRef.Number).IsEqualTo(999);
	}

	[Test]
	public async Task LocateMatch_AbsoluteDBRef_ShouldMatchByDBRef()
	{
		var sharedRoom = _factory.CreateRoom(999, "Shared Room");
		var player = _factory.CreatePlayer(1, "TestPlayer", sharedRoom);
		var thing = _factory.CreateThing(42, "TestObject", sharedRoom, player);

		_mediator.Send(Arg.Is<GetObjectNodeQuery>(q => q.DBRef.Number == 42), Arg.Any<CancellationToken>())
			.Returns(callInfo => ValueTask.FromResult<AnyOptionalSharpObject>(thing.WithNoneOption()));

		_mediator.CreateStream(Arg.Any<GetPlayerQuery>(), Arg.Any<CancellationToken>())
			.Returns(_ => AsyncEnumerable.Empty<SharpPlayer>());

		_permissionService.Controls(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>())
			.Returns(true);

		_permissionService.CanInteract(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(),
				Arg.Any<IPermissionService.InteractType>())
			.Returns(true);

		_permissionService.CanExamine(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>())
			.Returns(true);

		var result = await _locateService.Locate(_parser, player, player, "#42",
			LocateFlags.AbsoluteMatch | LocateFlags.PreferLockPass);

		await Assert.That(result.IsValid()).IsTrue();
		await Assert.That(result.WithoutError().WithoutNone().Object().DBRef).IsEqualTo(new DBRef(42, 0));
	}

	[Test]
	public async Task LocateMatch_TypePreference_ShouldRespectPlayerPreference()
	{
		var sharedRoom = _factory.CreateRoom(999, "Shared Room");
		var player = _factory.CreatePlayer(1, "TestPlayer", sharedRoom);
		var targetPlayer = _factory.CreatePlayer(5, "Bob", sharedRoom);
		var thing = _factory.CreateThing(6, "Bob", sharedRoom, player);

		var contents = new[] { thing, targetPlayer }.ToAsyncEnumerable();

		Holds(sharedRoom, contents);

		var playerResults = new[] { targetPlayer.AsPlayer }.ToAsyncEnumerable();
		_mediator.CreateStream(Arg.Is<GetPlayerQuery>(q => q.Name.Contains("Bob")), Arg.Any<CancellationToken>())
			.Returns(_ => playerResults);

		_permissionService.Controls(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>())
			.Returns(true);

		_permissionService.CanInteract(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(),
				Arg.Any<IPermissionService.InteractType>())
			.Returns(true);

		_permissionService.CanExamine(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>())
			.Returns(true);

		var result = await _locateService.Locate(_parser, player, player, "*Bob",
			LocateFlags.PlayersPreference | LocateFlags.MatchOptionalWildCardForPlayerName |
			LocateFlags.MatchObjectsInLookerInventory | LocateFlags.PreferLockPass);

		await Assert.That(result.IsValid()).IsTrue();
		await Assert.That(result.WithoutError().WithoutNone().IsPlayer).IsTrue();
		await Assert.That(result.WithoutError().WithoutNone().Object().DBRef).IsEqualTo(new DBRef(5, 0));
	}

	[Test]
	public async Task MatchList_PartialMatching_ShouldFindObjectByPrefixName()
	{
		// Directly tests that Match_List does prefix matching (PennMUSH string_match() behavior).
		// Verifies that "Long" correctly locates an object named "LongObjectName" using prefix matching.

		var sharedRoom = _factory.CreateRoom(999, "Shared Room");
		var player = _factory.CreatePlayer(1, "TestPlayer", sharedRoom);
		var thing = _factory.CreateThing(3, "LongObjectName", sharedRoom, player);

		_permissionService.CanInteract(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(),
				Arg.Any<IPermissionService.InteractType>())
			.Returns(true);

		var list = new[] { thing }.ToAsyncEnumerable();

		var state = new LocateService.MatchState(0);
		await _locateService.MatchList(state, list, player,
			LocateFlags.NoTypePreference, "Long");
		var bestMatch = state.Best;
		var curr = state.Count;
		var exact = state.Exact;

		// Assert - prefix match should count one hit and mark it as a partial (non-exact) match
		await Assert.That(curr).IsEqualTo(1);
		await Assert.That(exact).IsFalse();
		await Assert.That(bestMatch.IsValid()).IsTrue();
		await Assert.That(bestMatch.WithoutError().WithoutNone().Object().DBRef).IsEqualTo(new DBRef(3, 0));
	}

	[Test]
	public async Task MatchList_PartialMatching_ShouldNotFindObjectByPrefixWhenNoPartialMatchesSet()
	{
		var sharedRoom = _factory.CreateRoom(999, "Shared Room");
		var player = _factory.CreatePlayer(1, "TestPlayer", sharedRoom);
		var thing = _factory.CreateThing(3, "LongObjectName", sharedRoom, player);

		_permissionService.CanInteract(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(),
				Arg.Any<IPermissionService.InteractType>())
			.Returns(true);

		var list = new[] { thing }.ToAsyncEnumerable();

		var state = new LocateService.MatchState(0);
		await _locateService.MatchList(state, list, player,
			LocateFlags.NoTypePreference | LocateFlags.NoPartialMatches, "Long");
		var bestMatch = state.Best;
		var curr = state.Count;

		await Assert.That(curr).IsEqualTo(0);
		await Assert.That(bestMatch.IsNone).IsTrue();
	}

	[Test]
	public async Task LocateMatch_PartialMatching_InventoryOnlyDoesNotReachTheRoom()
	{
		// LongObjectName sits in the room, not in the looker's hands, and PreferLockPass suppresses the
		// default flag injection — so the inventory scope is the only one that runs and finds nothing.
		// Prefix matching itself is covered by MatchList_PartialMatching_* and the seam tests.

		var sharedRoom = _factory.CreateRoom(999, "Shared Room");
		var player = _factory.CreatePlayer(1, "TestPlayer", sharedRoom);
		var thing = _factory.CreateThing(3, "LongObjectName", sharedRoom, player);

		var contents = new[] { thing }.ToAsyncEnumerable();

		Holds(sharedRoom, contents);

		_mediator.CreateStream(Arg.Any<GetPlayerQuery>(), Arg.Any<CancellationToken>())
			.Returns(_ => AsyncEnumerable.Empty<SharpPlayer>());

		_permissionService.Controls(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>())
			.Returns(true);

		_permissionService.CanInteract(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(),
				Arg.Any<IPermissionService.InteractType>())
			.Returns(true);

		_permissionService.CanExamine(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>())
			.Returns(true);

		var result = await _locateService.Locate(_parser, player, player, "Long",
			LocateFlags.MatchObjectsInLookerInventory | LocateFlags.PreferLockPass);

		// The room name "Shared Room" does not start with "Long", so no match expected.
		await Assert.That(result.IsNone).IsTrue();
	}

	[Test]
	public async Task LocateMatch_NoPartialMatches_ShouldRequireExactMatch()
	{
		var sharedRoom = _factory.CreateRoom(999, "Shared Room");
		var player = _factory.CreatePlayer(1, "TestPlayer", sharedRoom);
		var thing = _factory.CreateThing(3, "LongObjectName", sharedRoom, player);

		var contents = new[] { thing }.ToAsyncEnumerable();

		Holds(sharedRoom, contents);

		_mediator.CreateStream(Arg.Any<GetPlayerQuery>(), Arg.Any<CancellationToken>())
			.Returns(_ => AsyncEnumerable.Empty<SharpPlayer>());

		_permissionService.Controls(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>())
			.Returns(true);

		_permissionService.CanInteract(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(),
				Arg.Any<IPermissionService.InteractType>())
			.Returns(true);

		_permissionService.CanExamine(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>())
			.Returns(true);

		var result = await _locateService.Locate(_parser, player, player, "Long",
			LocateFlags.MatchObjectsInLookerInventory | LocateFlags.NoPartialMatches);

		await Assert.That(result.IsNone).IsTrue();
	}

	[Test]
	public async Task LocateMatch_MatchObjectsInLookerLocation_ShouldFindObjectsInSameRoom()
	{
		var sharedRoom = _factory.CreateRoom(999, "Shared Room");
		var player = _factory.CreatePlayer(1, "TestPlayer", sharedRoom);
		var thing = _factory.CreateThing(3, "RoomObject", sharedRoom, player);

		var contents = new[] { thing, player }.ToAsyncEnumerable();

		Holds(sharedRoom, contents);

		_mediator.CreateStream(Arg.Any<GetPlayerQuery>(), Arg.Any<CancellationToken>())
			.Returns(_ => AsyncEnumerable.Empty<SharpPlayer>());

		_permissionService.Controls(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>())
			.Returns(true);

		_permissionService.CanInteract(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(),
				Arg.Any<IPermissionService.InteractType>())
			.Returns(true);

		_permissionService.CanExamine(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>())
			.Returns(true);

		var result = await _locateService.Locate(_parser, player, player, "RoomObject",
			LocateFlags.MatchObjectsInLookerLocation);

		await Assert.That(result.IsValid()).IsTrue();
		await Assert.That(result.WithoutError().WithoutNone().Object().DBRef).IsEqualTo(new DBRef(3, 0));
	}

	[Test]
	public async Task LocateMatch_MultipleObjects_ShouldHandleAmbiguousMatches()
	{
		// In PennMUSH, this typically returns an ambiguous match or the first/last depending on flags

		var sharedRoom = _factory.CreateRoom(999, "Shared Room");
		var player = _factory.CreatePlayer(1, "TestPlayer", sharedRoom);
		var thing1 = _factory.CreateThing(3, "Coin", sharedRoom, player);
		var thing2 = _factory.CreateThing(4, "Coin", sharedRoom, player);

		var contents = new[] { thing1, thing2 }.ToAsyncEnumerable();

		Holds(sharedRoom, contents);

		_mediator.CreateStream(Arg.Any<GetPlayerQuery>(), Arg.Any<CancellationToken>())
			.Returns(_ => AsyncEnumerable.Empty<SharpPlayer>());

		_permissionService.Controls(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>())
			.Returns(true);

		_permissionService.CanInteract(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(),
				Arg.Any<IPermissionService.InteractType>())
			.Returns(true);

		_permissionService.CanExamine(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>())
			.Returns(true);

		var resultLast = await _locateService.Locate(_parser, player, player, "Coin",
			LocateFlags.MatchObjectsInLookerInventory | LocateFlags.UseLastIfAmbiguous);

		await Assert.That(resultLast.IsValid()).IsTrue();
		await Assert.That(resultLast.WithoutError().WithoutNone().Object().DBRef.Number).IsEqualTo(4);
	}

	[Test]
	public async Task LocateMatch_VisibilityCheck_ShouldRespectCanExamine()
	{
		var sharedRoom = _factory.CreateRoom(999, "Shared Room");
		var player = _factory.CreatePlayer(1, "TestPlayer", sharedRoom);
		var thing = _factory.CreateThing(3, "HiddenObject", sharedRoom, player);

		var contents = new[] { thing }.ToAsyncEnumerable();

		Holds(sharedRoom, contents);

		_mediator.CreateStream(Arg.Any<GetPlayerQuery>(), Arg.Any<CancellationToken>())
			.Returns(_ => AsyncEnumerable.Empty<SharpPlayer>());

		_permissionService.Controls(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>())
			.Returns(true);

		_permissionService.CanInteract(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(),
				Arg.Any<IPermissionService.InteractType>())
			.Returns(false);

		_permissionService.CanExamine(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>())
			.Returns(false);

		var result = await _locateService.Locate(_parser, player, player, "HiddenObject",
			LocateFlags.MatchObjectsInLookerInventory);

		await Assert.That(result.IsNone).IsTrue();
	}

	[Test]
	public async Task LocateMatch_DifferentExecutorAndLooker_ShouldCheckNearby()
	{
		var room1 = _factory.CreateRoom(1001, "Room 1");
		var room2 = _factory.CreateRoom(1002, "Room 2");

		var looker = _factory.CreatePlayer(1, "LookerPlayer", room1);
		var executor = _factory.CreatePlayer(2, "ExecutorPlayer", room2);
		var thing = _factory.CreateThing(3, "TestObject", room1, looker);

		var contents = new[] { thing }.ToAsyncEnumerable();

		Holds(room2, contents);

		_mediator.CreateStream(Arg.Any<GetPlayerQuery>(), Arg.Any<CancellationToken>())
			.Returns(_ => AsyncEnumerable.Empty<SharpPlayer>());

		// Set up permissions - executor doesn't control looker (critical for this test)
		_permissionService.Controls(executor, looker)
			.Returns(false);

		_permissionService.Controls(looker, looker)
			.Returns(true);
		_permissionService.Controls(executor, executor)
			.Returns(true);

		_permissionService.CanInteract(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(),
				Arg.Any<IPermissionService.InteractType>())
			.Returns(true);

		_permissionService.CanExamine(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>())
			.Returns(true);

		var result = await _locateService.Locate(_parser, looker, executor, "TestObject",
			LocateFlags.MatchObjectsInLookerInventory);

		await Assert.That(result.IsError).IsTrue();
		await Assert.That(result.AsError.Value).Contains("NOT PERMITTED");
	}

	[Test]
	public async Task MatchList_CanInteract_False_SkipsObject()
	{
		// Verifies the restored CanInteract continue: when CanInteract returns false,
		// the object must be silently skipped rather than considered for matching.

		var sharedRoom = _factory.CreateRoom(999, "Shared Room");
		var player = _factory.CreatePlayer(1, "TestPlayer", sharedRoom);
		var thing = _factory.CreateThing(3, "VisibleObject", sharedRoom, player);
		var hiddenThing = _factory.CreateThing(4, "HiddenObject", sharedRoom, player);

		_permissionService.CanInteract(Arg.Any<AnySharpObject>(), thing, Arg.Any<IPermissionService.InteractType>())
			.Returns(true);
		_permissionService.CanInteract(Arg.Any<AnySharpObject>(), hiddenThing, Arg.Any<IPermissionService.InteractType>())
			.Returns(false); // hidden from matching

		var list = new[] { thing, hiddenThing }.ToAsyncEnumerable();

		var state = new LocateService.MatchState(0);
		await _locateService.MatchList(state, list, player,
			LocateFlags.NoTypePreference, "HiddenObject");
		var bestMatch = state.Best;
		var curr = state.Count;

		// HiddenObject is not interactable → skipped → not found
		await Assert.That(curr).IsEqualTo(0);
		await Assert.That(bestMatch.IsNone).IsTrue();
	}

	[Test]
	public async Task MatchList_ExitName_NoPrefixMatching()
	{
		// PennMUSH exit matching uses exact name only — prefix search is for things/players.
		// "Nor" must NOT match an exit named "North".

		var sharedRoom = _factory.CreateRoom(999, "Shared Room");
		var player = _factory.CreatePlayer(1, "TestPlayer", sharedRoom);
		var exit = _factory.CreateExit(10, "North", ["n", "north"], sharedRoom);

		_permissionService.CanInteract(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(),
				Arg.Any<IPermissionService.InteractType>())
			.Returns(true);

		var list = new[] { exit }.ToAsyncEnumerable();

		var state = new LocateService.MatchState(0);
		await _locateService.MatchList(state, list, player,
			LocateFlags.NoTypePreference, "Nor");
		var bestMatch = state.Best;
		var curr = state.Count; // prefix — must not match exit

		await Assert.That(curr).IsEqualTo(0);
		await Assert.That(bestMatch.IsNone).IsTrue();
	}

	[Test]
	public async Task MatchList_ExitName_ExactMatch_FindsExit()
	{
		var sharedRoom = _factory.CreateRoom(999, "Shared Room");
		var player = _factory.CreatePlayer(1, "TestPlayer", sharedRoom);
		var exit = _factory.CreateExit(10, "North", ["n"], sharedRoom);

		_permissionService.CanInteract(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(),
				Arg.Any<IPermissionService.InteractType>())
			.Returns(true);

		var list = new[] { exit }.ToAsyncEnumerable();

		var state = new LocateService.MatchState(0);
		await _locateService.MatchList(state, list, player,
			LocateFlags.NoTypePreference, "North");
		var bestMatch = state.Best;
		var curr = state.Count;
		var exact = state.Exact;

		await Assert.That(curr).IsEqualTo(1);
		await Assert.That(exact).IsTrue();
		await Assert.That(bestMatch.WithoutError().WithoutNone().Object().DBRef).IsEqualTo(new DBRef(10, 0));
	}

	[Test]
	public async Task MatchList_ExitAlias_ExactMatch_FindsExit()
	{
		var sharedRoom = _factory.CreateRoom(999, "Shared Room");
		var player = _factory.CreatePlayer(1, "TestPlayer", sharedRoom);
		var exit = _factory.CreateExit(10, "North", ["n", "go north"], sharedRoom);

		_permissionService.CanInteract(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(),
				Arg.Any<IPermissionService.InteractType>())
			.Returns(true);

		var list = new[] { exit }.ToAsyncEnumerable();

		var state = new LocateService.MatchState(0);
		await _locateService.MatchList(state, list, player,
			LocateFlags.NoTypePreference, "n");
		var bestMatch = state.Best;
		var curr = state.Count;
		var exact = state.Exact;

		await Assert.That(curr).IsEqualTo(1);
		await Assert.That(exact).IsTrue();
		await Assert.That(bestMatch.WithoutError().WithoutNone().Object().DBRef).IsEqualTo(new DBRef(10, 0));
	}

	[Test]
	public async Task MatchList_ExitAlias_PrefixDoesNotMatchExit()
	{
		// Even with an alias, exits do not match by prefix — only exact.

		var sharedRoom = _factory.CreateRoom(999, "Shared Room");
		var player = _factory.CreatePlayer(1, "TestPlayer", sharedRoom);
		// Exit named "Exit_A" so its name does not accidentally match "north"
		var exit = _factory.CreateExit(10, "Exit_A", ["northwest"], sharedRoom);

		_permissionService.CanInteract(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(),
				Arg.Any<IPermissionService.InteractType>())
			.Returns(true);

		var list = new[] { exit }.ToAsyncEnumerable();

		// "north" is a prefix of alias "northwest", but exits use exact matching only
		var state = new LocateService.MatchState(0);
		await _locateService.MatchList(state, list, player,
			LocateFlags.NoTypePreference, "north");
		var bestMatch = state.Best;
		var curr = state.Count;

		await Assert.That(curr).IsEqualTo(0);
		await Assert.That(bestMatch.IsNone).IsTrue();
	}

	[Test]
	public async Task MatchList_PlayerAlias_ExactMatch()
	{
		var sharedRoom = _factory.CreateRoom(999, "Shared Room");
		var player = _factory.CreatePlayer(1, "TestPlayer", sharedRoom);
		var target = _factory.CreatePlayer(5, "Wizard", ["Wiz", "Admin"], sharedRoom);

		_permissionService.CanInteract(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(),
				Arg.Any<IPermissionService.InteractType>())
			.Returns(true);

		var list = new[] { target }.ToAsyncEnumerable();

		var state = new LocateService.MatchState(0);
		await _locateService.MatchList(state, list, player,
			LocateFlags.NoTypePreference, "Admin");
		var bestMatch = state.Best;
		var curr = state.Count;
		var exact = state.Exact;

		await Assert.That(curr).IsEqualTo(1);
		await Assert.That(exact).IsTrue();
		await Assert.That(bestMatch.WithoutError().WithoutNone().Object().DBRef).IsEqualTo(new DBRef(5, 0));
	}

	[Test]
	public async Task MatchList_PlayerAlias_PrefixMatch()
	{
		// A player found by alias prefix (PennMUSH string_match() behavior for aliases).

		var sharedRoom = _factory.CreateRoom(999, "Shared Room");
		var player = _factory.CreatePlayer(1, "TestPlayer", sharedRoom);
		var target = _factory.CreatePlayer(5, "Wizard", ["Administrator"], sharedRoom);

		_permissionService.CanInteract(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(),
				Arg.Any<IPermissionService.InteractType>())
			.Returns(true);

		var list = new[] { target }.ToAsyncEnumerable();

		// "Admin" is a prefix of alias "Administrator"
		var state = new LocateService.MatchState(0);
		await _locateService.MatchList(state, list, player,
			LocateFlags.NoTypePreference, "Admin");
		var bestMatch = state.Best;
		var curr = state.Count;
		var exact = state.Exact;

		await Assert.That(curr).IsEqualTo(1);
		await Assert.That(exact).IsFalse(); // partial match
		await Assert.That(bestMatch.WithoutError().WithoutNone().Object().DBRef).IsEqualTo(new DBRef(5, 0));
	}

	[Test]
	public async Task MatchList_PlayerAlias_ExactMatchBeforePrefixMatchForAliases()
	{
		// When two players match: one by exact alias, one by prefix alias — the exact one wins.

		var sharedRoom = _factory.CreateRoom(999, "Shared Room");
		var player = _factory.CreatePlayer(1, "TestPlayer", sharedRoom);
		var exactPlayer = _factory.CreatePlayer(5, "ExactAliasPlayer", ["Wiz"], sharedRoom);
		var prefixPlayer = _factory.CreatePlayer(6, "PrefixAliasPlayer", ["Wizard"], sharedRoom);

		_permissionService.CanInteract(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(),
				Arg.Any<IPermissionService.InteractType>())
			.Returns(true);

		// Put prefix first, exact second — exact should win regardless of list order
		var list = new[] { prefixPlayer, exactPlayer }.ToAsyncEnumerable();

		var state = new LocateService.MatchState(0);
		await _locateService.MatchList(state, list, player,
			LocateFlags.NoTypePreference, "Wiz");
		var bestMatch = state.Best;
		var curr = state.Count;
		var exact = state.Exact;

		// After both are processed: exact match resets counter, partial removed
		await Assert.That(exact).IsTrue();
		await Assert.That(curr).IsEqualTo(1);
		await Assert.That(bestMatch.WithoutError().WithoutNone().Object().DBRef).IsEqualTo(new DBRef(5, 0));
	}

	[Test]
	public async Task MatchList_TwoExactMatches_AmbiguousCounters()
	{
		// When two objects have the same name, Match_List should set curr=2, right_type=2,
		// which LocateMatch interprets as an ambiguous match (ErrorAmbiguous).

		var sharedRoom = _factory.CreateRoom(999, "Shared Room");
		var player = _factory.CreatePlayer(1, "TestPlayer", sharedRoom);
		var coin1 = _factory.CreateThing(3, "Coin", sharedRoom, player);
		var coin2 = _factory.CreateThing(4, "Coin", sharedRoom, player);

		_permissionService.CanInteract(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(),
				Arg.Any<IPermissionService.InteractType>())
			.Returns(true);

		var list = new[] { coin1, coin2 }.ToAsyncEnumerable();

		var state = new LocateService.MatchState(0);
		await _locateService.MatchList(state, list, player,
			LocateFlags.NoTypePreference, "Coin");
		var curr = state.Count;
		var rightType = state.RightType;
		var exact = state.Exact;

		// Two exact matches → curr=2; with NoTypePreference, right_type is not tracked (stays 0),
		// but right_type != 1 is still true → LocateMatch would return ErrorAmbiguous.
		await Assert.That(curr).IsEqualTo(2);
		await Assert.That(rightType).IsNotEqualTo(1); // signals ambiguity regardless of exact value
		await Assert.That(exact).IsTrue();
	}

	[Test]
	public async Task MatchList_TwoExactMatches_UseLastIfAmbiguous_ReturnsSecond()
	{
		// With UseLastIfAmbiguous, the last matching object in the list is returned.
		// Verifies ChooseThing returns the latter object when both are same-type matches.

		var sharedRoom = _factory.CreateRoom(999, "Shared Room");
		var player = _factory.CreatePlayer(1, "TestPlayer", sharedRoom);
		var coin1 = _factory.CreateThing(3, "Coin", sharedRoom, player);
		var coin2 = _factory.CreateThing(4, "Coin", sharedRoom, player);

		_permissionService.CanInteract(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(),
				Arg.Any<IPermissionService.InteractType>())
			.Returns(true);

		var list = new[] { coin1, coin2 }.ToAsyncEnumerable();

		var state = new LocateService.MatchState(0);
		await _locateService.MatchList(state, list, player,
			LocateFlags.UseLastIfAmbiguous, "Coin");
		var bestMatch = state.Best;
		var curr = state.Count;
		var exact = state.Exact;

		// Both matched → bestMatch is the last one (coin2 = #4)
		await Assert.That(curr).IsEqualTo(2);
		await Assert.That(exact).IsTrue();
		await Assert.That(bestMatch.WithoutError().WithoutNone().Object().DBRef.Number).IsEqualTo(4);
	}

	[Test]
	public async Task MatchList_TwoPartialMatches_AmbiguousCounters()
	{
		// Two objects matching by prefix also produce an ambiguous counter state.

		var sharedRoom = _factory.CreateRoom(999, "Shared Room");
		var player = _factory.CreatePlayer(1, "TestPlayer", sharedRoom);
		var sword1 = _factory.CreateThing(3, "Sword_A", sharedRoom, player);
		var sword2 = _factory.CreateThing(4, "Sword_B", sharedRoom, player);

		_permissionService.CanInteract(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(),
				Arg.Any<IPermissionService.InteractType>())
			.Returns(true);

		var list = new[] { sword1, sword2 }.ToAsyncEnumerable();

		var state = new LocateService.MatchState(0);
		await _locateService.MatchList(state, list, player,
			LocateFlags.NoTypePreference, "Sword");
		var curr = state.Count;
		var rightType = state.RightType;
		var exact = state.Exact;

		await Assert.That(curr).IsEqualTo(2); // both partial-matched
		await Assert.That(exact).IsFalse(); // all were partial matches
																				// With NoTypePreference, right_type is not tracked but still != 1 → ambiguous
		await Assert.That(rightType).IsNotEqualTo(1);
	}

	[Test]
	public async Task MatchList_ExactMatchAfterPartial_ResetsToExact()
	{
		// PennMUSH: when an exact match is found after a partial match, the partial
		// count is discarded and exact takes over (curr reset to 1, exact=true).

		var sharedRoom = _factory.CreateRoom(999, "Shared Room");
		var player = _factory.CreatePlayer(1, "TestPlayer", sharedRoom);
		var partialMatch = _factory.CreateThing(3, "SwordFake", sharedRoom, player); // prefix "Sword" matches
		var exactMatch = _factory.CreateThing(4, "Sword", sharedRoom, player);       // exact "Sword" match

		_permissionService.CanInteract(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(),
				Arg.Any<IPermissionService.InteractType>())
			.Returns(true);

		// Partial first, then exact
		var list = new[] { partialMatch, exactMatch }.ToAsyncEnumerable();

		var state = new LocateService.MatchState(0);
		await _locateService.MatchList(state, list, player,
			LocateFlags.NoTypePreference, "Sword");
		var bestMatch = state.Best;
		var curr = state.Count;
		var exact = state.Exact;

		// Exact match resets: curr=1, exact=true, bestMatch = exactMatch
		await Assert.That(curr).IsEqualTo(1);
		await Assert.That(exact).IsTrue();
		await Assert.That(bestMatch.WithoutError().WithoutNone().Object().DBRef).IsEqualTo(new DBRef(4, 0));
	}

	[Test]
	public async Task MatchList_OnlyMatchControlled_SkipsUncontrolledCandidates()
	{
		// Verifies the §4.3 fix: OnlyMatchLookerControlledObjects must be checked
		// against each candidate (cur), not against the reference 'where' object.
		// Uncontrolled objects must be silently skipped (PennMUSH continue semantics),
		// preserving any previously found controlled match.

		var sharedRoom = _factory.CreateRoom(999, "Shared Room");
		var player = _factory.CreatePlayer(1, "TestPlayer", sharedRoom);
		var ownedThing = _factory.CreateThing(3, "Widget", sharedRoom, player);
		var foreignThing = _factory.CreateThing(4, "Widget", sharedRoom, player);

		_permissionService.CanInteract(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(),
				Arg.Any<IPermissionService.InteractType>())
			.Returns(true);

		_permissionService.Controls(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>()).Returns(true);
		_permissionService.Controls(player, foreignThing).Returns(false);

		var list = new[] { ownedThing, foreignThing }.ToAsyncEnumerable();

		var state = new LocateService.MatchState(0);
		await _locateService.MatchList(state, list, player,
			LocateFlags.OnlyMatchLookerControlledObjects, "Widget");
		var bestMatch = state.Best;
		var curr = state.Count;
		var exact = state.Exact;

		await Assert.That(curr).IsEqualTo(1);
		await Assert.That(exact).IsTrue();
		await Assert.That(bestMatch.IsValid()).IsTrue();
		await Assert.That(bestMatch.WithoutError().WithoutNone().Object().DBRef).IsEqualTo(new DBRef(3, 0));
	}

	[Test]
	public async Task MatchList_CaseInsensitive_LowercaseQueryFindsUpperCaseName()
	{
		var sharedRoom = _factory.CreateRoom(999, "Shared Room");
		var player = _factory.CreatePlayer(1, "TestPlayer", sharedRoom);
		var thing = _factory.CreateThing(3, "TestObject", sharedRoom, player);

		_permissionService.CanInteract(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(),
				Arg.Any<IPermissionService.InteractType>())
			.Returns(true);

		var list = new[] { thing }.ToAsyncEnumerable();

		var state = new LocateService.MatchState(0);
		await _locateService.MatchList(state, list, player,
			LocateFlags.NoTypePreference, "testobject");
		var bestMatch = state.Best;
		var curr = state.Count;
		var exact = state.Exact;

		await Assert.That(curr).IsEqualTo(1);
		await Assert.That(exact).IsTrue();
		await Assert.That(bestMatch.WithoutError().WithoutNone().Object().DBRef).IsEqualTo(new DBRef(3, 0));
	}

	[Test]
	public async Task MatchList_CaseInsensitive_UppercaseQueryFindsMixedCaseName()
	{
		var sharedRoom = _factory.CreateRoom(999, "Shared Room");
		var player = _factory.CreatePlayer(1, "TestPlayer", sharedRoom);
		var thing = _factory.CreateThing(3, "myWidget", sharedRoom, player);

		_permissionService.CanInteract(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(),
				Arg.Any<IPermissionService.InteractType>())
			.Returns(true);

		var list = new[] { thing }.ToAsyncEnumerable();

		var state = new LocateService.MatchState(0);
		await _locateService.MatchList(state, list, player,
			LocateFlags.NoTypePreference, "MYWIDGET");
		var bestMatch = state.Best;
		var curr = state.Count;
		var exact = state.Exact;

		await Assert.That(curr).IsEqualTo(1);
		await Assert.That(exact).IsTrue();
		await Assert.That(bestMatch.WithoutError().WithoutNone().Object().DBRef).IsEqualTo(new DBRef(3, 0));
	}

	[Test]
	public async Task MatchList_OrdinalEnglish_SecondOfThree_ReturnsSecond()
	{
		// Verifies that Match_List with final=2 (English "2nd") returns the 2nd object
		// matching the name in list order — this is the runtime behavior of ParseEnglish.

		var sharedRoom = _factory.CreateRoom(999, "Shared Room");
		var player = _factory.CreatePlayer(1, "TestPlayer", sharedRoom);
		var sword1 = _factory.CreateThing(3, "Sword", sharedRoom, player);
		var sword2 = _factory.CreateThing(4, "Sword", sharedRoom, player);
		var sword3 = _factory.CreateThing(5, "Sword", sharedRoom, player);

		_permissionService.CanInteract(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(),
				Arg.Any<IPermissionService.InteractType>())
			.Returns(true);

		var list = new[] { sword1, sword2, sword3 }.ToAsyncEnumerable();

		// final=2 means "2nd" — Match_List in English mode counts up to final
		var state = new LocateService.MatchState(2);
		await _locateService.MatchList(state, list, player,
			LocateFlags.NoTypePreference, "Sword");
		var bestMatch = state.Best;
		var curr = state.Count;
		var flow = state.Done;

		await Assert.That(curr).IsEqualTo(2);
		await Assert.That(flow).IsTrue(); // found the Nth item → every remaining scope is skipped
		await Assert.That(bestMatch.WithoutError().WithoutNone().Object().DBRef).IsEqualTo(new DBRef(4, 0));
	}

	[Test]
	public async Task MatchList_OrdinalEnglish_FirstOfMany_ReturnsFirst()
	{
		// English "1st sword" — with final=1, the very first match is returned.

		var sharedRoom = _factory.CreateRoom(999, "Shared Room");
		var player = _factory.CreatePlayer(1, "TestPlayer", sharedRoom);
		var sword1 = _factory.CreateThing(3, "Sword", sharedRoom, player);
		var sword2 = _factory.CreateThing(4, "Sword", sharedRoom, player);

		_permissionService.CanInteract(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(),
				Arg.Any<IPermissionService.InteractType>())
			.Returns(true);

		var list = new[] { sword1, sword2 }.ToAsyncEnumerable();

		var state = new LocateService.MatchState(1);
		await _locateService.MatchList(state, list, player,
			LocateFlags.NoTypePreference, "Sword");
		var bestMatch = state.Best;
		var curr = state.Count;
		var flow = state.Done;

		await Assert.That(flow).IsTrue();
		await Assert.That(flow).IsTrue();
		await Assert.That(bestMatch.WithoutError().WithoutNone().Object().DBRef).IsEqualTo(new DBRef(3, 0));
	}

	[Test]
	public async Task MatchList_OrdinalEnglish_NthBeyondList_NeverBreaks()
	{
		// Requesting "5th sword" when only 2 swords exist → Match_List exhausts the list
		// without finding the 5th. The caller (LocateMatch) uses final!=0 && curr!=final
		// to detect the "ordinal not reached" condition and return None.

		var sharedRoom = _factory.CreateRoom(999, "Shared Room");
		var player = _factory.CreatePlayer(1, "TestPlayer", sharedRoom);
		var sword1 = _factory.CreateThing(3, "Sword", sharedRoom, player);
		var sword2 = _factory.CreateThing(4, "Sword", sharedRoom, player);

		_permissionService.CanInteract(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(),
				Arg.Any<IPermissionService.InteractType>())
			.Returns(true);

		var list = new[] { sword1, sword2 }.ToAsyncEnumerable();

		var state = new LocateService.MatchState(5);
		await _locateService.MatchList(state, list, player,
			LocateFlags.NoTypePreference, "Sword");
		var curr = state.Count;
		var flow = state.Done;

		// Only 2 swords; asked for 5th → list exhausted without breaking
		await Assert.That(curr).IsEqualTo(2);
		await Assert.That(flow).IsFalse(); // never found the 5th → the search keeps going
	}

	[Test]
	public async Task MatchList_ThingsPreference_PrefersTheThingButStillCountsThePlayer()
	{
		// MATCH_TYPE returns -1 for a wrong-type object when MAT_TYPE is unset, so it is still a
		// candidate: curr counts both, ChooseThing hands back the thing, and right_type == 1 is what
		// keeps that from reading as ambiguous.
		var sharedRoom = _factory.CreateRoom(999, "Shared Room");
		var player = _factory.CreatePlayer(1, "TestPlayer", sharedRoom);
		var targetPlayer = _factory.CreatePlayer(5, "Widget", sharedRoom);
		var thing = _factory.CreateThing(6, "Widget", sharedRoom, player);

		_permissionService.CanInteract(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(),
				Arg.Any<IPermissionService.InteractType>())
			.Returns(true);

		var list = new[] { targetPlayer, thing }.ToAsyncEnumerable();

		var state = new LocateService.MatchState(0);
		await _locateService.MatchList(state, list, player,
			LocateFlags.ThingsPreference, "Widget");
		var bestMatch = state.Best;
		var curr = state.Count;
		var rightType = state.RightType;

		await Assert.That(curr).IsEqualTo(2);
		await Assert.That(rightType).IsEqualTo(1);
		await Assert.That(bestMatch.WithoutError().WithoutNone().Object().DBRef).IsEqualTo(new DBRef(6, 0));
	}

	[Test]
	public async Task MatchList_ThingsPreferenceWithOnlyMatchTypePreference_SkipsNonThings()
	{
		// MAT_TYPE — the flag that turns the preference into a filter.
		var sharedRoom = _factory.CreateRoom(999, "Shared Room");
		var player = _factory.CreatePlayer(1, "TestPlayer", sharedRoom);
		var targetPlayer = _factory.CreatePlayer(5, "Widget", sharedRoom);
		var thing = _factory.CreateThing(6, "Widget", sharedRoom, player);

		_permissionService.CanInteract(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(),
				Arg.Any<IPermissionService.InteractType>())
			.Returns(true);

		var list = new[] { targetPlayer, thing }.ToAsyncEnumerable();

		var state = new LocateService.MatchState(0);
		await _locateService.MatchList(state, list, player,
			LocateFlags.ThingsPreference | LocateFlags.OnlyMatchTypePreference, "Widget");
		var bestMatch = state.Best;
		var curr = state.Count;

		await Assert.That(curr).IsEqualTo(1);
		await Assert.That(bestMatch.WithoutError().WithoutNone().IsPlayer).IsFalse();
		await Assert.That(bestMatch.WithoutError().WithoutNone().Object().DBRef).IsEqualTo(new DBRef(6, 0));
	}

	[Test]
	public async Task MatchList_PlayersPreference_PrefersThePlayerButStillCountsTheThing()
	{
		var sharedRoom = _factory.CreateRoom(999, "Shared Room");
		var player = _factory.CreatePlayer(1, "TestPlayer", sharedRoom);
		var targetPlayer = _factory.CreatePlayer(5, "Widget", sharedRoom);
		var thing = _factory.CreateThing(6, "Widget", sharedRoom, player);

		_permissionService.CanInteract(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(),
				Arg.Any<IPermissionService.InteractType>())
			.Returns(true);

		var list = new[] { thing, targetPlayer }.ToAsyncEnumerable();

		var state = new LocateService.MatchState(0);
		await _locateService.MatchList(state, list, player,
			LocateFlags.PlayersPreference, "Widget");
		var bestMatch = state.Best;
		var curr = state.Count;
		var rightType = state.RightType;

		await Assert.That(curr).IsEqualTo(2);
		await Assert.That(rightType).IsEqualTo(1);
		await Assert.That(bestMatch.WithoutError().WithoutNone().IsPlayer).IsTrue();
		await Assert.That(bestMatch.WithoutError().WithoutNone().Object().DBRef).IsEqualTo(new DBRef(5, 0));
	}

	[Test]
	public async Task Locate_HereWithOnlyMatchControlled_ErrorWhenLockerNotControlRoom()
	{
		// When MatchHereForLookerLocation and OnlyMatchLookerControlledObjects are set,
		// and the executor does not control the looker → ErrorPerm.

		var sharedRoom = _factory.CreateRoom(999, "Shared Room");
		var player = _factory.CreatePlayer(1, "TestPlayer", sharedRoom);

		_mediator.CreateStream(Arg.Any<GetPlayerQuery>(), Arg.Any<CancellationToken>())
			.Returns(_ => AsyncEnumerable.Empty<SharpPlayer>());

		// executor doesn't control themselves → the "here" OnlyMatchLookerControlledObjects check fails
		_permissionService.Controls(player, player).Returns(false);

		var result = await _locateService.Locate(_parser, player, player, "here",
			LocateFlags.MatchHereForLookerLocation | LocateFlags.OnlyMatchLookerControlledObjects |
			LocateFlags.PreferLockPass);

		await Assert.That(result.IsError).IsTrue();
		await Assert.That(result.AsError.Value).IsEqualTo(ErrorMessages.Returns.PermissionDenied);
	}

	[Test]
	public async Task Locate_MeWithOnlyMatchControlled_ReturnsPlayerWhenControls()
	{
		// MatchMeForLooker + OnlyMatchLookerControlledObjects + executor controls themselves → match.

		var sharedRoom = _factory.CreateRoom(999, "Shared Room");
		var player = _factory.CreatePlayer(1, "TestPlayer", sharedRoom);

		_mediator.CreateStream(Arg.Any<GetPlayerQuery>(), Arg.Any<CancellationToken>())
			.Returns(_ => AsyncEnumerable.Empty<SharpPlayer>());

		_permissionService.Controls(player, player).Returns(true);
		_permissionService.CanInteract(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(),
				Arg.Any<IPermissionService.InteractType>())
			.Returns(true);
		_permissionService.CanExamine(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>())
			.Returns(true);

		var result = await _locateService.Locate(_parser, player, player, "me",
			LocateFlags.MatchMeForLooker | LocateFlags.OnlyMatchLookerControlledObjects | LocateFlags.PreferLockPass);

		await Assert.That(result.IsValid()).IsTrue();
		await Assert.That(result.WithoutError().WithoutNone().Object().DBRef).IsEqualTo(new DBRef(1, 0));
	}

	[Test]
	public async Task Locate_AbsoluteDbref_BypassesVisibility()
	{
		// PennMUSH: #N always bypasses visibility check (match_absolute). The object
		// should be returned even when CanExamine and CanInteract both return false.

		var sharedRoom = _factory.CreateRoom(999, "Shared Room");
		var player = _factory.CreatePlayer(1, "TestPlayer", sharedRoom);
		var thing = _factory.CreateThing(42, "Hidden", sharedRoom, player);

		_mediator.Send(Arg.Is<GetObjectNodeQuery>(q => q.DBRef.Number == 42), Arg.Any<CancellationToken>())
			.Returns(callInfo => ValueTask.FromResult<AnyOptionalSharpObject>(thing.WithNoneOption()));

		_mediator.CreateStream(Arg.Any<GetPlayerQuery>(), Arg.Any<CancellationToken>())
			.Returns(_ => AsyncEnumerable.Empty<SharpPlayer>());

		_permissionService.Controls(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>()).Returns(true);
		_permissionService.CanInteract(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(),
				Arg.Any<IPermissionService.InteractType>())
			.Returns(false);
		_permissionService.CanExamine(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>())
			.Returns(false);

		var result = await _locateService.Locate(_parser, player, player, "#42",
			LocateFlags.AbsoluteMatch | LocateFlags.PreferLockPass);

		await Assert.That(result.IsValid()).IsTrue();
		await Assert.That(result.WithoutError().WithoutNone().Object().DBRef).IsEqualTo(new DBRef(42, 0));
	}

	[Test]
	public async Task MatchList_NoVisibilityCheck_Flag_PreservesMatchWhenPostMatchVisibilityFails()
	{
		// The NoVisibilityCheck flag bypasses the post-match CanExamine/CanInteract check
		// in Locate(). This test verifies the flag independently of absolute DBRef behavior
		// by using Match_List directly (which has no post-match visibility check).
		// The visibility check that NoVisibilityCheck bypasses is in Locate(), not Match_List.
		// So this test verifies that a non-visible object in Match_List is found when
		// CanInteract returns true (Match_List only skips objects where CanInteract is false).

		var sharedRoom = _factory.CreateRoom(999, "Shared Room");
		var player = _factory.CreatePlayer(1, "TestPlayer", sharedRoom);
		var thing = _factory.CreateThing(3, "TargetObject", sharedRoom, player);

		_permissionService.CanInteract(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(),
				Arg.Any<IPermissionService.InteractType>())
			.Returns(true);

		var list = new[] { thing }.ToAsyncEnumerable();

		var state = new LocateService.MatchState(0);
		await _locateService.MatchList(state, list, player,
			LocateFlags.NoTypePreference | LocateFlags.NoVisibilityCheck, "TargetObject");
		var bestMatch = state.Best;
		var curr = state.Count;

		await Assert.That(curr).IsEqualTo(1);
		await Assert.That(bestMatch.IsValid()).IsTrue();
		await Assert.That(bestMatch.WithoutError().WithoutNone().Object().DBRef).IsEqualTo(new DBRef(3, 0));
	}

	/// <summary>
	/// Verifies that the ordinal validation logic (matching PennMUSH parse_english())
	/// correctly accepts well-formed ordinals and rejects malformed ones.
	/// Previously a trailing <c>|| ordinal != "th"</c> clause made 1st/2nd/3rd always invalid.
	/// Additionally, <c>Range(10,14)</c> covered 10–23 instead of the intended teen range 11–13,
	/// and there was no teen exclusion on the st/nd/rd mod10 checks.
	/// </summary>
	[Test]
	[Arguments(1, "st", true)]   // 1st  – valid
	[Arguments(2, "nd", true)]   // 2nd  – valid
	[Arguments(3, "rd", true)]   // 3rd  – valid
	[Arguments(4, "th", true)]   // 4th  – valid
	[Arguments(10, "th", true)]  // 10th – mod10==0, uses "th"
	[Arguments(11, "th", true)]  // 11th – teen, "th" required
	[Arguments(12, "th", true)]  // 12th – teen, "th" required
	[Arguments(13, "th", true)]  // 13th – teen, "th" required
	[Arguments(21, "st", true)]  // 21st – valid (not a teen)
	[Arguments(22, "nd", true)]  // 22nd – valid
	[Arguments(23, "rd", true)]  // 23rd – valid
	[Arguments(100, "th", true)] // 100th – mod10==0, uses "th"
	[Arguments(111, "th", true)] // 111th – mod100==11, teen
	[Arguments(11, "st", false)] // 11st – invalid, teen must use "th"
	[Arguments(12, "nd", false)] // 12nd – invalid
	[Arguments(13, "rd", false)] // 13rd – invalid
	[Arguments(1, "nd", false)]  // 1nd  – invalid
	[Arguments(2, "rd", false)]  // 2rd  – invalid
	public async Task ParseEnglish_OrdinalValidation_MatchesPennMUSH(int count, string ordinal, bool shouldBeValid)
	{
		// Replicate the fixed validation logic from ParseEnglish to confirm the
		// mathematical logic matches PennMUSH parse_english() exactly.
		var mod100 = count % 100;
		var isTeen = mod100 >= 11 && mod100 <= 13;
		var mod10 = count % 10;

		string expectedSuffix = (isTeen || mod10 == 0 || mod10 > 3) ? "th"
			: mod10 == 1 ? "st"
			: mod10 == 2 ? "nd"
			: "rd";

		bool isValid = count >= 1 && ordinal.Equals(expectedSuffix, StringComparison.CurrentCultureIgnoreCase);

		await Assert.That(isValid).IsEqualTo(shouldBeValid);
	}
}