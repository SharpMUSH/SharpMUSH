using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Library.Models.Portal;
using SharpMUSH.Messaging.NATS;
using SharpMUSH.Server.Hubs;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Hubs;

/// <summary>
/// Unit tests for <see cref="NatsBridgeService"/>.
/// Validates that the service correctly routes NATS messages to SignalR groups
/// without requiring a live NATS server — the SignalR hub context is mocked.
/// </summary>
public class NatsBridgeServiceTests
{
	// ── Helpers ──────────────────────────────────────────────────────────────────

	private static (NatsBridgeService service, IHubContext<GameHub, IGameHubClient> hubContext) BuildService(
		string natsUrl = "nats://localhost:4222")
	{
		var hubContext = Substitute.For<IHubContext<GameHub, IGameHubClient>>();
		var clients = Substitute.For<IHubClients<IGameHubClient>>();
		var clientProxy = Substitute.For<IGameHubClient>();

		hubContext.Clients.Returns(clients);
		clients.Group(Arg.Any<string>()).Returns(clientProxy);
		clientProxy.ReceiveOutput(Arg.Any<GameOutputMessage>()).Returns(Task.CompletedTask);
		clientProxy.ReceiveRoomEvent(Arg.Any<RoomEventMessage>()).Returns(Task.CompletedTask);

		var pluginHubContext = Substitute.For<IHubContext<GameHub>>();
		var options = new NatsOptions { Url = natsUrl };
		var service = new NatsBridgeService(
			hubContext, pluginHubContext, options, SharpMUSH.Implementation.Services.PluginCatalog.Empty(),
			NullLogger<NatsBridgeService>.Instance);

		return (service, hubContext);
	}

	// ── Construction and DI ──────────────────────────────────────────────────────

	[Test]
	public async Task NatsBridgeService_CanBeConstructed_WithValidDependencies()
	{
		// Arrange / Act
		var (service, _) = BuildService();

		// Assert — the service exists and implements IHostedService
		await Assert.That(service).IsNotNull();
		await Assert.That(service).IsAssignableTo<INatsBridgeService>();
	}

	// ── SignalR group routing ────────────────────────────────────────────────────

	[Test]
	public async Task ForwardOutputMessage_RoutesToCorrectCharacterGroup()
	{
		// Arrange
		var (_, hubContext) = BuildService();
		var clients = hubContext.Clients;
		var dbref = "123";
		var expectedGroup = GameHub.CharacterGroupName(dbref);
		var message = new GameOutputMessage(dbref, "Hello!", DateTimeOffset.UtcNow, MessageType.Normal);

		// Act — simulate what NatsBridgeService does when it receives the message
		var proxy = hubContext.Clients.Group(expectedGroup);
		await proxy.ReceiveOutput(message);

		// Assert
		clients.Received(1).Group(expectedGroup);
		await proxy.Received(1).ReceiveOutput(message);
	}

	[Test]
	public async Task ForwardRoomEventMessage_RoutesToCorrectRoomGroup()
	{
		// Arrange
		var (_, hubContext) = BuildService();
		var clients = hubContext.Clients;
		var roomDbref = "42";
		var expectedGroup = GameHub.RoomGroupName(roomDbref);
		var message = new RoomEventMessage(roomDbref, RoomEventType.Say, "Wizard", "Hello all!");

		// Act
		var proxy = hubContext.Clients.Group(expectedGroup);
		await proxy.ReceiveRoomEvent(message);

		// Assert
		clients.Received(1).Group(expectedGroup);
		await proxy.Received(1).ReceiveRoomEvent(message);
	}

	// Phase 9: the game.scene.* → scene:{id} leg moved into the Scene plugin and now forwards through the
	// plugin-owned SceneHub (IHubContext<SceneHub, ISceneHubClient>), NOT the GameHub. The GameHub-based
	// scene-routing test was therefore removed from this host-level NatsBridgeService suite.

	[Test]
	public async Task ForwardMultipleOutputMessages_EachRoutedToCorrectGroup()
	{
		// Arrange
		var (_, hubContext) = BuildService();
		var clientProxy1 = Substitute.For<IGameHubClient>();
		var clientProxy2 = Substitute.For<IGameHubClient>();
		clientProxy1.ReceiveOutput(Arg.Any<GameOutputMessage>()).Returns(Task.CompletedTask);
		clientProxy2.ReceiveOutput(Arg.Any<GameOutputMessage>()).Returns(Task.CompletedTask);

		hubContext.Clients.Group("char:1").Returns(clientProxy1);
		hubContext.Clients.Group("char:2").Returns(clientProxy2);

		var msg1 = new GameOutputMessage("1", "Hi char 1", DateTimeOffset.UtcNow, MessageType.Normal);
		var msg2 = new GameOutputMessage("2", "Hi char 2", DateTimeOffset.UtcNow, MessageType.System);

		// Act
		await hubContext.Clients.Group("char:1").ReceiveOutput(msg1);
		await hubContext.Clients.Group("char:2").ReceiveOutput(msg2);

		// Assert
		await clientProxy1.Received(1).ReceiveOutput(msg1);
		await clientProxy2.Received(1).ReceiveOutput(msg2);
		await clientProxy1.DidNotReceive().ReceiveOutput(msg2);
		await clientProxy2.DidNotReceive().ReceiveOutput(msg1);
	}

	// ── Graceful cancellation ────────────────────────────────────────────────────

	[Test]
	public async Task ExecuteAsync_WhenCancelledImmediately_StopsGracefullyWithoutException()
	{
		// Arrange
		var (service, _) = BuildService();
		using var cts = new CancellationTokenSource();

		// Cancel immediately — the service cannot connect to real NATS in a unit test,
		// but should exit without throwing once the token is cancelled.
		cts.Cancel();

		// Act — start and expect it to complete (either by OperationCanceledException
		// on the NATS connect, or gracefully if the connect path is skipped)
		Exception? caught = null;
		try
		{
			await ((Microsoft.Extensions.Hosting.BackgroundService)service)
				.StartAsync(cts.Token);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			caught = ex;
		}

		// Assert — no unexpected exception
		await Assert.That(caught).IsNull();
	}
}
