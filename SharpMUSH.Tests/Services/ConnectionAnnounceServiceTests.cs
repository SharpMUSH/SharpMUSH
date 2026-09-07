using Mediator;
using NSubstitute;
using OneOf;
using OneOf.Types;
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
/// Unit tests for <see cref="ConnectionAnnounceService"/>'s connect-side broadcasts (Task 3),
/// zone/master-room ACONNECT dispatch (Task 4), and disconnect-side broadcasts plus LASTLOGOUT (Task 5).
/// </summary>
public class ConnectionAnnounceServiceTests
{
	/// <summary>
	/// Builds a fresh <see cref="IOptionsWrapper{SharpMUSHOptions}"/> substitute from the shared test
	/// PennMUSH config file, whose defaults have <c>announce_connects yes</c> and
	/// <c>room_connects yes</c> — matching PennMUSH's own out-of-the-box behavior.
	/// </summary>
	private static IOptionsWrapper<SharpMUSHOptions> FakeOptionsWrapper()
	{
		var configFile = Path.Combine(AppContext.BaseDirectory, "Configuration", "Testfile", "mushcnf.dst");
		var options = ReadPennMushConfig.Create(configFile);

		var wrapper = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
		wrapper.CurrentValue.Returns(options);
		return wrapper;
	}

	/// <summary>
	/// Builds a minimal, connected <see cref="AnySharpObject"/> player (DARK unset, not SUSPECT,
	/// located in a plain room) usable by <see cref="ConnectionAnnounceService"/>.
	/// </summary>
	private static AnySharpObject FakeConnectedPlayer(string name, params string[] flagNames)
	{
		var dbref = new DBRef(100, 0);

		var flagSet = flagNames
			.Select((flagName, idx) => new SharpObjectFlag
			{
				Name = flagName,
				Aliases = [],
				Symbol = ((char)('A' + idx)).ToString(),
				SetPermissions = [],
				UnsetPermissions = [],
				System = false,
				TypeRestrictions = ["Player", "Thing", "Room", "Exit"]
			})
			.ToList();

		var room = new SharpRoom
		{
			Id = "test-room-1",
			Aliases = [],
			Object = new SharpObject
			{
				Key = 200,
				Name = "Test Room",
				Type = "Room",
				Locks = System.Collections.Immutable.ImmutableDictionary<string, SharpLockData>.Empty,
				Owner = new(async _ => { await Task.CompletedTask; return null!; }),
				Powers = new(() => AsyncEnumerable.Empty<SharpPower>()),
				Attributes = new(() => AsyncEnumerable.Empty<SharpAttribute>()),
				LazyAttributes = new(() => AsyncEnumerable.Empty<LazySharpAttribute>()),
				AllAttributes = new(() => AsyncEnumerable.Empty<SharpAttribute>()),
				LazyAllAttributes = new(() => AsyncEnumerable.Empty<LazySharpAttribute>()),
				Flags = new(() => AsyncEnumerable.Empty<SharpObjectFlag>()),
				Parent = new(async _ => { await Task.CompletedTask; return new None(); }),
				Zone = new(async _ => { await Task.CompletedTask; return new None(); }),
				Children = new(() => AsyncEnumerable.Empty<SharpObject>())
			},
			Location = new(async _ => { await Task.CompletedTask; return new None(); })
		};

		SharpPlayer? playerRef = null;

		var sharpObject = new SharpObject
		{
			Key = dbref.Number,
			CreationTime = 0L,
			Name = name,
			Type = "Player",
			Locks = System.Collections.Immutable.ImmutableDictionary<string, SharpLockData>.Empty,
			Owner = new(async _ =>
			{
				await Task.CompletedTask;
				return playerRef ?? null!;
			}),
			Powers = new(() => AsyncEnumerable.Empty<SharpPower>()),
			Attributes = new(() => AsyncEnumerable.Empty<SharpAttribute>()),
			LazyAttributes = new(() => AsyncEnumerable.Empty<LazySharpAttribute>()),
			AllAttributes = new(() => AsyncEnumerable.Empty<SharpAttribute>()),
			LazyAllAttributes = new(() => AsyncEnumerable.Empty<LazySharpAttribute>()),
			Flags = new(() => flagSet.ToAsyncEnumerable()),
			Parent = new(async _ => { await Task.CompletedTask; return new None(); }),
			Zone = new(async _ => { await Task.CompletedTask; return new None(); }),
			Children = new(() => AsyncEnumerable.Empty<SharpObject>())
		};

		var player = new SharpPlayer
		{
			Object = sharpObject,
			Aliases = [],
			Location = new(async _ => { await Task.CompletedTask; return room; }),
			Home = new(async _ => { await Task.CompletedTask; return room; }),
			PasswordHash = string.Empty,
			PasswordSalt = null,
			Quota = 20
		};

		playerRef = player;
		return new AnySharpObject(player);
	}

	/// <summary>
	/// Configures the given <see cref="IAttributeService"/> substitute to report "no such attribute"
	/// for every <c>GetAttributeAsync</c> call, so <c>QueueHookAsync</c> takes its early-return path
	/// instead of dereferencing an unconfigured (null) result.
	/// </summary>
	private static void StubNoAconnectAttribute(IAttributeService attributeService) =>
		attributeService.GetAttributeAsync(
				Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(), Arg.Any<string>(),
				Arg.Any<IAttributeService.AttributeMode>(), Arg.Any<bool>())
			.Returns(new ValueTask<OptionalSharpAttributeOrError>(new None()));

	/// <summary>
	/// Builds an <see cref="IMediator"/> substitute whose <c>GetObjectNodeQuery</c> answers "no such
	/// object" for everything, so <c>DispatchZoneAndMasterRoomHooksAsync</c>'s unconditional master-room
	/// lookup (test config's <c>master_room</c> is <c>#2</c>) is a no-op for tests that don't care about it.
	/// </summary>
	private static IMediator FakeMediatorWithNoMasterRoom()
	{
		var mediator = Substitute.For<IMediator>();
		mediator.Send(Arg.Any<GetObjectNodeQuery>(), Arg.Any<CancellationToken>())
			.Returns(new ValueTask<AnyOptionalSharpObject>(new None()));
		return mediator;
	}

	[Test]
	public async Task AnnounceConnectAsync_FirstConnection_BroadcastsHasConnected()
	{
		var communicationService = Substitute.For<ICommunicationService>();
		var gameBroadcastService = Substitute.For<IGameBroadcastService>();
		var attributeService = Substitute.For<IAttributeService>();
		StubNoAconnectAttribute(attributeService);
		var configuration = FakeOptionsWrapper();

		var service = new ConnectionAnnounceService(
			communicationService, gameBroadcastService, attributeService, configuration, FakeMediatorWithNoMasterRoom());

		var player = FakeConnectedPlayer("Bob");
		var parser = Substitute.For<IMUSHCodeParser>();

		await service.AnnounceConnectAsync(parser, player, connectionCount: 1);

		await communicationService.Received(1).SendToRoomAsync(
			player, player.AsContainer,
			Arg.Any<Func<AnySharpObject, OneOf<MString, string>>>(),
			INotifyService.NotificationType.Announce,
			null, null);

		await gameBroadcastService.Received(1).BroadcastToFlagAsync(
			null, "HEAR_CONNECT", "GAME: Bob has connected.");
	}

	[Test]
	public async Task AnnounceConnectAsync_SecondConnection_UsesReconnectedWording()
	{
		var communicationService = Substitute.For<ICommunicationService>();
		var gameBroadcastService = Substitute.For<IGameBroadcastService>();
		var attributeService = Substitute.For<IAttributeService>();
		StubNoAconnectAttribute(attributeService);
		var configuration = FakeOptionsWrapper();

		var service = new ConnectionAnnounceService(
			communicationService, gameBroadcastService, attributeService, configuration, FakeMediatorWithNoMasterRoom());
		var player = FakeConnectedPlayer("Bob");
		var parser = Substitute.For<IMUSHCodeParser>();

		await service.AnnounceConnectAsync(parser, player, connectionCount: 2);

		await gameBroadcastService.Received(1).BroadcastToFlagAsync(
			null, "HEAR_CONNECT", "GAME: Bob has reconnected.");
	}

	[Test]
	public async Task AnnounceConnectAsync_MasterRoomObjectsWithAconnect_AreQueued()
	{
		var communicationService = Substitute.For<ICommunicationService>();
		var gameBroadcastService = Substitute.For<IGameBroadcastService>();
		var attributeService = Substitute.For<IAttributeService>();
		StubNoAconnectAttribute(attributeService);
		var configuration = FakeOptionsWrapper();

		// Test config's master_room is #2 (SharpMUSH.Tests/Configuration/Testfile/mushcnf.dst).
		var factory = new TestObjectFactory();
		var masterRoom = factory.CreateRoom(2, "Master Room");
		var hookTarget = factory.CreateThing(50, "Hookable Thing", location: masterRoom);

		var mediator = Substitute.For<IMediator>();
		mediator.Send(Arg.Any<GetObjectNodeQuery>(), Arg.Any<CancellationToken>())
			.Returns(new ValueTask<AnyOptionalSharpObject>(new None()));
		mediator.Send(
				Arg.Is<GetObjectNodeQuery>(q => q.DBRef.Number == masterRoom.Object.Key),
				Arg.Any<CancellationToken>())
			.Returns(new ValueTask<AnyOptionalSharpObject>(masterRoom));
		mediator.CreateStream(Arg.Any<GetContentsQuery>(), Arg.Any<CancellationToken>())
			.Returns(_ => AsyncEnumerable.Empty<AnySharpContent>());
		mediator.CreateStream(
				Arg.Is<GetContentsQuery>(q =>
					q.DBRef.Match(d => d, c => c.Object().DBRef).Number == masterRoom.Object.Key),
				Arg.Any<CancellationToken>())
			.Returns(_ => new[] { hookTarget }.ToAsyncEnumerable().Select(x => x.AsContent));

		var service = new ConnectionAnnounceService(
			communicationService, gameBroadcastService, attributeService, configuration, mediator);

		var player = FakeConnectedPlayer("Bob");
		var parser = Substitute.For<IMUSHCodeParser>();

		await service.AnnounceConnectAsync(parser, player, connectionCount: 1);

		await attributeService.Received(1).GetAttributeAsync(
			player, hookTarget, "ACONNECT", IAttributeService.AttributeMode.Execute, true);
	}

	[Test]
	public async Task AnnounceDisconnectAsync_LastConnection_BroadcastsHasDisconnectedAndSetsLastLogout()
	{
		var communicationService = Substitute.For<ICommunicationService>();
		var gameBroadcastService = Substitute.For<IGameBroadcastService>();
		var attributeService = Substitute.For<IAttributeService>();
		StubNoAconnectAttribute(attributeService);
		var configuration = FakeOptionsWrapper();

		var service = new ConnectionAnnounceService(
			communicationService, gameBroadcastService, attributeService, configuration, FakeMediatorWithNoMasterRoom());

		var player = FakeConnectedPlayer("Bob");
		var parser = Substitute.For<IMUSHCodeParser>();

		await service.AnnounceDisconnectAsync(parser, player, remainingConnections: 0);

		await gameBroadcastService.Received(1).BroadcastToFlagAsync(null, "HEAR_CONNECT", "GAME: Bob has disconnected.");
		await attributeService.Received(1).SetAttributeAsync(player, player, "LASTLOGOUT", Arg.Any<MString>());
	}

	[Test]
	public async Task AnnounceDisconnectAsync_OtherConnectionsRemain_UsesPartiallyDisconnectedWordingAndSkipsLastLogout()
	{
		var communicationService = Substitute.For<ICommunicationService>();
		var gameBroadcastService = Substitute.For<IGameBroadcastService>();
		var attributeService = Substitute.For<IAttributeService>();
		StubNoAconnectAttribute(attributeService);
		var configuration = FakeOptionsWrapper();

		var service = new ConnectionAnnounceService(
			communicationService, gameBroadcastService, attributeService, configuration, FakeMediatorWithNoMasterRoom());

		var player = FakeConnectedPlayer("Bob");
		var parser = Substitute.For<IMUSHCodeParser>();

		await service.AnnounceDisconnectAsync(parser, player, remainingConnections: 1);

		await gameBroadcastService.Received(1).BroadcastToFlagAsync(null, "HEAR_CONNECT", "GAME: Bob has partially disconnected.");
		await attributeService.DidNotReceive().SetAttributeAsync(player, player, "LASTLOGOUT", Arg.Any<MString>());
	}
}
