using Mediator;
using Microsoft.Extensions.Logging;
using NSubstitute;
using OneOf;
using OneOf.Types;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Notifications;
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
	/// Builds a minimal <see cref="SharpChannel"/> with the given <paramref name="privs"/>. Owner/Members
	/// are never touched by <c>AnnounceOnChannelsAsync</c> (which only reads <c>Privs</c> and passes the
	/// channel through to <c>ChannelMessageNotification</c>), so they get trivial stub values matching the
	/// idiom used for <c>AsyncLazy</c>/<c>Lazy</c> fields elsewhere in this file.
	/// </summary>
	private static SharpChannel FakeChannel(string name, string[] privs) =>
		new()
		{
			Name = MarkupText.Plain(name),
			Owner = new(async _ => { await Task.CompletedTask; return null!; }),
			Members = new(() => AsyncEnumerable.Empty<SharpChannel.MemberAndStatus>()),
			Privs = privs
		};

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
	/// object" for everything except <c>#1</c> (God), so <c>DispatchZoneAndMasterRoomHooksAsync</c>'s
	/// unconditional master-room lookup (test config's <c>master_room</c> is <c>#2</c>) is a no-op for
	/// tests that don't care about it. <c>#1</c> resolves to a stand-in God player because
	/// <c>AnnounceDisconnectAsync</c>'s LASTLOGOUT write (<c>remainingConnections == 0</c>) calls
	/// <c>HelperFunctions.GetGod</c> to stamp the attribute's owner - leaving <c>#1</c> unresolvable
	/// would make that write throw and get swallowed by the outer catch, silently breaking LASTLOGOUT
	/// and inflating the logged-error count in tests that assert on it.
	/// Also stubs <c>GetOnChannelQuery</c> to an empty stream, since NSubstitute has no built-in default
	/// for <see cref="IAsyncEnumerable{T}"/> — an unconfigured call returns <see langword="null"/>, and
	/// <c>AnnounceOnChannelsAsync</c>'s <c>await foreach</c> over that would NRE inside the caller's
	/// try/catch and silently truncate everything after it.
	/// </summary>
	private static IMediator FakeMediatorWithNoMasterRoom()
	{
		var mediator = Substitute.For<IMediator>();
		mediator.Send(Arg.Any<GetObjectNodeQuery>(), Arg.Any<CancellationToken>())
			.Returns(new ValueTask<AnyOptionalSharpObject>(new None()));
		mediator.Send(Arg.Is<GetObjectNodeQuery>(q => q.DBRef.Number == 1), Arg.Any<CancellationToken>())
			.Returns(new ValueTask<AnyOptionalSharpObject>(FakeGod()));
		mediator.CreateStream(Arg.Any<GetOnChannelQuery>(), Arg.Any<CancellationToken>())
			.Returns(_ => AsyncEnumerable.Empty<SharpChannel>());
		return mediator;
	}

	/// <summary>
	/// A stand-in for God (<c>#1</c>) - the owner LASTLOGOUT is stamped with when
	/// <c>AnnounceDisconnectAsync</c> writes it directly via <c>SetAttributeCommand</c>, bypassing
	/// <c>IAttributeService.SetAttributeAsync</c>'s permission gate (see the comment at that call site).
	/// </summary>
	private static AnyOptionalSharpObject FakeGod() => new TestObjectFactory().CreatePlayer(1, "God").WithNoneOption();

	/// <summary>
	/// Builds a fresh <see cref="ILogger{ConnectionAnnounceService}"/> substitute for the service's
	/// constructor. Tests that expect an exception to be swallowed assert against this directly.
	/// </summary>
	private static ILogger<ConnectionAnnounceService> FakeLogger() =>
		Substitute.For<ILogger<ConnectionAnnounceService>>();

	[Test]
	public async Task AnnounceConnectAsync_FirstConnection_BroadcastsHasConnected()
	{
		var communicationService = Substitute.For<ICommunicationService>();
		var gameBroadcastService = Substitute.For<IGameBroadcastService>();
		var attributeService = Substitute.For<IAttributeService>();
		StubNoAconnectAttribute(attributeService);
		var configuration = FakeOptionsWrapper();

		var service = new ConnectionAnnounceService(
			communicationService, gameBroadcastService, attributeService, configuration,
			FakeMediatorWithNoMasterRoom(), FakeLogger());

		var player = FakeConnectedPlayer("Bob");
		var parser = Substitute.For<IMUSHCodeParser>();

		await service.AnnounceConnectAsync(parser, player, connectionCount: 1, isHiddenConnection: false);

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
			communicationService, gameBroadcastService, attributeService, configuration,
			FakeMediatorWithNoMasterRoom(), FakeLogger());
		var player = FakeConnectedPlayer("Bob");
		var parser = Substitute.For<IMUSHCodeParser>();

		await service.AnnounceConnectAsync(parser, player, connectionCount: 2, isHiddenConnection: false);

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
		mediator.CreateStream(Arg.Any<GetOnChannelQuery>(), Arg.Any<CancellationToken>())
			.Returns(_ => AsyncEnumerable.Empty<SharpChannel>());

		var service = new ConnectionAnnounceService(
			communicationService, gameBroadcastService, attributeService, configuration, mediator, FakeLogger());

		var player = FakeConnectedPlayer("Bob");
		var parser = Substitute.For<IMUSHCodeParser>();

		await service.AnnounceConnectAsync(parser, player, connectionCount: 1, isHiddenConnection: false);

		// The permission check reads as the hook OWNER evaluating its own attribute (self-eval always
		// passes CanEval, regardless of the owner's own privilege level), not as the connecting PLAYER -
		// see the comment in ConnectionAnnounceService.QueueHookAsync.
		await attributeService.Received(1).GetAttributeAsync(
			hookTarget, hookTarget, "ACONNECT", IAttributeService.AttributeMode.Execute, true);
	}

	/// <summary>
	/// Finding 6 of the final whole-branch review: PennMUSH queues each hook independently, so one
	/// broken global object can't take out the others. Before this fix, <c>QueueHookAsync</c> had no
	/// per-hook try/catch of its own, so a throwing lookup on one master-room object's ACONNECT would
	/// propagate out of <c>DispatchZoneAndMasterRoomHooksAsync</c>'s <c>await foreach</c> and skip
	/// every hook target still to come. This master room has two objects; the first's ACONNECT lookup
	/// throws, and the test proves the second's is still queued.
	/// </summary>
	[Test]
	public async Task AnnounceConnectAsync_OneMasterRoomHookThrows_LaterHookInTheSameRoomStillQueued()
	{
		var communicationService = Substitute.For<ICommunicationService>();
		var gameBroadcastService = Substitute.For<IGameBroadcastService>();
		var attributeService = Substitute.For<IAttributeService>();
		StubNoAconnectAttribute(attributeService);
		var configuration = FakeOptionsWrapper();
		var logger = FakeLogger();

		var factory = new TestObjectFactory();
		var masterRoom = factory.CreateRoom(2, "Master Room");
		var throwingHookTarget = factory.CreateThing(50, "Throwing Hookable Thing", location: masterRoom);
		var laterHookTarget = factory.CreateThing(51, "Later Hookable Thing", location: masterRoom);

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
			.Returns(_ => new[] { throwingHookTarget, laterHookTarget }.ToAsyncEnumerable().Select(x => x.AsContent));
		mediator.CreateStream(Arg.Any<GetOnChannelQuery>(), Arg.Any<CancellationToken>())
			.Returns(_ => AsyncEnumerable.Empty<SharpChannel>());

		var player = FakeConnectedPlayer("Bob");

		attributeService.GetAttributeAsync(
				throwingHookTarget, throwingHookTarget, "ACONNECT", Arg.Any<IAttributeService.AttributeMode>(), Arg.Any<bool>())
			.Returns<OptionalSharpAttributeOrError>(_ => throw new InvalidOperationException("boom"));

		var service = new ConnectionAnnounceService(
			communicationService, gameBroadcastService, attributeService, configuration, mediator, logger);

		var parser = Substitute.For<IMUSHCodeParser>();

		await service.AnnounceConnectAsync(parser, player, connectionCount: 1, isHiddenConnection: false);

		await attributeService.Received(1).GetAttributeAsync(
			laterHookTarget, laterHookTarget, "ACONNECT", IAttributeService.AttributeMode.Execute, true);
		logger.Received(1).Log(
			LogLevel.Error,
			Arg.Any<EventId>(),
			Arg.Any<object>(),
			Arg.Any<Exception>(),
			Arg.Any<Func<object, Exception?, string>>());
	}

	/// <summary>
	/// Finding 6, disconnect side: LASTLOGOUT is written as the LAST statement inside
	/// <c>AnnounceDisconnectAsync</c>'s hook-dispatch sequence, so before this fix a throwing hook
	/// anywhere earlier (the player's own ADISCONNECT, here) would skip it entirely. The per-hook
	/// catch inside <c>QueueHookAsync</c> means the throw never reaches <c>AnnounceDisconnectAsync</c>
	/// at all, so LASTLOGOUT still gets written.
	/// </summary>
	[Test]
	public async Task AnnounceDisconnectAsync_PlayerHookThrows_LastLogoutStillGetsSet()
	{
		var communicationService = Substitute.For<ICommunicationService>();
		var gameBroadcastService = Substitute.For<IGameBroadcastService>();
		var attributeService = Substitute.For<IAttributeService>();
		var configuration = FakeOptionsWrapper();
		var logger = FakeLogger();

		var player = FakeConnectedPlayer("Bob");

		// Every hook lookup throws - player's own ADISCONNECT, the room's (RoomConnects is on in the
		// test config), the zone's (none, per FakeConnectedPlayer), and the master room's (none
		// resolvable via FakeMediatorWithNoMasterRoom).
		attributeService.GetAttributeAsync(
				Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(), Arg.Any<string>(),
				Arg.Any<IAttributeService.AttributeMode>(), Arg.Any<bool>())
			.Returns<OptionalSharpAttributeOrError>(_ => throw new InvalidOperationException("boom"));

		var mediator = FakeMediatorWithNoMasterRoom();
		var service = new ConnectionAnnounceService(
			communicationService, gameBroadcastService, attributeService, configuration,
			mediator, logger);

		var parser = Substitute.For<IMUSHCodeParser>();

		await service.AnnounceDisconnectAsync(parser, player, remainingConnections: 0, isHiddenConnection: false);

		// LASTLOGOUT is engine-maintained bookkeeping, written directly via SetAttributeCommand (stamped
		// with God's ownership) rather than through IAttributeService.SetAttributeAsync's permission
		// gate - see the comment at that call site in ConnectionAnnounceService.
		await mediator.Received(1).Send(
			Arg.Is<SetAttributeCommand>(c =>
				c.DBRef.Equals(player.Object().DBRef) &&
				c.Attribute.SequenceEqual(new[] { "LASTLOGOUT" })),
			Arg.Any<CancellationToken>());
		// Two hooks throw (the player's own ADISCONNECT, then the room's - RoomConnects is on in the
		// test config and FakeConnectedPlayer's location is a Room); the zone is unset and the master
		// room is unresolvable per FakeMediatorWithNoMasterRoom, so neither is attempted. Each throw is
		// caught and logged inside QueueHookAsync without propagating.
		logger.Received(2).Log(
			LogLevel.Error,
			Arg.Any<EventId>(),
			Arg.Any<object>(),
			Arg.Any<Exception>(),
			Arg.Any<Func<object, Exception?, string>>());
	}

	[Test]
	public async Task AnnounceDisconnectAsync_LastConnection_BroadcastsHasDisconnectedAndSetsLastLogout()
	{
		var communicationService = Substitute.For<ICommunicationService>();
		var gameBroadcastService = Substitute.For<IGameBroadcastService>();
		var attributeService = Substitute.For<IAttributeService>();
		StubNoAconnectAttribute(attributeService);
		var configuration = FakeOptionsWrapper();

		var mediator = FakeMediatorWithNoMasterRoom();
		var service = new ConnectionAnnounceService(
			communicationService, gameBroadcastService, attributeService, configuration,
			mediator, FakeLogger());

		var player = FakeConnectedPlayer("Bob");
		var parser = Substitute.For<IMUSHCodeParser>();

		await service.AnnounceDisconnectAsync(parser, player, remainingConnections: 0, isHiddenConnection: false);

		await gameBroadcastService.Received(1).BroadcastToFlagAsync(null, "HEAR_CONNECT", "GAME: Bob has disconnected.");
		await mediator.Received(1).Send(
			Arg.Is<SetAttributeCommand>(c =>
				c.DBRef.Equals(player.Object().DBRef) &&
				c.Attribute.SequenceEqual(new[] { "LASTLOGOUT" })),
			Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task AnnounceDisconnectAsync_OtherConnectionsRemain_UsesPartiallyDisconnectedWordingAndSkipsLastLogout()
	{
		var communicationService = Substitute.For<ICommunicationService>();
		var gameBroadcastService = Substitute.For<IGameBroadcastService>();
		var attributeService = Substitute.For<IAttributeService>();
		StubNoAconnectAttribute(attributeService);
		var configuration = FakeOptionsWrapper();

		var mediator = FakeMediatorWithNoMasterRoom();
		var service = new ConnectionAnnounceService(
			communicationService, gameBroadcastService, attributeService, configuration,
			mediator, FakeLogger());

		var player = FakeConnectedPlayer("Bob");
		var parser = Substitute.For<IMUSHCodeParser>();

		await service.AnnounceDisconnectAsync(parser, player, remainingConnections: 1, isHiddenConnection: false);

		await gameBroadcastService.Received(1).BroadcastToFlagAsync(null, "HEAR_CONNECT", "GAME: Bob has partially disconnected.");
		await mediator.DidNotReceive().Send(Arg.Any<SetAttributeCommand>(), Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Task 7 fix: a thrown exception anywhere inside <c>AnnounceConnectAsync</c> (here, a mocked
	/// <see cref="ICommunicationService.SendToRoomAsync"/> failure standing in for a buggy ACONNECT
	/// hook or a transient DB failure) must be caught and logged, not propagated — so the caller's
	/// subsequent code (e.g. the room-contents refresh) still runs.
	/// </summary>
	[Test]
	public async Task AnnounceConnectAsync_DependencyThrows_ExceptionIsCaughtAndLogged()
	{
		var communicationService = Substitute.For<ICommunicationService>();
		communicationService.SendToRoomAsync(
				Arg.Any<AnySharpObject>(), Arg.Any<AnySharpContainer>(),
				Arg.Any<Func<AnySharpObject, OneOf<MString, string>>>(),
				Arg.Any<INotifyService.NotificationType>(),
				Arg.Any<AnySharpObject?>(), Arg.Any<IEnumerable<AnySharpObject>?>())
			.Returns(_ => throw new InvalidOperationException("boom"));
		var gameBroadcastService = Substitute.For<IGameBroadcastService>();
		var attributeService = Substitute.For<IAttributeService>();
		StubNoAconnectAttribute(attributeService);
		var configuration = FakeOptionsWrapper();
		var logger = FakeLogger();

		var service = new ConnectionAnnounceService(
			communicationService, gameBroadcastService, attributeService, configuration,
			FakeMediatorWithNoMasterRoom(), logger);

		var player = FakeConnectedPlayer("Bob");
		var parser = Substitute.For<IMUSHCodeParser>();

		await service.AnnounceConnectAsync(parser, player, connectionCount: 1, isHiddenConnection: false);

		logger.Received(1).Log(
			LogLevel.Error,
			Arg.Any<EventId>(),
			Arg.Any<object>(),
			Arg.Any<Exception>(),
			Arg.Any<Func<object, Exception?, string>>());
	}

	/// <summary>
	/// Task 7 fix: same guarantee as above, for <c>AnnounceDisconnectAsync</c>.
	/// </summary>
	[Test]
	public async Task AnnounceDisconnectAsync_DependencyThrows_ExceptionIsCaughtAndLogged()
	{
		var communicationService = Substitute.For<ICommunicationService>();
		communicationService.SendToRoomAsync(
				Arg.Any<AnySharpObject>(), Arg.Any<AnySharpContainer>(),
				Arg.Any<Func<AnySharpObject, OneOf<MString, string>>>(),
				Arg.Any<INotifyService.NotificationType>(),
				Arg.Any<AnySharpObject?>(), Arg.Any<IEnumerable<AnySharpObject>?>())
			.Returns(_ => throw new InvalidOperationException("boom"));
		var gameBroadcastService = Substitute.For<IGameBroadcastService>();
		var attributeService = Substitute.For<IAttributeService>();
		StubNoAconnectAttribute(attributeService);
		var configuration = FakeOptionsWrapper();
		var logger = FakeLogger();

		var service = new ConnectionAnnounceService(
			communicationService, gameBroadcastService, attributeService, configuration,
			FakeMediatorWithNoMasterRoom(), logger);

		var player = FakeConnectedPlayer("Bob");
		var parser = Substitute.For<IMUSHCodeParser>();

		await service.AnnounceDisconnectAsync(parser, player, remainingConnections: 0, isHiddenConnection: false);

		logger.Received(1).Log(
			LogLevel.Error,
			Arg.Any<EventId>(),
			Arg.Any<object>(),
			Arg.Any<Exception>(),
			Arg.Any<Func<object, Exception?, string>>());
	}

	/// <summary>
	/// Finding 3 of the Codex review on PR #902: the broadcast section (room/inventory/channel) used to
	/// run inside the SAME try block as the ACONNECT hook dispatch that follows it, so a broadcast
	/// failure jumped straight to <c>AnnounceConnectAsync</c>'s outer catch and skipped the hook
	/// dispatch entirely. <c>BroadcastAnnouncementAsync</c> now isolates the broadcast in its own
	/// try/catch, so a <c>SendToRoomAsync</c> failure here must not prevent the player's own ACONNECT
	/// lookup from still running.
	/// </summary>
	[Test]
	public async Task AnnounceConnectAsync_BroadcastThrows_AconnectHookStillDispatched()
	{
		var communicationService = Substitute.For<ICommunicationService>();
		communicationService.SendToRoomAsync(
				Arg.Any<AnySharpObject>(), Arg.Any<AnySharpContainer>(),
				Arg.Any<Func<AnySharpObject, OneOf<MString, string>>>(),
				Arg.Any<INotifyService.NotificationType>(),
				Arg.Any<AnySharpObject?>(), Arg.Any<IEnumerable<AnySharpObject>?>())
			.Returns(_ => throw new InvalidOperationException("boom"));
		var gameBroadcastService = Substitute.For<IGameBroadcastService>();
		var attributeService = Substitute.For<IAttributeService>();
		StubNoAconnectAttribute(attributeService);
		var configuration = FakeOptionsWrapper();
		var logger = FakeLogger();

		var service = new ConnectionAnnounceService(
			communicationService, gameBroadcastService, attributeService, configuration,
			FakeMediatorWithNoMasterRoom(), logger);

		var player = FakeConnectedPlayer("Bob");
		var parser = Substitute.For<IMUSHCodeParser>();

		await service.AnnounceConnectAsync(parser, player, connectionCount: 1, isHiddenConnection: false);

		await attributeService.Received(1).GetAttributeAsync(
			player, player, "ACONNECT", IAttributeService.AttributeMode.Execute, true);
	}

	/// <summary>
	/// Finding 3, disconnect side: same isolation guarantee, proven against both the ADISCONNECT hook
	/// dispatch and LASTLOGOUT - both of which used to be skipped entirely when the broadcast section
	/// threw, since they ran after it inside the same outer try.
	/// </summary>
	[Test]
	public async Task AnnounceDisconnectAsync_BroadcastThrows_HookAndLastLogoutStillRun()
	{
		var communicationService = Substitute.For<ICommunicationService>();
		communicationService.SendToRoomAsync(
				Arg.Any<AnySharpObject>(), Arg.Any<AnySharpContainer>(),
				Arg.Any<Func<AnySharpObject, OneOf<MString, string>>>(),
				Arg.Any<INotifyService.NotificationType>(),
				Arg.Any<AnySharpObject?>(), Arg.Any<IEnumerable<AnySharpObject>?>())
			.Returns(_ => throw new InvalidOperationException("boom"));
		var gameBroadcastService = Substitute.For<IGameBroadcastService>();
		var attributeService = Substitute.For<IAttributeService>();
		StubNoAconnectAttribute(attributeService);
		var configuration = FakeOptionsWrapper();
		var logger = FakeLogger();

		var mediator = FakeMediatorWithNoMasterRoom();
		var service = new ConnectionAnnounceService(
			communicationService, gameBroadcastService, attributeService, configuration,
			mediator, logger);

		var player = FakeConnectedPlayer("Bob");
		var parser = Substitute.For<IMUSHCodeParser>();

		await service.AnnounceDisconnectAsync(parser, player, remainingConnections: 0, isHiddenConnection: false);

		await attributeService.Received(1).GetAttributeAsync(
			player, player, "ADISCONNECT", IAttributeService.AttributeMode.Execute, true);
		await mediator.Received(1).Send(
			Arg.Is<SetAttributeCommand>(c =>
				c.DBRef.Equals(player.Object().DBRef) &&
				c.Attribute.SequenceEqual(new[] { "LASTLOGOUT" })),
			Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Task 12: a Hidden connection (PennMUSH DESC.hide, distinct from the DARK flag) gets its own
	/// HIDDEN- wording, selected by the caller-supplied <c>isHiddenConnection</c> flag rather than
	/// re-derived from the player object.
	/// </summary>
	[Test]
	public async Task AnnounceConnectAsync_HiddenConnection_UsesHiddenConnectedWording()
	{
		var communicationService = Substitute.For<ICommunicationService>();
		var gameBroadcastService = Substitute.For<IGameBroadcastService>();
		var attributeService = Substitute.For<IAttributeService>();
		StubNoAconnectAttribute(attributeService);
		var configuration = FakeOptionsWrapper();

		var service = new ConnectionAnnounceService(
			communicationService, gameBroadcastService, attributeService, configuration,
			FakeMediatorWithNoMasterRoom(), FakeLogger());

		var player = FakeConnectedPlayer("Bob");
		var parser = Substitute.For<IMUSHCodeParser>();

		await service.AnnounceConnectAsync(parser, player, connectionCount: 1, isHiddenConnection: true);

		await gameBroadcastService.Received(1).BroadcastToFlagAsync(
			null, "HEAR_CONNECT", "GAME: Bob has HIDDEN-connected.");
	}

	/// <summary>
	/// Task 12: same guarantee as above for the disconnect side's HIDDEN-disconnected wording.
	/// </summary>
	[Test]
	public async Task AnnounceDisconnectAsync_HiddenConnection_UsesHiddenDisconnectedWording()
	{
		var communicationService = Substitute.For<ICommunicationService>();
		var gameBroadcastService = Substitute.For<IGameBroadcastService>();
		var attributeService = Substitute.For<IAttributeService>();
		StubNoAconnectAttribute(attributeService);
		var configuration = FakeOptionsWrapper();

		var service = new ConnectionAnnounceService(
			communicationService, gameBroadcastService, attributeService, configuration,
			FakeMediatorWithNoMasterRoom(), FakeLogger());

		var player = FakeConnectedPlayer("Bob");
		var parser = Substitute.For<IMUSHCodeParser>();

		await service.AnnounceDisconnectAsync(parser, player, remainingConnections: 0, isHiddenConnection: true);

		await gameBroadcastService.Received(1).BroadcastToFlagAsync(
			null, "HEAR_CONNECT", "GAME: Bob has HIDDEN-disconnected.");
	}

	/// <summary>
	/// Task 13: the connect line is published to every channel the player belongs to that lacks the
	/// "Quiet" privilege, ported from chat_player_announce (src/extchat.c:3164-3202).
	/// </summary>
	[Test]
	public async Task AnnounceConnectAsync_PlayerOnNonQuietChannel_PublishesChannelMessage()
	{
		var communicationService = Substitute.For<ICommunicationService>();
		var gameBroadcastService = Substitute.For<IGameBroadcastService>();
		var attributeService = Substitute.For<IAttributeService>();
		StubNoAconnectAttribute(attributeService);
		var configuration = FakeOptionsWrapper();
		var mediator = Substitute.For<IMediator>();

		var channel = FakeChannel("Public", privs: []);
		mediator.CreateStream(Arg.Any<GetOnChannelQuery>(), Arg.Any<CancellationToken>())
			.Returns(_ => new[] { channel }.ToAsyncEnumerable());

		var service = new ConnectionAnnounceService(
			communicationService, gameBroadcastService, attributeService, configuration, mediator, FakeLogger());

		var player = FakeConnectedPlayer("Bob");
		var parser = Substitute.For<IMUSHCodeParser>();

		await service.AnnounceConnectAsync(parser, player, connectionCount: 1, isHiddenConnection: false);

		await mediator.Received(1).Publish(Arg.Is<ChannelMessageNotification>(n =>
			n.Channel == channel && n.Message.ToPlainText() == "Bob has connected."), Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Task 13: a channel with the "Quiet" privilege is skipped entirely.
	/// </summary>
	[Test]
	public async Task AnnounceConnectAsync_PlayerOnQuietChannel_DoesNotPublish()
	{
		var communicationService = Substitute.For<ICommunicationService>();
		var gameBroadcastService = Substitute.For<IGameBroadcastService>();
		var attributeService = Substitute.For<IAttributeService>();
		StubNoAconnectAttribute(attributeService);
		var configuration = FakeOptionsWrapper();
		var mediator = Substitute.For<IMediator>();

		var channel = FakeChannel("Quiet Channel", privs: ["Quiet"]);
		mediator.CreateStream(Arg.Any<GetOnChannelQuery>(), Arg.Any<CancellationToken>())
			.Returns(_ => new[] { channel }.ToAsyncEnumerable());

		var service = new ConnectionAnnounceService(
			communicationService, gameBroadcastService, attributeService, configuration, mediator, FakeLogger());

		var player = FakeConnectedPlayer("Bob");
		var parser = Substitute.For<IMUSHCodeParser>();

		await service.AnnounceConnectAsync(parser, player, connectionCount: 1, isHiddenConnection: false);

		await mediator.DidNotReceive().Publish(Arg.Any<ChannelMessageNotification>(), Arg.Any<CancellationToken>());
	}
}
