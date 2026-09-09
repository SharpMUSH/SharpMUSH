using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.AspNetCore.SignalR;
using NSubstitute;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Portal;
using SharpMUSH.Library.Reality;
using SharpMUSH.Server.Hubs;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Hubs;

public class RealityRoomEventTests
{
	private readonly IHubContext<GameHub, IGameHubClient> _hub = Substitute.For<IHubContext<GameHub, IGameHubClient>>();
	private readonly HubConnectionRegistry _registry = new();
	private readonly IVisibleWorldProjection _projection = Substitute.For<IVisibleWorldProjection>();
	private readonly IRealityPolicy _reality = Substitute.For<IRealityPolicy>();
	private readonly DBRef _room = new(10, 1);
	private readonly DBRef _source = new(20, 1);
	private readonly RoomEventDispatcher _dispatcher;

	public RealityRoomEventTests()
	{
		_hub.Clients.Returns(Substitute.For<IHubClients<IGameHubClient>>());
		_hub.Clients.Group(Arg.Any<string>()).Returns(Substitute.For<IGameHubClient>());
		_hub.Clients.Client(Arg.Any<string>()).Returns(_ => Substitute.For<IGameHubClient>());
		_dispatcher = new(_hub, _registry, _projection, _reality, NullLogger<RoomEventDispatcher>.Instance);
	}

	private CapabilityActor Subscribe(string connection, int character)
	{
		var actor = new CapabilityActor("account-" + character, new DBRef(character, 1), new DBRef(character, 1));
		_registry.Add(connection, actor.AccountId, "127.0.0.1", () => { });
		_registry.JoinRoom(connection, actor, _room);
		return actor;
	}

	[Test]
	public async Task EnabledEventsUseRecipientChecksAndNeverTheRoomGroup()
	{
		_reality.IsEnabledAsync(Arg.Any<CancellationToken>()).Returns(true);
		var allowed = Subscribe("allowed", 30);
		Subscribe("hidden", 31);
		_projection.CanReceiveRoomEventAsync(allowed, _room, _source, RoomEventType.Say, Arg.Any<CancellationToken>()).Returns(true);
		var client = Substitute.For<IGameHubClient>();
		_hub.Clients.Client("allowed").Returns(client);
		var message = new RoomEventMessage(_room.ToString(), RoomEventType.Say, "Actor", "Private words", _source.ToString());
		await _dispatcher.DispatchAsync(message);
		await client.Received(1).ReceiveRoomEvent(message);
		_hub.Clients.DidNotReceive().Client("hidden");
		_hub.Clients.DidNotReceive().Group(Arg.Any<string>());
	}

	[Test]
	[Arguments(null)]
	[Arguments("#20")]
	[Arguments("invalid")]
	public async Task EnabledEventWithoutFullActorIdentityIsDropped(string? source)
	{
		_reality.IsEnabledAsync(Arg.Any<CancellationToken>()).Returns(true);
		await _dispatcher.DispatchAsync(new(_room.ToString(), RoomEventType.Say, "Secret name", "Secret text", source));
		_hub.Clients.DidNotReceive().Group(Arg.Any<string>());
		_hub.Clients.DidNotReceive().Client(Arg.Any<string>());
	}

	[Test]
	public async Task DisabledModePreservesLegacyRoomEvents()
	{
		var message = new RoomEventMessage(_room.ToString(), RoomEventType.Arrive, "Actor", "arrived");
		await _dispatcher.DispatchAsync(message);
		await _hub.Clients.Group(GameHub.RoomGroupName(_room)).Received(1).ReceiveRoomEvent(message);
	}

	[Test]
	public async Task FailedRecipientDoesNotPreventDeliveryToOtherObservers()
	{
		_reality.IsEnabledAsync(Arg.Any<CancellationToken>()).Returns(true);
		Subscribe("first", 30);
		Subscribe("second", 31);
		_projection.CanReceiveRoomEventAsync(Arg.Any<CapabilityActor>(), _room, _source, RoomEventType.Say, Arg.Any<CancellationToken>()).Returns(true);
		var client = Substitute.For<IGameHubClient>();
		_hub.Clients.Client(Arg.Any<string>()).Returns(client);
		var attempts = 0;
		client.ReceiveRoomEvent(Arg.Any<RoomEventMessage>()).Returns(_ => ++attempts == 1
			? Task.FromException(new IOException("Disconnected client")) : Task.CompletedTask);
		await _dispatcher.DispatchAsync(new(_room.ToString(), RoomEventType.Say, "Actor", "Words", _source.ToString()));
		await Assert.That(attempts).IsEqualTo(2);
	}

	[Test]
	public async Task RejoiningDuringAuthorizationInvalidatesTheCapturedSubscription()
	{
		_reality.IsEnabledAsync(Arg.Any<CancellationToken>()).Returns(true);
		var actor = Subscribe("rejoined", 30);
		_projection.CanReceiveRoomEventAsync(actor, _room, _source, RoomEventType.Say, Arg.Any<CancellationToken>())
			.Returns(_ => { _registry.JoinRoom("rejoined", actor, _room); return ValueTask.FromResult(true); });
		await _dispatcher.DispatchAsync(new(_room.ToString(), RoomEventType.Say, "Actor", "Secret", _source.ToString()));
		_hub.Clients.DidNotReceive().Client("rejoined");
	}

	[Test]
	public async Task LeavingDuringAuthorizationCannotDeliverAnOldSubscription()
	{
		_reality.IsEnabledAsync(Arg.Any<CancellationToken>()).Returns(true);
		var actor = Subscribe("left", 30);
		_projection.CanReceiveRoomEventAsync(actor, _room, _source, RoomEventType.Say, Arg.Any<CancellationToken>())
			.Returns(_ => { _registry.LeaveRoom("left", _room); return ValueTask.FromResult(true); });
		await _dispatcher.DispatchAsync(new(_room.ToString(), RoomEventType.Say, "Actor", "Secret", _source.ToString()));
		_hub.Clients.DidNotReceive().Client("left");
		_hub.Clients.DidNotReceive().Group(Arg.Any<string>());
	}
}
