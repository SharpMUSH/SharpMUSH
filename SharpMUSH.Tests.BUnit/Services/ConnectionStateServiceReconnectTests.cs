using Microsoft.Extensions.Logging.Abstractions;
using SharpMUSH.Client.Services;
using SharpMUSH.Tests.Shared;

namespace SharpMUSH.Tests.BUnit.Services;

file sealed class CountingHubFactory : IGameHubConnectionFactory
{
	public int CreateCount { get; private set; }
	public List<FakeGameHubConnection> Hubs { get; } = [];

	public IGameHubConnection Create()
	{
		CreateCount++;
		var hub = new FakeGameHubConnection();
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
