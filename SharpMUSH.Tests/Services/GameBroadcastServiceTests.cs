using Mediator;
using NSubstitute;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Models;
using System.Collections.Concurrent;
using System.Text;

namespace SharpMUSH.Tests.Services;

public class GameBroadcastServiceTests
{
	private static AnyOptionalSharpObject CreateMockPlayerWithFlags(DBRef dbref, params string[] flagNames)
	{
		// Create a SharpObject with appropriate flags
		var flagSet = flagNames
			.Select((name, idx) => new SharpObjectFlag
			{
				Name = name,
				Aliases = Array.Empty<string>(),
				Symbol = ((char)('A' + idx)).ToString(),
				SetPermissions = Array.Empty<string>(),
				UnsetPermissions = Array.Empty<string>(),
				System = false,
				TypeRestrictions = new[] { "Player", "Thing", "Room", "Exit" }
			})
			.ToList();

		var room = new SharpRoom
		{
			Id = $"test-room-{dbref.Number}",
			Object = new SharpObject
			{
				Key = dbref.Number + 1000,
				Name = $"Room{dbref.Number}",
				Type = "Room",
				Locks = System.Collections.Immutable.ImmutableDictionary<string, SharpLockData>.Empty,
				Owner = new(async ct => { await ValueTask.CompletedTask; return null!; }),
				Powers = new(() => AsyncEnumerable.Empty<SharpPower>()),
				Attributes = new(() => AsyncEnumerable.Empty<SharpAttribute>()),
				LazyAttributes = new(() => AsyncEnumerable.Empty<LazySharpAttribute>()),
				AllAttributes = new(() => AsyncEnumerable.Empty<SharpAttribute>()),
				LazyAllAttributes = new(() => AsyncEnumerable.Empty<LazySharpAttribute>()),
				Flags = new(() => AsyncEnumerable.Empty<SharpObjectFlag>()),
				Parent = new(async ct => { await ValueTask.CompletedTask; return new SharpMUSH.Library.DiscriminatedUnions.None(); }),
				Zone = new(async ct => { await ValueTask.CompletedTask; return new SharpMUSH.Library.DiscriminatedUnions.None(); }),
				Children = new(() => AsyncEnumerable.Empty<SharpObject>())
			},
			Location = new(async ct => { await ValueTask.CompletedTask; return new SharpMUSH.Library.DiscriminatedUnions.None(); })
		};

		SharpPlayer? playerRef = null;

		var sharpObject = new SharpObject
		{
			Key = dbref.Number,
			CreationTime = 0L,
			Name = $"TestPlayer{dbref.Number}",
			Type = "Player",
			Locks = System.Collections.Immutable.ImmutableDictionary<string, SharpLockData>.Empty,
			Owner = new(async ct =>
			{
				await ValueTask.CompletedTask;
				return playerRef ?? null!;
			}),
			Powers = new(() => AsyncEnumerable.Empty<SharpPower>()),
			Attributes = new(() => AsyncEnumerable.Empty<SharpAttribute>()),
			LazyAttributes = new(() => AsyncEnumerable.Empty<LazySharpAttribute>()),
			AllAttributes = new(() => AsyncEnumerable.Empty<SharpAttribute>()),
			LazyAllAttributes = new(() => AsyncEnumerable.Empty<LazySharpAttribute>()),
			Flags = new(() => flagSet.ToAsyncEnumerable()),
			Parent = new(async ct => { await ValueTask.CompletedTask; return new SharpMUSH.Library.DiscriminatedUnions.None(); }),
			Zone = new(async ct => { await ValueTask.CompletedTask; return new SharpMUSH.Library.DiscriminatedUnions.None(); }),
			Children = new(() => AsyncEnumerable.Empty<SharpObject>())
		};

		// Create a SharpPlayer with the SharpObject
		var player = new SharpPlayer
		{
			Object = sharpObject,
			Aliases = Array.Empty<string>(),
			Location = new(async ct => { await ValueTask.CompletedTask; return room; }),
			Home = new(async ct => { await ValueTask.CompletedTask; return room; }),
			PasswordHash = string.Empty,
			PasswordSalt = null,
			Quota = 20
		};

		playerRef = player;
		return new AnyOptionalSharpObject(player);
	}

	[Test]
	public async Task BroadcastToFlagAsync_TwoFlagGroups_RequiresAnyOfFirstGroupAndTheSecondFlag()
	{
		var connectionService = Substitute.For<IConnectionService>();
		var notifyService = Substitute.For<INotifyService>();
		var mediator = Substitute.For<IMediator>();

		var royaltyPlayerRef = new DBRef(10, null);
		var mortalPlayerRef = new DBRef(11, null);

		// Create metadata dictionaries with required keys
		var royaltyMetadata = new ConcurrentDictionary<string, string>
		{
			["ConnectionStartTime"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(),
			["LastConnectionSignal"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(),
			["ConnectionType"] = "telnet",
			["PresenceClass"] = "Interactive"
		};

		var mortalMetadata = new ConcurrentDictionary<string, string>
		{
			["ConnectionStartTime"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(),
			["LastConnectionSignal"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(),
			["ConnectionType"] = "telnet",
			["PresenceClass"] = "Interactive"
		};

		// Create ConnectionData records with all required parameters
		var royaltyConn = new IConnectionService.ConnectionData(
			Handle: 1,
			Ref: royaltyPlayerRef,
			State: IConnectionService.ConnectionState.LoggedIn,
			OutputFunction: _ => ValueTask.CompletedTask,
			PromptOutputFunction: _ => ValueTask.CompletedTask,
			Encoding: () => Encoding.UTF8,
			Metadata: royaltyMetadata
		);

		var mortalConn = new IConnectionService.ConnectionData(
			Handle: 2,
			Ref: mortalPlayerRef,
			State: IConnectionService.ConnectionState.LoggedIn,
			OutputFunction: _ => ValueTask.CompletedTask,
			PromptOutputFunction: _ => ValueTask.CompletedTask,
			Encoding: () => Encoding.UTF8,
			Metadata: mortalMetadata
		);

		connectionService.GetAll().Returns(new[] { royaltyConn, mortalConn }.ToAsyncEnumerable());

		// Create mocked player objects with flags
		var royaltyPlayerResult = CreateMockPlayerWithFlags(royaltyPlayerRef, "ROYALTY", "HEAR_CONNECT");
		var mortalPlayerResult = CreateMockPlayerWithFlags(mortalPlayerRef, "HEAR_CONNECT");

		mediator.Send(Arg.Is<GetObjectNodeQuery>(q => q.DBRef == royaltyPlayerRef), Arg.Any<CancellationToken>())
			.Returns(royaltyPlayerResult);
		mediator.Send(Arg.Is<GetObjectNodeQuery>(q => q.DBRef == mortalPlayerRef), Arg.Any<CancellationToken>())
			.Returns(mortalPlayerResult);

		var service = new GameBroadcastService(connectionService, notifyService, mediator);

		await service.BroadcastToFlagAsync(["ROYALTY", "WIZARD"], "HEAR_CONNECT", "GAME: Someone has connected.");

		// Only the royalty player should receive the message (has ROYALTY from anyOfFlags and HEAR_CONNECT)
		await notifyService.Received(1).Notify(1, Arg.Any<SharpMessage>());
		await notifyService.DidNotReceive().Notify(2, Arg.Any<SharpMessage>());
	}

	[Test]
	public async Task BroadcastToFlagAsync_SingleFlag_DelegatesTo_TwoFlagVersion()
	{
		var connectionService = Substitute.For<IConnectionService>();
		var notifyService = Substitute.For<INotifyService>();
		var mediator = Substitute.For<IMediator>();

		var playerRef = new DBRef(10, null);

		var metadata = new ConcurrentDictionary<string, string>
		{
			["ConnectionStartTime"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(),
			["LastConnectionSignal"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(),
			["ConnectionType"] = "telnet",
			["PresenceClass"] = "Interactive"
		};

		var conn = new IConnectionService.ConnectionData(
			Handle: 1,
			Ref: playerRef,
			State: IConnectionService.ConnectionState.LoggedIn,
			OutputFunction: _ => ValueTask.CompletedTask,
			PromptOutputFunction: _ => ValueTask.CompletedTask,
			Encoding: () => Encoding.UTF8,
			Metadata: metadata
		);

		connectionService.GetAll().Returns(new[] { conn }.ToAsyncEnumerable());

		var playerResult = CreateMockPlayerWithFlags(playerRef, "WIZARD");

		mediator.Send(Arg.Is<GetObjectNodeQuery>(q => q.DBRef == playerRef), Arg.Any<CancellationToken>())
			.Returns(playerResult);

		var service = new GameBroadcastService(connectionService, notifyService, mediator);

		await service.BroadcastToFlagAsync("WIZARD", "GAME: A wizard has logged in.");

		await notifyService.Received(1).Notify(1, Arg.Any<SharpMessage>());
	}

	[Test]
	public async Task BroadcastToFlagAsync_NoAnyOfFlags_RequiresOnlyRequiredFlag()
	{
		var connectionService = Substitute.For<IConnectionService>();
		var notifyService = Substitute.For<INotifyService>();
		var mediator = Substitute.For<IMediator>();

		var playerRef = new DBRef(10, null);

		var metadata = new ConcurrentDictionary<string, string>
		{
			["ConnectionStartTime"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(),
			["LastConnectionSignal"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(),
			["ConnectionType"] = "telnet",
			["PresenceClass"] = "Interactive"
		};

		var conn = new IConnectionService.ConnectionData(
			Handle: 1,
			Ref: playerRef,
			State: IConnectionService.ConnectionState.LoggedIn,
			OutputFunction: _ => ValueTask.CompletedTask,
			PromptOutputFunction: _ => ValueTask.CompletedTask,
			Encoding: () => Encoding.UTF8,
			Metadata: metadata
		);

		connectionService.GetAll().Returns(new[] { conn }.ToAsyncEnumerable());

		var playerResult = CreateMockPlayerWithFlags(playerRef, "HEAR_CONNECT");

		mediator.Send(Arg.Is<GetObjectNodeQuery>(q => q.DBRef == playerRef), Arg.Any<CancellationToken>())
			.Returns(playerResult);

		var service = new GameBroadcastService(connectionService, notifyService, mediator);

		// Passing null for anyOfFlags should not require any flag from the first group
		await service.BroadcastToFlagAsync(null, "HEAR_CONNECT", "GAME: Connection event.");

		await notifyService.Received(1).Notify(1, Arg.Any<SharpMessage>());
	}

	[Test]
	public async Task BroadcastToFlagAsync_SkipsPlayersWithoutRequiredFlag()
	{
		var connectionService = Substitute.For<IConnectionService>();
		var notifyService = Substitute.For<INotifyService>();
		var mediator = Substitute.For<IMediator>();

		var playerRef = new DBRef(10, null);

		var metadata = new ConcurrentDictionary<string, string>
		{
			["ConnectionStartTime"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(),
			["LastConnectionSignal"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(),
			["ConnectionType"] = "telnet",
			["PresenceClass"] = "Interactive"
		};

		var conn = new IConnectionService.ConnectionData(
			Handle: 1,
			Ref: playerRef,
			State: IConnectionService.ConnectionState.LoggedIn,
			OutputFunction: _ => ValueTask.CompletedTask,
			PromptOutputFunction: _ => ValueTask.CompletedTask,
			Encoding: () => Encoding.UTF8,
			Metadata: metadata
		);

		connectionService.GetAll().Returns(new[] { conn }.ToAsyncEnumerable());

		// Player has ROYALTY but not HEAR_CONNECT
		var playerResult = CreateMockPlayerWithFlags(playerRef, "ROYALTY");

		mediator.Send(Arg.Is<GetObjectNodeQuery>(q => q.DBRef == playerRef), Arg.Any<CancellationToken>())
			.Returns(playerResult);

		var service = new GameBroadcastService(connectionService, notifyService, mediator);

		await service.BroadcastToFlagAsync(["ROYALTY"], "HEAR_CONNECT", "GAME: Connection event.");

		// Player has ROYALTY but not HEAR_CONNECT, so should not receive the message
		await notifyService.DidNotReceive().Notify(1, Arg.Any<SharpMessage>());
	}
}
