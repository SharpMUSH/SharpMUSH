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
		await Assert.That(result.WithoutError().WithoutNone().Object().DBRef).IsEqualTo(new DBRef(3));
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
		await Assert.That(resultWithoutNoTypePreference.WithoutError().WithoutNone().Object().DBRef).IsEqualTo(new DBRef(1));
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
}
