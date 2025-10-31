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
	
	public LocateServiceCompatibilityTests()
	{
		var database = new DatabaseOptions(
			PlayerStart: 0u,
			MasterRoom: 0u,
			BaseRoom: 0u,
			DefaultHome: 0u,
			ExitsConnectRooms: true,
			ZoneControlZmpOnly: false,
			AncestorRoom: null,
			AncestorExit: null,
			AncestorThing: null,
			AncestorPlayer: null,
			EventHandler: null,
			HttpHandler: null,
			HttpRequestsPerSecond: 10u
		);

		// Create a real SharpMUSHOptions with all required properties
		var options = Substitute.For<SharpMUSHOptions>();
		options.Database.Returns(database);

		var wrapper = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
		wrapper.CurrentValue.Returns(options);
		
		_locateService = new LocateService(_mediator, _notifyService, _permissionService, wrapper);
	}

	[Skip("NSubstitute issues")]
	[Test]
	public async Task LocateMatch_NameMatching_ShouldMatchExactNamesForNonExits()
	{
		// Arrange
		var player = CreateMockPlayer("TestPlayer", new DBRef(1));
		var thing = CreateMockThing("TestObject", new DBRef(3));
		
		var contents = new[] { thing }.ToAsyncEnumerable();
		
		_mediator.Send(Arg.Any<GetContentsQuery>(), Arg.Any<CancellationToken>())
			.Returns(contents.Select(x => x.AsContent));
			
		_permissionService.CanInteract(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(), 
			IPermissionService.InteractType.Match)
			.Returns(true);
			
		// Act
		var result = await _locateService.Locate(_parser, player, player, "TestObject", 
			LocateFlags.MatchObjectsInLookerInventory);
		
		// Assert
		await Assert.That(result.IsValid()).IsTrue();
		await Assert.That(result.WithoutError().WithoutNone().Object().DBRef).IsEqualTo(new DBRef(3));
	}

	[Skip("NSubstitute issues")]
	[Test]
	public async Task LocateMatch_NameMatching_ShouldNotMatchWrongNamesForNonExits()
	{
		// This test verifies the fix for the inverted logic bug
		// Before fix: (!cur.IsExit && !string.Equals(...)) would match everything that DIDN'T match
		// After fix: (!cur.IsExit && string.Equals(...)) only matches exact names
		
		// Arrange
		var player = CreateMockPlayer("TestPlayer", new DBRef(1));
		var thing = CreateMockThing("TestObject", new DBRef(3));
		
		var contents = new[] { thing }.ToAsyncEnumerable();
		
		_mediator.Send(Arg.Any<GetContentsQuery>(), Arg.Any<CancellationToken>())
			.Returns(contents.Select(x => x.AsContent));
			
		_permissionService.CanInteract(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(), 
			IPermissionService.InteractType.Match)
			.Returns(true);
			
		// Act
		var result = await _locateService.Locate(_parser, player, player, "WrongName", 
			LocateFlags.MatchObjectsInLookerInventory);
		
		// Assert
		await Assert.That(result.IsNone).IsTrue();
	}

	[Skip("NSubstitute issues")]
	[Test]
	public async Task LocateMatch_MeMatching_ShouldRespectNoTypePreference()
	{
		// This test verifies the fix for the NoTypePreference check
		
		// Arrange
		var player = CreateMockPlayer("TestPlayer", new DBRef(1));
		
		// Act - with NoTypePreference, should not match "me"
		var resultWithNoTypePreference = await _locateService.Locate(_parser, player, player, "me", 
			LocateFlags.NoTypePreference | LocateFlags.MatchMeForLooker);
		
		// Act - without NoTypePreference, should match "me"
		var resultWithoutNoTypePreference = await _locateService.Locate(_parser, player, player, "me", 
			LocateFlags.MatchMeForLooker);
		
		// Assert
		await Assert.That(resultWithNoTypePreference.IsNone || resultWithNoTypePreference.IsError).IsTrue();
		await Assert.That(resultWithoutNoTypePreference.IsValid()).IsTrue();
		await Assert.That(resultWithoutNoTypePreference.WithoutError().WithoutNone().Object().DBRef).IsEqualTo(new DBRef(1));
	}

	[Skip("NSubstitute issues")]
	[Test]
	public async Task LocateMatch_PermissionCheck_ShouldUseCorrectLogic()
	{
		// This test verifies the fix for the permission check logic
		
		// Arrange
		var player = CreateMockPlayer("TestPlayer", new DBRef(1));
		var target = CreateMockPlayer("TargetPlayer", new DBRef(2));
		
		_permissionService.Controls(player, target)
			.Returns(false);
		
		// Act - with OnlyMatchLookerControlledObjects, should fail when player doesn't control target
		var resultWithControlRequired = await _locateService.Locate(_parser, player, target, "me", 
			LocateFlags.MatchMeForLooker | LocateFlags.OnlyMatchLookerControlledObjects);
		
		// Act - without OnlyMatchLookerControlledObjects, should succeed
		var resultWithoutControlRequired = await _locateService.Locate(_parser, player, target, "me", 
			LocateFlags.MatchMeForLooker);
		
		// Assert
		await Assert.That(resultWithControlRequired.IsError).IsTrue();
		await Assert.That(resultWithControlRequired.AsError.Value).IsEqualTo(Errors.ErrorPerm);
		await Assert.That(resultWithoutControlRequired.IsValid()).IsTrue();
	}

	private static AnySharpObject CreateMockPlayer(string name, DBRef dbref)
	{
		var sharpObject = Substitute.For<SharpObject>();
		sharpObject.Key.Returns(dbref.Number);
		sharpObject.CreationTime.Returns(dbref.CreationMilliseconds ?? 0L);
		sharpObject.Name.Returns(name);
		sharpObject.Type.Returns("Player");
		
		var player = Substitute.For<SharpPlayer>();
		player.Object.Returns(sharpObject);
		player.Aliases.Returns(Array.Empty<string>());
		
		return new AnySharpObject(player);
	}
	
	private static AnySharpObject CreateMockThing(string name, DBRef dbref)
	{
		var sharpObject = Substitute.For<SharpObject>();
		sharpObject.Key.Returns(dbref.Number);
		sharpObject.CreationTime.Returns(dbref.CreationMilliseconds ?? 0L);
		sharpObject.Name.Returns(name);
		sharpObject.Type.Returns("Thing");
		
		var thing = Substitute.For<SharpThing>();
		thing.Object.Returns(sharpObject);
		thing.Aliases.Returns(Array.Empty<string>());
		
		return new AnySharpObject(thing);
	}
}
