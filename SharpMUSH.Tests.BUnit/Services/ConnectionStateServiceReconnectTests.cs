using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging.Abstractions;
using SharpMUSH.Client.Models;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.Models.Portal;

namespace SharpMUSH.Tests.BUnit.Services;

file sealed class FakeHubConnection : IGameHubConnection
{
	public int StartCount { get; private set; }
	public int StopCount { get; private set; }
	public HubConnectionState State { get; private set; } = HubConnectionState.Disconnected;

	public Task StartAsync(CancellationToken cancellationToken = default)
	{
		StartCount++;
		State = HubConnectionState.Connected;
		return Task.CompletedTask;
	}

	public Task StopAsync(CancellationToken cancellationToken = default)
	{
		StopCount++;
		State = HubConnectionState.Disconnected;
		return Task.CompletedTask;
	}

	public Task InvokeAsync(string methodName, string arg, CancellationToken cancellationToken = default) => Task.CompletedTask;
	public IDisposable On(string methodName, Action<GameOutputMessage> handler) => new Noop();
	public IDisposable On(string methodName, Action<RoomEventMessage> handler) => new Noop();
	public IDisposable On(string methodName, Action<SceneEventMessage> handler) => new Noop();
	public IDisposable On(string methodName, Action handler) => new Noop();
	public event Func<Exception?, Task>? Closed;
	public event Func<Exception?, Task>? Reconnecting;
	public event Func<string?, Task>? Reconnected;
	public ValueTask DisposeAsync() => ValueTask.CompletedTask;

	private sealed class Noop : IDisposable { public void Dispose() { } }
}

file sealed class CountingHubFactory : IGameHubConnectionFactory
{
	public int CreateCount { get; private set; }
	public List<FakeHubConnection> Hubs { get; } = [];

	public IGameHubConnection Create()
	{
		CreateCount++;
		var hub = new FakeHubConnection();
		Hubs.Add(hub);
		return hub;
	}

	public IGameHubConnection? CreateScene() => null;
}

/// <summary>
/// Pins that reconnecting the game hub tears down the current connection and builds a fresh one from
/// the factory — the "reconnect as new character" path a character switch uses, so the new connection
/// re-reads the active character the factory encodes on the URL.
/// </summary>
public class ConnectionStateServiceReconnectTests
{
	[Test]
	public async Task ReconnectAsync_WhenConnected_StopsOldAndBuildsFresh()
	{
		var factory = new CountingHubFactory();
		var service = new ConnectionStateService(factory, NullLogger<ConnectionStateService>.Instance);
		await service.ConnectAsync();

		await Assert.That(factory.CreateCount).IsEqualTo(1);
		await Assert.That(factory.Hubs[0].StartCount).IsEqualTo(1);

		await service.ReconnectAsync();

		await Assert.That(factory.Hubs[0].StopCount).IsEqualTo(1);
		await Assert.That(factory.CreateCount).IsEqualTo(2);
		await Assert.That(factory.Hubs[1].StartCount).IsEqualTo(1);
	}

	/// <summary>
	/// Every caller of <c>ReconnectAsync</c> — character creation, terminal login, character switch —
	/// reaches it on a session that has never held a hub, and <c>ConnectAsync</c> has no other caller.
	/// A reconnect that no-ops on a null hub therefore means the game hub is never connected at all:
	/// no live scene poses, no room events, and a permanently disabled compose box on
	/// <c>/scenes/{id}/live</c>. So "not connected yet" has to mean connect, not do nothing.
	/// </summary>
	[Test]
	public async Task ReconnectAsync_WhenNeverConnected_ConnectsFresh()
	{
		var factory = new CountingHubFactory();
		var service = new ConnectionStateService(factory, NullLogger<ConnectionStateService>.Instance);

		await service.ReconnectAsync();

		await Assert.That(factory.CreateCount).IsEqualTo(1);
		await Assert.That(factory.Hubs[0].StartCount).IsEqualTo(1);
		await Assert.That(service.IsConnected).IsTrue();
	}

	/// <summary>
	/// The first connect has nothing to tear down. Stopping a hub that was never started would push a
	/// disposed/never-started connection through <c>StopAsync</c>, which SignalR answers with an
	/// <see cref="InvalidOperationException"/> the service would then have to swallow.
	/// </summary>
	[Test]
	public async Task ReconnectAsync_WhenNeverConnected_DoesNotStopAnything()
	{
		var factory = new CountingHubFactory();
		var service = new ConnectionStateService(factory, NullLogger<ConnectionStateService>.Instance);

		await service.ReconnectAsync();

		await Assert.That(factory.Hubs[0].StopCount).IsEqualTo(0);
	}
}
