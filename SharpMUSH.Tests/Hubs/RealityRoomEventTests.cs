using Microsoft.Extensions.Logging;
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
	private readonly ILogger<RoomEventDispatcher> _logger = Substitute.For<ILogger<RoomEventDispatcher>>();

	public RealityRoomEventTests()
	{
		_hub.Clients.Returns(Substitute.For<IHubClients<IGameHubClient>>());
		_hub.Clients.Group(Arg.Any<string>()).Returns(Substitute.For<IGameHubClient>());
		_hub.Clients.Client(Arg.Any<string>()).Returns(_ => Substitute.For<IGameHubClient>());
		_dispatcher = new(_hub, _registry, _projection, _reality, _logger);
	}

	private CapabilityActor Subscribe(string connection, int character)
	{
		var actor = new CapabilityActor("account-" + character, new DBRef(character, 1), new DBRef(character, 1));
		_registry.Add(connection, actor.AccountId, "127.0.0.1", () => { });
		_registry.JoinRoom(connection, actor, _room);
		return actor;
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task AllModesUseRecipientChecksAndNeverTheRoomGroup(bool enabled)
	{
		_reality.IsEnabledAsync(Arg.Any<CancellationToken>()).Returns(enabled);
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
		await Assert.That(_logger.ReceivedCalls().Any(call => call.GetMethodInfo().Name == "Log"
			&& call.GetArguments()[0] is LogLevel.Warning)).IsTrue();
		_hub.Clients.DidNotReceive().Group(Arg.Any<string>());
		_hub.Clients.DidNotReceive().Client(Arg.Any<string>());
	}

	[Test]
	public async Task DisabledModePreservesLegacyRoomEvents()
	{
		var message = new RoomEventMessage(_room.ToString(), RoomEventType.Arrive, "Actor", "arrived");
		var actor = Subscribe("current", 30);
		Subscribe("stale", 31);
		_projection.CanSubscribeRoomAsync(actor, _room, Arg.Any<CancellationToken>()).Returns(true);
		var client = Substitute.For<IGameHubClient>();
		_hub.Clients.Client("current").Returns(client);
		await _dispatcher.DispatchAsync(message);
		await client.Received(1).ReceiveRoomEvent(message);
		_hub.Clients.DidNotReceive().Client("stale");
		_hub.Clients.DidNotReceive().Group(Arg.Any<string>());
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task FailedRecipientDoesNotPreventDeliveryToOtherObservers(bool canceled)
	{
		_reality.IsEnabledAsync(Arg.Any<CancellationToken>()).Returns(true);
		Subscribe("first", 30);
		Subscribe("second", 31);
		_projection.CanReceiveRoomEventAsync(Arg.Any<CapabilityActor>(), _room, _source, RoomEventType.Say, Arg.Any<CancellationToken>()).Returns(true);
		var client = Substitute.For<IGameHubClient>();
		_hub.Clients.Client(Arg.Any<string>()).Returns(client);
		var attempts = 0;
		client.ReceiveRoomEvent(Arg.Any<RoomEventMessage>()).Returns(_ => Interlocked.Increment(ref attempts) == 1
			? Task.FromException(canceled ? new OperationCanceledException("Disconnected client") : new IOException("Disconnected client")) : Task.CompletedTask);
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
	[Test]
	public async Task SlowRecipientDoesNotBlockOtherRecipients()
	{
		Subscribe("one", 30);
		Subscribe("two", 31);
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var second = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var calls = 0;
		async ValueTask<bool> Authorize()
		{
			if (Interlocked.Increment(ref calls) == 1) await release.Task;
			else second.TrySetResult();
			return true;
		}
		_projection.CanReceiveRoomEventAsync(Arg.Any<CapabilityActor>(), _room, _source, RoomEventType.Say, Arg.Any<CancellationToken>())
			.Returns(_ => Authorize());
		var delivery = _dispatcher.DispatchAsync(new(_room.ToString(), RoomEventType.Say, "Actor", "Words", _source.ToString()));
		try { await second.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
		finally { release.TrySetResult(); await delivery; }
	}

}
