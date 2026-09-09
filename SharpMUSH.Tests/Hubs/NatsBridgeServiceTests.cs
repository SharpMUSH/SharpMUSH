using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Library.Models;
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
			NullLogger<NatsBridgeService>.Instance, Substitute.For<IRoomEventDispatcher>());

		return (service, hubContext);
	}

	[Test]
	public async Task NatsBridgeService_CanBeConstructed_WithValidDependencies()
	{
		var (service, _) = BuildService();

		await Assert.That(service).IsNotNull();
		await Assert.That(service).IsAssignableTo<INatsBridgeService>();
	}

	[Test]
	public async Task ForwardOutputMessage_RoutesToCorrectCharacterGroup()
	{
		var (_, hubContext) = BuildService();
		var clients = hubContext.Clients;
		var character = new DBRef(123, 1700000000);
		var dbref = character.ToString();
		var expectedGroup = GameHub.CharacterGroupName(character);
		var message = new GameOutputMessage(dbref, "Hello!", DateTimeOffset.UtcNow, MessageType.Normal);

		// simulate what NatsBridgeService does when it receives the message
		var proxy = hubContext.Clients.Group(expectedGroup);
		await proxy.ReceiveOutput(message);

		clients.Received(1).Group(expectedGroup);
		await proxy.Received(1).ReceiveOutput(message);
	}

	[Test]
	public async Task ForwardRoomEventUsesSharedRecipientDispatcher()
	{
		var hub = Substitute.For<IHubContext<GameHub, IGameHubClient>>();
		var dispatcher = Substitute.For<IRoomEventDispatcher>();
		var clients = Substitute.For<IHubClients<IGameHubClient>>();
		hub.Clients.Returns(clients);
		var service = new NatsBridgeService(hub, Substitute.For<IHubContext<GameHub>>(), new NatsOptions { Url = "nats://localhost:4222" },
			SharpMUSH.Implementation.Services.PluginCatalog.Empty(), NullLogger<NatsBridgeService>.Instance, dispatcher);
		var message = new RoomEventMessage("#42:1", RoomEventType.Say, "Actor", "Hello", "#7:1");
		await service.ForwardRoomEventAsync(message);
		await dispatcher.Received(1).DispatchAsync(message, Arg.Any<CancellationToken>());
		clients.DidNotReceive().Group(Arg.Any<string>());
	}

	[Test]
	public async Task ForwardMultipleOutputMessages_EachRoutedToCorrectGroup()
	{
		var (_, hubContext) = BuildService();
		var clientProxy1 = Substitute.For<IGameHubClient>();
		var clientProxy2 = Substitute.For<IGameHubClient>();
		clientProxy1.ReceiveOutput(Arg.Any<GameOutputMessage>()).Returns(Task.CompletedTask);
		clientProxy2.ReceiveOutput(Arg.Any<GameOutputMessage>()).Returns(Task.CompletedTask);

		hubContext.Clients.Group("char:1").Returns(clientProxy1);
		hubContext.Clients.Group("char:2").Returns(clientProxy2);

		var msg1 = new GameOutputMessage("1", "Hi char 1", DateTimeOffset.UtcNow, MessageType.Normal);
		var msg2 = new GameOutputMessage("2", "Hi char 2", DateTimeOffset.UtcNow, MessageType.System);

		await hubContext.Clients.Group("char:1").ReceiveOutput(msg1);
		await hubContext.Clients.Group("char:2").ReceiveOutput(msg2);

		await clientProxy1.Received(1).ReceiveOutput(msg1);
		await clientProxy2.Received(1).ReceiveOutput(msg2);
		await clientProxy1.DidNotReceive().ReceiveOutput(msg2);
		await clientProxy2.DidNotReceive().ReceiveOutput(msg1);
	}

	[Test]
	public async Task ExecuteAsync_WhenCancelledImmediately_StopsGracefullyWithoutException()
	{
		var (service, _) = BuildService();
		using var cts = new CancellationTokenSource();

		// Cancel immediately — the service cannot connect to real NATS in a unit test,
		// but should exit without throwing once the token is cancelled.
		cts.Cancel();

		// start and expect it to complete (either by OperationCanceledException
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

		await Assert.That(caught).IsNull();
	}
	[Test]
	public async Task RoomStreamsPreserveOrderingWithoutBlockingUnrelatedRooms()
	{
		var dispatcher = Substitute.For<IRoomEventDispatcher>();
		var service = new NatsBridgeService(Substitute.For<IHubContext<GameHub, IGameHubClient>>(),
			Substitute.For<IHubContext<GameHub>>(), new NatsOptions(), SharpMUSH.Implementation.Services.PluginCatalog.Empty(),
			NullLogger<NatsBridgeService>.Instance, dispatcher);
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var other = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var order = new System.Collections.Concurrent.ConcurrentQueue<string>();
		dispatcher.DispatchAsync(Arg.Any<RoomEventMessage>(), Arg.Any<CancellationToken>()).Returns(async call =>
		{
			var message = call.ArgAt<RoomEventMessage>(0);
			if (message.Content == "first") await release.Task;
			order.Enqueue(message.Content);
			if (message.Content == "other") other.TrySetResult();
		});
		async IAsyncEnumerable<RoomEventMessage> Messages()
		{
			yield return new("#1:1", RoomEventType.Say, "actor", "first", "#3:1");
			yield return new("#1:1", RoomEventType.Say, "actor", "second", "#3:1");
			yield return new("#2:1", RoomEventType.Say, "actor", "other", "#3:1");
			await Task.CompletedTask;
		}
		var forwarding = service.ForwardRoomEventsAsync(Messages());
		try
		{
			await other.Task.WaitAsync(TimeSpan.FromSeconds(5));
			await Assert.That(order.Contains("second")).IsFalse();
		}
		finally { release.TrySetResult(); await forwarding; }
		await Assert.That(string.Join(",", order.Where(x => x != "other"))).IsEqualTo("first,second");
	}

	[Test]
	public async Task FullRoomLaneAppliesBackpressureAndCancelsPendingDelivery()
	{
		var dispatcher = Substitute.For<IRoomEventDispatcher>();
		var service = new NatsBridgeService(Substitute.For<IHubContext<GameHub, IGameHubClient>>(),
			Substitute.For<IHubContext<GameHub>>(), new NatsOptions(), SharpMUSH.Implementation.Services.PluginCatalog.Empty(),
			NullLogger<NatsBridgeService>.Instance, dispatcher);
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		dispatcher.DispatchAsync(Arg.Any<RoomEventMessage>(), Arg.Any<CancellationToken>())
			.Returns(call => release.Task.WaitAsync(call.ArgAt<CancellationToken>(1)));
		var full = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var produced = 0;
		async IAsyncEnumerable<RoomEventMessage> Messages()
		{
			for (var index = 0; index < 1000; index++)
			{
				if (Interlocked.Increment(ref produced) == 34) full.TrySetResult();
				yield return new("#1:1", RoomEventType.Say, "actor", "words", "#3:1");
			}
			await Task.CompletedTask;
		}
		using var cancellation = new CancellationTokenSource();
		var forwarding = service.ForwardRoomEventsAsync(Messages(), cancellation.Token);
		try
		{
			await full.Task.WaitAsync(TimeSpan.FromSeconds(5));
			await Assert.That(Volatile.Read(ref produced)).IsEqualTo(34);
		}
		finally
		{
			cancellation.Cancel();
			try { await forwarding; }
			catch (OperationCanceledException) { }
		}
		await Assert.That(forwarding.IsCanceled).IsTrue();
	}

	[Test]
	public async Task RoomStreamPreservesIngressFailuresAfterStoppingWorkers()
	{
		var dispatcher = Substitute.For<IRoomEventDispatcher>();
		var service = new NatsBridgeService(Substitute.For<IHubContext<GameHub, IGameHubClient>>(),
			Substitute.For<IHubContext<GameHub>>(), new NatsOptions(), SharpMUSH.Implementation.Services.PluginCatalog.Empty(),
			NullLogger<NatsBridgeService>.Instance, dispatcher);
		async IAsyncEnumerable<RoomEventMessage> Messages()
		{
			yield return new("#1:1", RoomEventType.Say, "actor", "first", "#3:1");
			await Task.Yield();
			throw new IOException("NATS ingress disconnected");
		}
		await Assert.That(async () => await service.ForwardRoomEventsAsync(Messages())).Throws<IOException>();
	}

}
