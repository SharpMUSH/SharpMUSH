using Microsoft.Extensions.DependencyInjection;
using NATS.Client.Core;
using NATS.Client.Serializers.Json;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.Messages;
using System.Text;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// Proves the Task 12 <c>@BOOT</c> bonus fix: <c>ConnectionService.Disconnect</c> alone only
/// updates server-side connection state and never closes the actual socket (see the comment on
/// the QUIT command in <c>SocketCommands.cs</c>), so <c>@BOOT</c> must also publish
/// <see cref="DisconnectConnectionMessage"/> for ConnectionServer to tear down the socket.
/// <para>
/// This subscribes directly to the real NATS server backing the shared test host (rather than
/// substituting <c>IMessageBus</c>, which is a process-wide static on the <c>Commands</c> partial
/// shared by every test in this session — see <c>BanEnforcementTests</c>' remarks on why
/// substituting shared-host services can leak across tests) and asserts the message actually
/// lands on the wire when <c>@boot/port</c> runs.
/// </para>
/// </summary>
public class WizardCommandsBootTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private SharpMUSH.Library.ParserInterfaces.IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;

	[Test]
	public async ValueTask Boot_PublishesDisconnectConnectionMessage()
	{
		var natsUrl = Environment.GetEnvironmentVariable("NATS_URL");
		await Assert.That(natsUrl).IsNotNull();

		var handle = Random.Shared.NextInt64(900_000, 999_999);
		await ConnectionService.Register(handle, "127.0.0.1", "localhost", "test",
			_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => Encoding.UTF8);

		await using var nats = new NatsConnection(new NatsOpts { Url = natsUrl! });
		await nats.ConnectAsync();

		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
		var received = new TaskCompletionSource<DisconnectConnectionMessage>(TaskCreationOptions.RunContinuationsAsynchronously);

		var subscribeTask = Task.Run(async () =>
		{
			await foreach (var msg in nats.SubscribeAsync<DisconnectConnectionMessage>(
				"sharpmush.ms.disconnect-connection",
				serializer: NatsJsonSerializer<DisconnectConnectionMessage>.Default,
				cancellationToken: cts.Token))
			{
				if (msg.Data is { } data && data.Handle == handle)
				{
					received.TrySetResult(data);
					return;
				}
			}
		}, cts.Token);

		// Give the SUB frame time to reach the (real) NATS server before publishing, otherwise the
		// publish can race the subscription and be missed.
		await Task.Delay(TimeSpan.FromMilliseconds(500));

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@boot/port {handle}"));

		var message = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
		await Assert.That(message.Handle).IsEqualTo(handle);
		await Assert.That(message.Reason).IsEqualTo("BOOT");

		await cts.CancelAsync();
		try
		{
			await subscribeTask;
		}
		catch (OperationCanceledException)
		{
			// Expected: cancelling the subscription's CancellationToken unwinds the SubscribeAsync loop.
		}
	}
}
