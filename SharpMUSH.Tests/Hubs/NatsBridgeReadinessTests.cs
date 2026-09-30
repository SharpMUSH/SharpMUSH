using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NATS.Client.Core;
using NATS.Client.Serializers.Json;
using NSubstitute;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Portal;
using SharpMUSH.Messaging.NATS;
using SharpMUSH.Server.Hubs;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Hubs;

/// <summary>
/// The bridge against a real broker: once it reports ready, output published to <c>game.output.*</c>
/// reaches the character's SignalR group.
/// <para>What this does not prove is the ordering that motivated it. The bridge subscribes, then waits a
/// PING round trip, then reports ready, because core NATS does not replay and readiness is what lets a
/// portal start sending commands. On a local broker the SUB lands microseconds after the connect, so a
/// bridge that reported ready first would pass this too (checked: five runs with the flip moved ahead of
/// the subscriptions, all green). The ordering holds by construction in <c>NatsBridgeService</c>.</para>
/// </summary>
public class NatsBridgeReadinessTests
{
	[ClassDataSource<NatsTestServer>(Shared = SharedType.PerTestSession)]
	public required NatsTestServer Nats { get; init; }

	[Test]
	public async Task OutputPublishedAsSoonAsTheBridgeIsReady_ReachesThePortal()
	{
		var url = $"nats://{Nats.Instance.Hostname}:{Nats.Instance.GetMappedPublicPort(4222)}";
		var character = new DBRef(7, 1700000000);
		var received = new TaskCompletionSource<GameOutputMessage>(TaskCreationOptions.RunContinuationsAsynchronously);

		var hub = Substitute.For<IHubContext<GameHub, IGameHubClient>>();
		var proxy = Substitute.For<IGameHubClient>();
		hub.Clients.Group(GameHub.CharacterGroupName(character)).Returns(proxy);
		proxy.ReceiveOutput(Arg.Any<GameOutputMessage>())
			.Returns(call => { received.TrySetResult(call.Arg<GameOutputMessage>()); return Task.CompletedTask; });

		var readiness = new ServerReadiness(new NatsConsumerRegistry(), Substitute.For<IHostApplicationLifetime>());
		var service = new NatsBridgeService(hub, Substitute.For<IHubContext<GameHub>>(), new NatsOptions { Url = url },
			SharpMUSH.Implementation.Services.PluginCatalog.Empty(), NullLogger<NatsBridgeService>.Instance,
			Substitute.For<IRoomEventDispatcher>(), readiness);

		await using var publisher = new NatsConnection(new NatsOpts { Url = url });
		await publisher.ConnectAsync();
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
		await service.StartAsync(timeout.Token);
		try
		{
			while (readiness.Pending().Contains("output-bridge"))
			{
				await Task.Delay(1, timeout.Token);
			}

			var message = new GameOutputMessage(character.ToString(), "first words", DateTimeOffset.UtcNow, MessageType.Normal);
			await publisher.PublishAsync($"game.output.{character.Number}", message,
				serializer: NatsJsonSerializer<GameOutputMessage>.Default, cancellationToken: timeout.Token);

			var delivered = await received.Task.WaitAsync(TimeSpan.FromSeconds(10), timeout.Token);
			await Assert.That(delivered.Content).IsEqualTo("first words");
		}
		finally
		{
			await service.StopAsync(CancellationToken.None);
		}
	}
}
