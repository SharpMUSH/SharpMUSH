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

	[Test]
	public async Task ReconnectAsync_WhenNotConnected_DoesNothing()
	{
		var factory = new CountingHubFactory();
		var service = new ConnectionStateService(factory, NullLogger<ConnectionStateService>.Instance);

		await service.ReconnectAsync();

		await Assert.That(factory.CreateCount).IsEqualTo(0);
	}
}
