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
		var player = CreateMockPlayer("TestPlayer", new DBRef(1));
		var thing = CreateMockThing("TestObject", new DBRef(3));
		
		var contents = new[] { thing }.ToAsyncEnumerable();
		
		_mediator.Send(Arg.Any<GetContentsQuery>(), Arg.Any<CancellationToken>())
			.Returns(contents.Select(x => x.AsContent));
		
		// Mock GetPlayerQuery to return empty results
		_mediator.Send(Arg.Any<GetPlayerQuery>(), Arg.Any<CancellationToken>())
			.Returns(callInfo => ValueTask.FromResult(AsyncEnumerable.Empty<SharpPlayer>()));
			
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
		
		// Mock GetPlayerQuery to return empty results
		_mediator.Send(Arg.Any<GetPlayerQuery>(), Arg.Any<CancellationToken>())
			.Returns(callInfo => ValueTask.FromResult(AsyncEnumerable.Empty<SharpPlayer>()));
			
		_permissionService.CanInteract(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(), 
			IPermissionService.InteractType.Match)
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
		var player = CreateMockPlayer("TestPlayer", new DBRef(1));
		
		// Mock GetPlayerQuery to return empty results
		_mediator.Send(Arg.Any<GetPlayerQuery>(), Arg.Any<CancellationToken>())
			.Returns(callInfo => ValueTask.FromResult(AsyncEnumerable.Empty<SharpPlayer>()));
		
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

	[Test]
	public async Task LocateMatch_PermissionCheck_ShouldUseCorrectLogic()
	{
		// This test verifies the fix for the permission check logic
		
		// Arrange
		var player = CreateMockPlayer("TestPlayer", new DBRef(1));
		var target = CreateMockPlayer("TargetPlayer", new DBRef(2));
		
		// Mock GetPlayerQuery to return empty results
		_mediator.Send(Arg.Any<GetPlayerQuery>(), Arg.Any<CancellationToken>())
			.Returns(callInfo => ValueTask.FromResult(AsyncEnumerable.Empty<SharpPlayer>()));
		
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
		// Create a simple mock room to use as the location
		var mockRoom = new SharpRoom
		{
			Id = "mock-room",
			Object = new SharpObject
			{
				Key = 0,
				Name = "Mock Room",
				Type = "Room",
				Locks = ImmutableDictionary<string, string>.Empty,
				Owner = new(async ct => { await ValueTask.CompletedTask; return null!; }),
				Powers = new(() => AsyncEnumerable.Empty<SharpPower>()),
				Attributes = new(() => AsyncEnumerable.Empty<SharpAttribute>()),
				LazyAttributes = new(() => AsyncEnumerable.Empty<LazySharpAttribute>()),
				AllAttributes = new(() => AsyncEnumerable.Empty<SharpAttribute>()),
				LazyAllAttributes = new(() => AsyncEnumerable.Empty<LazySharpAttribute>()),
				Flags = new(() => AsyncEnumerable.Empty<SharpObjectFlag>()),
				Parent = new(async ct => { await ValueTask.CompletedTask; return new None(); }),
				Children = new(() => null)
			}
		};
		
		var sharpObject = new SharpObject
		{
			Key = (int)dbref.Number,
			CreationTime = dbref.CreationMilliseconds ?? 0L,
			Name = name,
			Type = "Player",
			Locks = ImmutableDictionary<string, string>.Empty,
			Owner = new(async ct => { await ValueTask.CompletedTask; return null!; }),
			Powers = new(() => AsyncEnumerable.Empty<SharpPower>()),
			Attributes = new(() => AsyncEnumerable.Empty<SharpAttribute>()),
			LazyAttributes = new(() => AsyncEnumerable.Empty<LazySharpAttribute>()),
			AllAttributes = new(() => AsyncEnumerable.Empty<SharpAttribute>()),
			LazyAllAttributes = new(() => AsyncEnumerable.Empty<LazySharpAttribute>()),
			Flags = new(() => AsyncEnumerable.Empty<SharpObjectFlag>()),
			Parent = new(async ct => { await ValueTask.CompletedTask; return new None(); }),
			Children = new(() => null)
		};
		
		var player = new SharpPlayer
		{
			Object = sharpObject,
			Aliases = Array.Empty<string>(),
			Location = new(async ct => { await ValueTask.CompletedTask; return mockRoom; }),
			Home = new(async ct => { await ValueTask.CompletedTask; return mockRoom; }),
			PasswordHash = string.Empty
		};
		
		return new AnySharpObject(player);
	}
	
	private static AnySharpObject CreateMockThing(string name, DBRef dbref)
	{
		// Create a simple mock room to use as the location
		var mockRoom = new SharpRoom
		{
			Id = "mock-room",
			Object = new SharpObject
			{
				Key = 0,
				Name = "Mock Room",
				Type = "Room",
				Locks = ImmutableDictionary<string, string>.Empty,
				Owner = new(async ct => { await ValueTask.CompletedTask; return null!; }),
				Powers = new(() => AsyncEnumerable.Empty<SharpPower>()),
				Attributes = new(() => AsyncEnumerable.Empty<SharpAttribute>()),
				LazyAttributes = new(() => AsyncEnumerable.Empty<LazySharpAttribute>()),
				AllAttributes = new(() => AsyncEnumerable.Empty<SharpAttribute>()),
				LazyAllAttributes = new(() => AsyncEnumerable.Empty<LazySharpAttribute>()),
				Flags = new(() => AsyncEnumerable.Empty<SharpObjectFlag>()),
				Parent = new(async ct => { await ValueTask.CompletedTask; return new None(); }),
				Children = new(() => null)
			}
		};
		
		var sharpObject = new SharpObject
		{
			Key = (int)dbref.Number,
			CreationTime = dbref.CreationMilliseconds ?? 0L,
			Name = name,
			Type = "Thing",
			Locks = ImmutableDictionary<string, string>.Empty,
			Owner = new(async ct => { await ValueTask.CompletedTask; return null!; }),
			Powers = new(() => AsyncEnumerable.Empty<SharpPower>()),
			Attributes = new(() => AsyncEnumerable.Empty<SharpAttribute>()),
			LazyAttributes = new(() => AsyncEnumerable.Empty<LazySharpAttribute>()),
			AllAttributes = new(() => AsyncEnumerable.Empty<SharpAttribute>()),
			LazyAllAttributes = new(() => AsyncEnumerable.Empty<LazySharpAttribute>()),
			Flags = new(() => AsyncEnumerable.Empty<SharpObjectFlag>()),
			Parent = new(async ct => { await ValueTask.CompletedTask; return new None(); }),
			Children = new(() => null)
		};
		
		var thing = new SharpThing
		{
			Object = sharpObject,
			Aliases = Array.Empty<string>(),
			Location = new(async ct => { await ValueTask.CompletedTask; return mockRoom; }),
			Home = new(async ct => { await ValueTask.CompletedTask; return mockRoom; })
		};
		
		return new AnySharpObject(thing);
	}
}
