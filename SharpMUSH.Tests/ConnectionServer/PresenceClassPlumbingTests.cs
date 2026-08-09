using System.Collections.Concurrent;
using System.Text;
using Mediator;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.ConnectionServer.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.Abstractions;
using SharpMUSH.Messaging.Messages;

namespace SharpMUSH.Tests.ConnectionServer;

/// <summary>
/// The presence-class plumbing: every connection declares "play" (a real interactive session) or
/// "portal" (a background query connection). The default is "play" everywhere, so telnet and older
/// publishers keep counting as normal sessions.
/// </summary>
public class PresenceClassPlumbingTests
{
	[Test]
	public async Task ConnectionEstablishedMessage_defaults_presence_class_to_play()
	{
		var message = new ConnectionEstablishedMessage(1, "1.2.3.4", "host", "telnet", DateTimeOffset.UtcNow);
		await Assert.That(message.PresenceClass).IsEqualTo("play");
	}

	[Test]
	public async Task ConnectionEstablishedMessage_carries_an_explicit_presence_class()
	{
		var message = new ConnectionEstablishedMessage(1, "1.2.3.4", "host", "websocket", DateTimeOffset.UtcNow, "portal");
		await Assert.That(message.PresenceClass).IsEqualTo("portal");
	}

	[Test]
	public async Task ConnectionData_presence_class_defaults_to_play_when_metadata_is_absent()
	{
		var metadata = new ConcurrentDictionary<string, string>
		{
			["ConnectionType"] = "telnet"
		};
		var data = new IConnectionService.ConnectionData(
			1, null, IConnectionService.ConnectionState.Connected,
			_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => Encoding.UTF8, metadata);

		await Assert.That(data.PresenceClass).IsEqualTo("play");
	}

	[Test]
	public async Task ConnectionData_presence_class_reflects_metadata_when_present()
	{
		var metadata = new ConcurrentDictionary<string, string>
		{
			["ConnectionType"] = "websocket",
			["PresenceClass"] = "portal"
		};
		var data = new IConnectionService.ConnectionData(
			1, null, IConnectionService.ConnectionState.Connected,
			_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => Encoding.UTF8, metadata);

		await Assert.That(data.PresenceClass).IsEqualTo("portal");
	}

	[Test]
	public async Task RegisterAsync_publishes_the_presence_class_on_the_established_message()
	{
		var bus = Substitute.For<IMessageBus>();
		var service = new ConnectionServerService(NullLogger<ConnectionServerService>.Instance, bus);

		await service.RegisterAsync(
			42, "1.2.3.4", "host", "websocket",
			_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => Encoding.UTF8, () => { },
			presenceClass: "portal");

		await bus.Received(1).Publish(
			Arg.Is<ConnectionEstablishedMessage>(m => m.Handle == 42 && m.PresenceClass == "portal"),
			Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task RegisterAsync_defaults_the_published_presence_class_to_play()
	{
		var bus = Substitute.For<IMessageBus>();
		var service = new ConnectionServerService(NullLogger<ConnectionServerService>.Instance, bus);

		await service.RegisterAsync(
			43, "1.2.3.4", "host", "telnet",
			_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => Encoding.UTF8, () => { });

		await bus.Received(1).Publish(
			Arg.Is<ConnectionEstablishedMessage>(m => m.Handle == 43 && m.PresenceClass == "play"),
			Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// The presence class was forwarded on the NATS message but dropped from the ConnectionServer's
	/// own session record, so anything reading it back locally saw the "play" default.
	/// </summary>
	[Test]
	public async Task RegisterAsync_records_the_presence_class_on_the_session_it_keeps()
	{
		var bus = Substitute.For<IMessageBus>();
		var service = new ConnectionServerService(NullLogger<ConnectionServerService>.Instance, bus);

		await service.RegisterAsync(
			44, "1.2.3.4", "host", "websocket",
			_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => Encoding.UTF8, () => { },
			presenceClass: "portal");

		await Assert.That(service.Get(44)!.PresenceClass).IsEqualTo("portal");
	}

	/// <summary>
	/// The failure N-13 describes, reproduced without a process restart: the ConnectionServer
	/// persists a portal connection, the engine rebuilds it from that store, and the rebuilt
	/// connection must still be portal-class. The store omitted <c>PresenceClass</c> entirely, and
	/// <c>IConnectionService.ConnectionData.PresenceClass</c> defaults a missing key to "play", so
	/// the reconciled connection came back visible to a mortal WHO.
	/// </summary>
	[Test]
	public async Task APortalConnectionSurvivesAStateStoreRoundTripAsPortal()
	{
		var store = new InMemoryConnectionStateStore();
		var bus = Substitute.For<IMessageBus>();
		var connectionServer = new ConnectionServerService(NullLogger<ConnectionServerService>.Instance, bus, store);

		await connectionServer.RegisterAsync(
			45, "1.2.3.4", "host", "websocket",
			_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => Encoding.UTF8, () => { },
			presenceClass: "portal");

		var engine = new SharpMUSH.Library.Services.ConnectionService(Substitute.For<IPublisher>(), store);
		await engine.ReconcileFromStateStoreAsync(
			_ => _ => ValueTask.CompletedTask, _ => _ => ValueTask.CompletedTask, () => Encoding.UTF8);

		await Assert.That(engine.Get(45)!.PresenceClass).IsEqualTo("portal");
	}

	[Test]
	public async Task ATelnetConnectionSurvivesAStateStoreRoundTripAsPlay()
	{
		var store = new InMemoryConnectionStateStore();
		var bus = Substitute.For<IMessageBus>();
		var connectionServer = new ConnectionServerService(NullLogger<ConnectionServerService>.Instance, bus, store);

		await connectionServer.RegisterAsync(
			46, "1.2.3.4", "host", "telnet",
			_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => Encoding.UTF8, () => { });

		var engine = new SharpMUSH.Library.Services.ConnectionService(Substitute.For<IPublisher>(), store);
		await engine.ReconcileFromStateStoreAsync(
			_ => _ => ValueTask.CompletedTask, _ => _ => ValueTask.CompletedTask, () => Encoding.UTF8);

		await Assert.That(engine.Get(46)!.PresenceClass).IsEqualTo("play");
	}

	/// <summary>The store contract only; no Redis, no container.</summary>
	private sealed class InMemoryConnectionStateStore : IConnectionStateStore
	{
		private readonly ConcurrentDictionary<long, ConnectionStateData> _state = [];

		public Task SetConnectionAsync(long handle, ConnectionStateData data, CancellationToken ct = default)
		{
			_state[handle] = data;
			return Task.CompletedTask;
		}

		public Task<ConnectionStateData?> GetConnectionAsync(long handle, CancellationToken ct = default) =>
			Task.FromResult(_state.GetValueOrDefault(handle));

		public Task RemoveConnectionAsync(long handle, CancellationToken ct = default)
		{
			_state.TryRemove(handle, out _);
			return Task.CompletedTask;
		}

		public Task<IEnumerable<long>> GetAllHandlesAsync(CancellationToken ct = default) =>
			Task.FromResult<IEnumerable<long>>(_state.Keys.ToArray());

		public Task<IEnumerable<(long Handle, ConnectionStateData Data)>> GetAllConnectionsAsync(
			CancellationToken ct = default) =>
			Task.FromResult<IEnumerable<(long, ConnectionStateData)>>(
				_state.Select(x => (x.Key, x.Value)).ToArray());

		public Task SetPlayerBindingAsync(long handle, string? playerObjid, CancellationToken ct = default)
		{
			if (_state.TryGetValue(handle, out var data)) data.PlayerObjid = playerObjid;
			return Task.CompletedTask;
		}

		public Task UpdateMetadataAsync(long handle, string key, string value, CancellationToken ct = default)
		{
			if (_state.TryGetValue(handle, out var data)) data.Metadata[key] = value;
			return Task.CompletedTask;
		}
	}
}
