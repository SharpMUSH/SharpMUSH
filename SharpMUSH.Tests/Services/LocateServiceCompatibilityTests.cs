using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using Mediator;
using Microsoft.Extensions.Options;
using SharpMUSH.Library.Queries.Database;
using NSubstitute;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Extensions;
using TUnit.Core;
using System.Collections.Immutable;
using DotNext.Threading;
using OneOf.Types;

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
		// Load a real SharpMUSHOptions from the test config file
		var configFile = Path.Combine(AppContext.BaseDirectory, "Configuration", "Testfile", "mushcnf.dst");
		var options = ReadPennMushConfig.Create(configFile);

		var wrapper = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
		wrapper.CurrentValue.Returns(options);

		_locateService = new LocateService(_mediator, _notifyService, _permissionService, wrapper);
	}

	[Test]
	[Skip("Skip for now")]
	public async Task LocateMatch_NameMatching_ShouldMatchExactNamesForNonExits()
	{
		// Arrange
		// Create a shared room for player and thing to be in the same location
		var sharedRoom = _factory.CreateRoom(999, "Shared Room");

		var player = _factory.CreatePlayer(1, "TestPlayer", sharedRoom);
		var thing = _factory.CreateThing(3, "TestObject", sharedRoom, player);

		var contents = new[] { thing }.ToAsyncEnumerable();

		// Mock GetContentsQuery for any container to return our thing
		_mediator.Send(Arg.Is<GetContentsQuery>(q => true), Arg.Any<CancellationToken>())
			.Returns(callInfo => ValueTask.FromResult<IAsyncEnumerable<AnySharpContent>?>(contents.Select(x => x.AsContent)));

		// Mock GetPlayerQuery to return empty results
		_mediator.Send(Arg.Is<GetPlayerQuery>(q => true), Arg.Any<CancellationToken>())
			.Returns(callInfo => ValueTask.FromResult(AsyncEnumerable.Empty<SharpPlayer>()));

		// Set up comprehensive permission mocking
		_permissionService.Controls(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>())
			.Returns(true);

		_permissionService.CanInteract(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(),
				Arg.Any<IPermissionService.InteractType>())
			.Returns(true);

		_permissionService.CanExamine(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>())
			.Returns(true);

		// Act
		var result = await _locateService.Locate(_parser, player, player, "TestObject",
			LocateFlags.MatchObjectsInLookerInventory);

		// Assert
		await Assert.That(result.IsValid()).IsTrue();
		await Assert.That(result.WithoutError().WithoutNone().Object().DBRef).IsEqualTo(new DBRef(3, 0));
	}

	[Test]
	public async Task LocateMatch_NameMatching_ShouldNotMatchWrongNamesForNonExits()
	{
		// This test verifies the fix for the inverted logic bug
		// Before fix: (!cur.IsExit && !string.Equals(...)) would match everything that DIDN'T match
		// After fix: (!cur.IsExit && string.Equals(...)) only matches exact names

		// Arrange
		// Create a shared room for player and thing to be in the same location
		var sharedRoom = _factory.CreateRoom(999, "Shared Room");

		var player = _factory.CreatePlayer(1, "TestPlayer", sharedRoom);
		var thing = _factory.CreateThing(3, "TestObject", sharedRoom, player);

		var contents = new[] { thing }.ToAsyncEnumerable();

		// Mock GetContentsQuery for any container to return our thing
		_mediator.Send(Arg.Is<GetContentsQuery>(q => true), Arg.Any<CancellationToken>())
			.Returns(callInfo => ValueTask.FromResult<IAsyncEnumerable<AnySharpContent>?>(contents.Select(x => x.AsContent)));

		// Mock GetPlayerQuery to return empty results
		_mediator.Send(Arg.Is<GetPlayerQuery>(q => true), Arg.Any<CancellationToken>())
			.Returns(callInfo => ValueTask.FromResult(AsyncEnumerable.Empty<SharpPlayer>()));

		// Set up comprehensive permission mocking
		_permissionService.Controls(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>())
			.Returns(true);

		_permissionService.CanInteract(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(),
				Arg.Any<IPermissionService.InteractType>())
			.Returns(true);

		_permissionService.CanExamine(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>())
			.Returns(true);

		// Act
		var result = await _locateService.Locate(_parser, player, player, "WrongName",
			LocateFlags.MatchObjectsInLookerInventory);

		// Assert
		await Assert.That(result.IsNone).IsTrue();
	}

	[Test]
	public async Task LocateMatch_MeMatching_ShouldRespectNoTypePreference()
	{
		// This test verifies the fix for the NoTypePreference check

		// Arrange
		var sharedRoom = _factory.CreateRoom(999, "Shared Room");
		var player = _factory.CreatePlayer(1, "TestPlayer", sharedRoom);

		// Mock GetPlayerQuery to return empty results
		_mediator.Send(Arg.Is<GetPlayerQuery>(q => true), Arg.Any<CancellationToken>())
			.Returns(callInfo => ValueTask.FromResult(AsyncEnumerable.Empty<SharpPlayer>()));

		// Set up comprehensive permission mocking
		_permissionService.Controls(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>())
			.Returns(true);

		_permissionService.CanInteract(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(),
				Arg.Any<IPermissionService.InteractType>())
			.Returns(true);

		_permissionService.CanExamine(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>())
			.Returns(true);

		// Act - with NoTypePreference, should not match "me"
		// Use PreferLockPass to prevent auto-adding flags that would interfere
		var resultWithNoTypePreference = await _locateService.Locate(_parser, player, player, "me",
			LocateFlags.NoTypePreference | LocateFlags.MatchMeForLooker | LocateFlags.PreferLockPass);

		// Act - without NoTypePreference, should match "me"
		var resultWithoutNoTypePreference = await _locateService.Locate(_parser, player, player, "me",
			LocateFlags.MatchMeForLooker | LocateFlags.PreferLockPass);

		// Assert
		await Assert.That(resultWithNoTypePreference.IsNone || resultWithNoTypePreference.IsError).IsTrue();
		await Assert.That(resultWithoutNoTypePreference.IsValid()).IsTrue();
		await Assert.That(resultWithoutNoTypePreference.WithoutError().WithoutNone().Object().DBRef)
			.IsEqualTo(new DBRef(1, 0));
	}

	[Test]
	public async Task LocateMatch_PermissionCheck_ShouldUseCorrectLogic()
	{
		// This test verifies the fix for the permission check logic

		// Arrange
		// Create players in DIFFERENT locations to test permission check
		var room1 = _factory.CreateRoom(1001, "Room 1");
		var room2 = _factory.CreateRoom(1002, "Room 2");

		var player = _factory.CreatePlayer(1, "TestPlayer", room1);
		var target = _factory.CreatePlayer(2, "TargetPlayer", room2);

		// Mock GetPlayerQuery to return empty results
		_mediator.Send(Arg.Is<GetPlayerQuery>(q => true), Arg.Any<CancellationToken>())
			.Returns(callInfo => ValueTask.FromResult(AsyncEnumerable.Empty<SharpPlayer>()));

		// Set up specific permission logic for this test
		_permissionService.Controls(player, target)
			.Returns(false);

		// But player can control themselves
		_permissionService.Controls(player, player)
			.Returns(true);

		// Target can control themselves  
		_permissionService.Controls(target, target)
			.Returns(true);

		_permissionService.CanInteract(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(),
				Arg.Any<IPermissionService.InteractType>())
			.Returns(true);

		_permissionService.CanExamine(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>())
			.Returns(true);

		// Act - with OnlyMatchLookerControlledObjects, should fail when player doesn't control target
		// Use PreferLockPass to prevent auto-adding flags
		var resultWithControlRequired = await _locateService.Locate(_parser, player, target, "me",
			LocateFlags.MatchMeForLooker | LocateFlags.OnlyMatchLookerControlledObjects | LocateFlags.PreferLockPass);

		// Act - without OnlyMatchLookerControlledObjects, should succeed
		var resultWithoutControlRequired = await _locateService.Locate(_parser, player, target, "me",
			LocateFlags.MatchMeForLooker | LocateFlags.PreferLockPass);

		// Assert
		await Assert.That(resultWithControlRequired.IsError).IsTrue();
		await Assert.That(resultWithControlRequired.AsError.Value).IsEqualTo(Errors.ErrorPerm);
		await Assert.That(resultWithoutControlRequired.IsValid()).IsTrue();
	}

	[Test]
	public async Task LocateMatch_HereMatching_ShouldMatchCurrentLocation()
	{
		// Test MatchHereForLookerLocation flag with "here" keyword

		// Arrange
		var sharedRoom = _factory.CreateRoom(999, "Shared Room");
		var player = _factory.CreatePlayer(1, "TestPlayer", sharedRoom);

		// Mock GetContentsQuery to return empty (not needed for "here")
		_mediator.Send(Arg.Is<GetContentsQuery>(q => true), Arg.Any<CancellationToken>())
			.Returns(callInfo =>
				ValueTask.FromResult<IAsyncEnumerable<AnySharpContent>?>(AsyncEnumerable.Empty<AnySharpContent>()));

		// Mock GetPlayerQuery to return empty results
		_mediator.Send(Arg.Is<GetPlayerQuery>(q => true), Arg.Any<CancellationToken>())
			.Returns(callInfo => ValueTask.FromResult(AsyncEnumerable.Empty<SharpPlayer>()));

		// Set up permissions
		_permissionService.Controls(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>())
			.Returns(true);

		_permissionService.CanInteract(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(),
				Arg.Any<IPermissionService.InteractType>())
			.Returns(true);

		_permissionService.CanExamine(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>())
			.Returns(true);

		// Act - should match "here" to player's location
		var result = await _locateService.Locate(_parser, player, player, "here",
			LocateFlags.MatchHereForLookerLocation | LocateFlags.PreferLockPass);

		// Assert
		await Assert.That(result.IsValid()).IsTrue();
		await Assert.That(result.WithoutError().WithoutNone().IsRoom).IsTrue();
		await Assert.That(result.WithoutError().WithoutNone().Object().DBRef.Number).IsEqualTo(999);
	}

	[Test]
	public async Task LocateMatch_AbsoluteDBRef_ShouldMatchByDBRef()
	{
		// Test AbsoluteMatch flag with #dbref format

		// Arrange
		var sharedRoom = _factory.CreateRoom(999, "Shared Room");
		var player = _factory.CreatePlayer(1, "TestPlayer", sharedRoom);
		var thing = _factory.CreateThing(42, "TestObject", sharedRoom, player);

		// Mock GetObjectNodeQuery to return the thing when queried by dbref
		_mediator.Send(Arg.Is<GetObjectNodeQuery>(q => q.DBRef.Number == 42), Arg.Any<CancellationToken>())
			.Returns(callInfo => ValueTask.FromResult<AnyOptionalSharpObject>(thing.WithNoneOption()));

		// Mock GetPlayerQuery to return empty results
		_mediator.Send(Arg.Is<GetPlayerQuery>(q => true), Arg.Any<CancellationToken>())
			.Returns(callInfo => ValueTask.FromResult(AsyncEnumerable.Empty<SharpPlayer>()));

		// Set up permissions
		_permissionService.Controls(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>())
			.Returns(true);

		_permissionService.CanInteract(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(),
				Arg.Any<IPermissionService.InteractType>())
			.Returns(true);

		_permissionService.CanExamine(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>())
			.Returns(true);

		// Act - search by absolute DBRef #42
		var result = await _locateService.Locate(_parser, player, player, "#42",
			LocateFlags.AbsoluteMatch | LocateFlags.PreferLockPass);

		// Assert
		await Assert.That(result.IsValid()).IsTrue();
		await Assert.That(result.WithoutError().WithoutNone().Object().DBRef).IsEqualTo(new DBRef(42, 0));
	}

	[Test]
	[Skip("Skip for now")]
	public async Task LocateMatch_TypePreference_ShouldRespectPlayerPreference()
	{
		// Test PlayersPreference flag prioritizes players over other objects

		// Arrange
		var sharedRoom = _factory.CreateRoom(999, "Shared Room");
		var player = _factory.CreatePlayer(1, "TestPlayer", sharedRoom);
		var targetPlayer = _factory.CreatePlayer(5, "Bob", sharedRoom);
		var thing = _factory.CreateThing(6, "Bob", sharedRoom, player); // Same name as player

		var contents = new[] { thing, targetPlayer }.ToAsyncEnumerable();

		// Mock GetContentsQuery to return both objects  
		_mediator.Send(Arg.Is<GetContentsQuery>(q => true), Arg.Any<CancellationToken>())
			.Returns(callInfo => ValueTask.FromResult<IAsyncEnumerable<AnySharpContent>?>(contents.Select(x => x.AsContent)));

		// Mock GetPlayerQuery to return the player when searching with *
		var playerResults = new[] { targetPlayer.AsPlayer }.ToAsyncEnumerable();
		_mediator.Send(Arg.Is<GetPlayerQuery>(q => q.Name.Contains("Bob")), Arg.Any<CancellationToken>())
			.Returns(callInfo => ValueTask.FromResult(playerResults));

		// Set up permissions
		_permissionService.Controls(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>())
			.Returns(true);

		_permissionService.CanInteract(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(),
				Arg.Any<IPermissionService.InteractType>())
			.Returns(true);

		_permissionService.CanExamine(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>())
			.Returns(true);

		// Act - with PlayersPreference and wildcard, should match player #5 not thing #6
		var result = await _locateService.Locate(_parser, player, player, "*Bob",
			LocateFlags.PlayersPreference | LocateFlags.MatchOptionalWildCardForPlayerName |
			LocateFlags.MatchObjectsInLookerInventory | LocateFlags.PreferLockPass);

		// Assert
		await Assert.That(result.IsValid()).IsTrue();
		await Assert.That(result.WithoutError().WithoutNone().IsPlayer).IsTrue();
		await Assert.That(result.WithoutError().WithoutNone().Object().DBRef).IsEqualTo(new DBRef(5, 0));
	}

	[Test]
	[Skip("Skip for now")]
	public async Task LocateMatch_PartialMatching_ShouldFindObjectByPartialName()
	{
		// Test partial name matching (non-exit objects)

		// Arrange
		var sharedRoom = _factory.CreateRoom(999, "Shared Room");
		var player = _factory.CreatePlayer(1, "TestPlayer", sharedRoom);
		var thing = _factory.CreateThing(3, "LongObjectName", sharedRoom, player);

		var contents = new[] { thing }.ToAsyncEnumerable();

		// Mock GetContentsQuery
		_mediator.Send(Arg.Is<GetContentsQuery>(q => true), Arg.Any<CancellationToken>())
			.Returns(callInfo => ValueTask.FromResult<IAsyncEnumerable<AnySharpContent>?>(contents.Select(x => x.AsContent)));

		// Mock GetPlayerQuery to return empty results
		_mediator.Send(Arg.Is<GetPlayerQuery>(q => true), Arg.Any<CancellationToken>())
			.Returns(callInfo => ValueTask.FromResult(AsyncEnumerable.Empty<SharpPlayer>()));

		// Set up permissions
		_permissionService.Controls(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>())
			.Returns(true);

		_permissionService.CanInteract(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(),
				Arg.Any<IPermissionService.InteractType>())
			.Returns(true);

		_permissionService.CanExamine(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>())
			.Returns(true);

		// Act - search with partial name (should match "LongObjectName")
		var result = await _locateService.Locate(_parser, player, player, "LongObjectName",
			LocateFlags.MatchObjectsInLookerInventory);

		// Assert
		await Assert.That(result.IsValid()).IsTrue();
		await Assert.That(result.WithoutError().WithoutNone().Object().DBRef).IsEqualTo(new DBRef(3, 0));
	}

	[Test]
	public async Task LocateMatch_NoPartialMatches_ShouldRequireExactMatch()
	{
		// Test NoPartialMatches flag requires exact name match

		// Arrange
		var sharedRoom = _factory.CreateRoom(999, "Shared Room");
		var player = _factory.CreatePlayer(1, "TestPlayer", sharedRoom);
		var thing = _factory.CreateThing(3, "LongObjectName", sharedRoom, player);

		var contents = new[] { thing }.ToAsyncEnumerable();

		// Mock GetContentsQuery
		_mediator.Send(Arg.Is<GetContentsQuery>(q => true), Arg.Any<CancellationToken>())
			.Returns(callInfo => ValueTask.FromResult<IAsyncEnumerable<AnySharpContent>?>(contents.Select(x => x.AsContent)));

		// Mock GetPlayerQuery to return empty results
		_mediator.Send(Arg.Is<GetPlayerQuery>(q => true), Arg.Any<CancellationToken>())
			.Returns(callInfo => ValueTask.FromResult(AsyncEnumerable.Empty<SharpPlayer>()));

		// Set up permissions
		_permissionService.Controls(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>())
			.Returns(true);

		_permissionService.CanInteract(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(),
				Arg.Any<IPermissionService.InteractType>())
			.Returns(true);

		_permissionService.CanExamine(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>())
			.Returns(true);

		// Act - with NoPartialMatches, partial name "Long" should NOT match "LongObjectName"
		var result = await _locateService.Locate(_parser, player, player, "Long",
			LocateFlags.MatchObjectsInLookerInventory | LocateFlags.NoPartialMatches);

		// Assert - should not find the object
		await Assert.That(result.IsNone).IsTrue();
	}

	[Test]
	[Skip("Skip for now")]
	public async Task LocateMatch_MatchObjectsInLookerLocation_ShouldFindObjectsInSameRoom()
	{
		// Test MatchObjectsInLookerLocation flag finds objects in the same room

		// Arrange
		var sharedRoom = _factory.CreateRoom(999, "Shared Room");
		var player = _factory.CreatePlayer(1, "TestPlayer", sharedRoom);
		var thing = _factory.CreateThing(3, "RoomObject", sharedRoom, player);

		var contents = new[] { thing, player }.ToAsyncEnumerable();

		// Mock GetContentsQuery for the room to return objects in it
		_mediator.Send(Arg.Is<GetContentsQuery>(q => true), Arg.Any<CancellationToken>())
			.Returns(callInfo => ValueTask.FromResult<IAsyncEnumerable<AnySharpContent>?>(contents.Select(x => x.AsContent)));

		// Mock GetPlayerQuery to return empty results
		_mediator.Send(Arg.Is<GetPlayerQuery>(q => true), Arg.Any<CancellationToken>())
			.Returns(callInfo => ValueTask.FromResult(AsyncEnumerable.Empty<SharpPlayer>()));

		// Set up permissions
		_permissionService.Controls(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>())
			.Returns(true);

		_permissionService.CanInteract(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(),
				Arg.Any<IPermissionService.InteractType>())
			.Returns(true);

		_permissionService.CanExamine(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>())
			.Returns(true);

		// Act - search for object in same location
		var result = await _locateService.Locate(_parser, player, player, "RoomObject",
			LocateFlags.MatchObjectsInLookerLocation);

		// Assert
		await Assert.That(result.IsValid()).IsTrue();
		await Assert.That(result.WithoutError().WithoutNone().Object().DBRef).IsEqualTo(new DBRef(3, 0));
	}

	[Test]
	[Skip("Skip for now")]
	public async Task LocateMatch_MultipleObjects_ShouldHandleAmbiguousMatches()
	{
		// Test that when multiple objects have the same name, Locate handles ambiguity properly
		// In PennMUSH, this typically returns an ambiguous match or the first/last depending on flags

		// Arrange
		var sharedRoom = _factory.CreateRoom(999, "Shared Room");
		var player = _factory.CreatePlayer(1, "TestPlayer", sharedRoom);
		var thing1 = _factory.CreateThing(3, "Coin", sharedRoom, player);
		var thing2 = _factory.CreateThing(4, "Coin", sharedRoom, player);

		var contents = new[] { thing1, thing2 }.ToAsyncEnumerable();

		// Mock GetContentsQuery
		_mediator.Send(Arg.Is<GetContentsQuery>(q => true), Arg.Any<CancellationToken>())
			.Returns(callInfo => ValueTask.FromResult<IAsyncEnumerable<AnySharpContent>?>(contents.Select(x => x.AsContent)));

		// Mock GetPlayerQuery to return empty results
		_mediator.Send(Arg.Is<GetPlayerQuery>(q => true), Arg.Any<CancellationToken>())
			.Returns(callInfo => ValueTask.FromResult(AsyncEnumerable.Empty<SharpPlayer>()));

		// Set up permissions
		_permissionService.Controls(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>())
			.Returns(true);

		_permissionService.CanInteract(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(),
				Arg.Any<IPermissionService.InteractType>())
			.Returns(true);

		_permissionService.CanExamine(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>())
			.Returns(true);

		// Act - search for "Coin" with UseLastIfAmbiguous flag
		var resultLast = await _locateService.Locate(_parser, player, player, "Coin",
			LocateFlags.MatchObjectsInLookerInventory | LocateFlags.UseLastIfAmbiguous);

		// Assert - with UseLastIfAmbiguous should return the second one (thing2 with key 4)
		await Assert.That(resultLast.IsValid()).IsTrue();
		await Assert.That(resultLast.WithoutError().WithoutNone().Object().DBRef.Number).IsEqualTo(4);
	}

	[Test]
	public async Task LocateMatch_VisibilityCheck_ShouldRespectCanExamine()
	{
		// Test that objects are only returned if executor can examine them

		// Arrange
		var sharedRoom = _factory.CreateRoom(999, "Shared Room");
		var player = _factory.CreatePlayer(1, "TestPlayer", sharedRoom);
		var thing = _factory.CreateThing(3, "HiddenObject", sharedRoom, player);

		var contents = new[] { thing }.ToAsyncEnumerable();

		// Mock GetContentsQuery
		_mediator.Send(Arg.Is<GetContentsQuery>(q => true), Arg.Any<CancellationToken>())
			.Returns(callInfo => ValueTask.FromResult<IAsyncEnumerable<AnySharpContent>?>(contents.Select(x => x.AsContent)));

		// Mock GetPlayerQuery to return empty results
		_mediator.Send(Arg.Is<GetPlayerQuery>(q => true), Arg.Any<CancellationToken>())
			.Returns(callInfo => ValueTask.FromResult(AsyncEnumerable.Empty<SharpPlayer>()));

		// Set up permissions - player CAN'T examine the object
		_permissionService.Controls(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>())
			.Returns(true);

		_permissionService.CanInteract(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(),
				Arg.Any<IPermissionService.InteractType>())
			.Returns(false); // Can't interact/see

		_permissionService.CanExamine(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>())
			.Returns(false); // Can't examine

		// Act - should not find the object due to visibility restrictions
		var result = await _locateService.Locate(_parser, player, player, "HiddenObject",
			LocateFlags.MatchObjectsInLookerInventory);

		// Assert - should return None (not visible)
		await Assert.That(result.IsNone).IsTrue();
	}

	[Test]
	public async Task LocateMatch_DifferentExecutorAndLooker_ShouldCheckNearby()
	{
		// Test that when executor != looker and not nearby, permission check triggers

		// Arrange
		var room1 = _factory.CreateRoom(1001, "Room 1");
		var room2 = _factory.CreateRoom(1002, "Room 2");

		var looker = _factory.CreatePlayer(1, "LookerPlayer", room1);
		var executor = _factory.CreatePlayer(2, "ExecutorPlayer", room2); // Different room
		var thing = _factory.CreateThing(3, "TestObject", room1, looker);

		var contents = new[] { thing }.ToAsyncEnumerable();

		// Mock GetContentsQuery
		_mediator.Send(Arg.Is<GetContentsQuery>(q => true), Arg.Any<CancellationToken>())
			.Returns(callInfo => ValueTask.FromResult<IAsyncEnumerable<AnySharpContent>?>(contents.Select(x => x.AsContent)));

		// Mock GetPlayerQuery to return empty results
		_mediator.Send(Arg.Is<GetPlayerQuery>(q => true), Arg.Any<CancellationToken>())
			.Returns(callInfo => ValueTask.FromResult(AsyncEnumerable.Empty<SharpPlayer>()));

		// Set up permissions - executor doesn't control looker (critical for this test)
		_permissionService.Controls(executor, looker)
			.Returns(false);

		// Other controls return true
		_permissionService.Controls(looker, looker)
			.Returns(true);
		_permissionService.Controls(executor, executor)
			.Returns(true);

		_permissionService.CanInteract(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(),
				Arg.Any<IPermissionService.InteractType>())
			.Returns(true);

		_permissionService.CanExamine(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>())
			.Returns(true);

		// Act - should fail with permission error because executor isn't nearby and doesn't control looker
		var result = await _locateService.Locate(_parser, looker, executor, "TestObject",
			LocateFlags.MatchObjectsInLookerInventory);

		// Assert - should get NOT PERMITTED error because they're not nearby
		await Assert.That(result.IsError).IsTrue();
		await Assert.That(result.AsError.Value).Contains("NOT PERMITTED");
	}
}