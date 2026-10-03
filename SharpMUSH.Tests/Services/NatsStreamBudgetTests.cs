using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using SharpMUSH.Messaging.Messages;
using SharpMUSH.Messaging.NATS;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// Bus streams carry explicit byte budgets that every creator applies the same way (#1455), and hold
/// only unprocessed work rather than an archive of everything sent (#1456).
/// </summary>
public class NatsStreamBudgetTests
{
	[ClassDataSource<NatsTestServer>(Shared = SharedType.PerTestSession)]
	public required NatsTestServer NatsTestServer { get; init; }

	private string Url => $"nats://localhost:{NatsTestServer.Instance.GetMappedPublicPort(4222)}";

	private NatsOptions Options(string id, long maxBytes = NatsOptions.DefaultMaxBytes) => new()
	{
		Url = Url,
		StreamName = "BUDGET-" + id,
		SubjectPrefix = "budget." + id,
		MaxBytes = maxBytes,
		MaxMsgSize = (int)Math.Min(maxBytes, 6 * 1024 * 1024),
		AckWait = TimeSpan.FromSeconds(2),
	};

	private static string NewId() => Guid.NewGuid().ToString("N")[..12];

	[Test]
	public async Task Budgets_that_are_missing_or_contradictory_are_rejected()
	{
		await Assert.That(() => new NatsOptions { MaxBytes = 0 }.Validate()).Throws<ArgumentOutOfRangeException>();
		await Assert.That(() => new NatsOptions { MaxBytes = -1 }.Validate()).Throws<ArgumentOutOfRangeException>();
		await Assert.That(() => new NatsOptions { MaxBytes = 1024, MaxMsgSize = 2048 }.Validate()).Throws<ArgumentOutOfRangeException>();
		await Assert.That(() => new NatsOptions { MaxMsgs = 0 }.Validate()).Throws<ArgumentOutOfRangeException>();
		await Assert.That(() => new NatsOptions { MaxAge = TimeSpan.Zero }.Validate()).Throws<ArgumentOutOfRangeException>();
		await Assert.That(() => new NatsOptions { HandlerMaxAttempts = 0 }.Validate()).Throws<ArgumentOutOfRangeException>();
		await Assert.That(() => NatsStreamPolicy.BusStream("S", "s", new NatsOptions { MaxBytes = 0 })).Throws<ArgumentOutOfRangeException>();

		var configured = new NatsOptions();
		configured.ApplyEnvironment(name => name switch
		{
			"SHARPMUSH_NATS_MAX_BYTES" => "268435456",
			"SHARPMUSH_NATS_MAX_AGE" => "30m",
			_ => null
		});
		await Assert.That(configured.MaxBytes).IsEqualTo(268435456L);
		await Assert.That(configured.MaxAge).IsEqualTo(TimeSpan.FromMinutes(30));
		await Assert.That(() => new NatsOptions().ApplyEnvironment(name => name == "SHARPMUSH_NATS_MAX_BYTES" ? "1GB" : null))
			.Throws<FormatException>();
		await Assert.That(() => new NatsOptions().ApplyEnvironment(name => name == "SHARPMUSH_NATS_MAX_AGE" ? "soon" : null))
			.Throws<FormatException>();
		await Assert.That(() => new NatsOptions().ApplyEnvironment(name => name == "SHARPMUSH_NATS_MAX_AGE" ? "99999999999999d" : null))
			.Throws<FormatException>();
	}

	[Test]
	public async Task Bus_stream_is_created_with_a_byte_budget_interest_retention_and_discard_new()
	{
		var options = Options(NewId(), maxBytes: 8 * 1024 * 1024);
		await using var bus = await NatsJetStreamMessageBus.CreateAsync(options, NullLogger<NatsJetStreamMessageBus>.Instance);

		var config = await StreamConfigAsync(options.StreamName);

		await Assert.That(config.MaxBytes).IsEqualTo(8L * 1024 * 1024);
		await Assert.That(config.Retention).IsEqualTo(StreamConfigRetention.Interest);
		await Assert.That(config.Discard).IsEqualTo(StreamConfigDiscard.New);
		await Assert.That(config.MaxAge).IsEqualTo(options.MaxAge);
	}

	[Test]
	public async Task Acknowledged_messages_leave_the_bus_stream()
	{
		var id = NewId();
		var options = Options(id);
		var received = 0;
		var all = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var registry = Registry(options, "ack" + id, (_, _, _) =>
		{
			if (Interlocked.Increment(ref received) == 50) all.TrySetResult();
			return Task.CompletedTask;
		});
		await using var running = await StartAsync(registry, options);
		await using var bus = await NatsJetStreamMessageBus.CreateAsync(options, NullLogger<NatsJetStreamMessageBus>.Instance);

		for (var i = 0; i < 50; i++) await bus.Publish(new TelnetOutputMessage(i, new byte[512]));
		await all.Task.WaitAsync(TimeSpan.FromSeconds(15));

		// Transport retention is the unprocessed backlog, not a 24h copy of everything sent: browser
		// replay keeps its own archive.
		var state = await WaitForAsync(options.StreamName, info => info.State.Messages == 0);
		await Assert.That(state.State.Messages).IsEqualTo(0L);
		await Assert.That(state.State.Bytes).IsEqualTo(0L);
	}

	[Test]
	public async Task A_full_stream_refuses_new_work_and_keeps_the_unprocessed_backlog_through_a_consumer_outage()
	{
		var id = NewId();
		var options = Options(id, maxBytes: 16 * 1024);
		var firstPayload = new byte[1024];
		Random.Shared.NextBytes(firstPayload);
		var delivered = new TaskCompletionSource<TelnetOutputMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
		var registry = Registry(options, "outage" + id, (_, message, _) =>
		{
			delivered.TrySetResult((TelnetOutputMessage)message);
			return Task.CompletedTask;
		});
		using var metrics = new NatsMessagingMetrics();
		using var recorder = new MessagingMeterRecorder(metrics);
		await using var bus = await NatsJetStreamMessageBus.CreateAsync(options, NullLogger<NatsJetStreamMessageBus>.Instance,
			metrics: metrics);

		// The consumer exists (so the stream holds work for it) and then goes away.
		await (await StartAsync(registry, options)).DisposeAsync();

		await bus.Publish(new TelnetOutputMessage(0, firstPayload));
		var accepted = 1;
		NatsJSApiException? refusal = null;
		for (var i = 1; i < 64 && refusal is null; i++)
		{
			var payload = new byte[1024];
			Random.Shared.NextBytes(payload);
			try
			{
				await bus.Publish(new TelnetOutputMessage(i, payload));
				accepted++;
			}
			catch (NatsJSApiException ex)
			{
				refusal = ex;
			}
		}

		await Assert.That(refusal).IsNotNull();
		await Assert.That(accepted).IsLessThan(64);
		await Assert.That(recorder.Sum("sharpmush.messaging.publish.rejected", "stream", options.StreamName)).IsEqualTo(1L);
		await Assert.That(recorder.Sum("sharpmush.messaging.publish.rejected", "reason", "full")).IsEqualTo(1L);

		var monitor = new NatsBrokerMonitor(options, metrics, NullLogger<NatsBrokerMonitor>.Instance, registry);
		await using (var connection = new NatsConnection(new NatsOpts { Url = Url }))
		{
			var snapshot = await monitor.ReadAsync(new NatsJSContext(connection), CancellationToken.None);
			var stream = snapshot.Streams.Single(s => s.Name == options.StreamName);
			await Assert.That(stream.Messages).IsEqualTo((long)accepted);
			await Assert.That(stream.MaxBytes).IsEqualTo(16L * 1024);
			await Assert.That(snapshot.Consumers.Single().Pending).IsEqualTo((long)accepted);
			await Assert.That(snapshot.Storage is { Used: > 0 }).IsTrue();
		}

		// Discard-new kept the oldest unprocessed message: the restarted consumer gets it first.
		await using var restarted = await StartAsync(registry, options);
		var first = await delivered.Task.WaitAsync(TimeSpan.FromSeconds(15));
		await Assert.That(first.Handle).IsEqualTo(0L);
		await Assert.That(first.Data).IsEquivalentTo(firstPayload);
	}

	[Test]
	public async Task Consumer_creates_a_missing_stream_but_never_overrides_the_publishers_limits()
	{
		var id = NewId();
		var publisher = Options(id, maxBytes: 4 * 1024 * 1024);
		var consumer = Options(id, maxBytes: 2 * 1024 * 1024);
		var registry = Registry(consumer, "owner" + id, (_, _, _) => Task.CompletedTask);

		// The consumer starts first: it creates the stream from the shared definition.
		await using (await StartAsync(registry, consumer))
			await Assert.That((await StreamConfigAsync(consumer.StreamName)).MaxBytes).IsEqualTo(2L * 1024 * 1024);

		// The publisher owns the limits and reconciles them.
		await using (await NatsJetStreamMessageBus.CreateAsync(publisher, NullLogger<NatsJetStreamMessageBus>.Instance))
			await Assert.That((await StreamConfigAsync(publisher.StreamName)).MaxBytes).IsEqualTo(4L * 1024 * 1024);

		// A consumer restart with its own setting leaves them alone, so the two processes do not undo
		// each other on every restart.
		await using (await StartAsync(registry, consumer))
			await Assert.That((await StreamConfigAsync(consumer.StreamName)).MaxBytes).IsEqualTo(4L * 1024 * 1024);
		await using (await NatsJetStreamMessageBus.CreateAsync(publisher, NullLogger<NatsJetStreamMessageBus>.Instance))
			await Assert.That((await StreamConfigAsync(publisher.StreamName)).MaxBytes).IsEqualTo(4L * 1024 * 1024);
	}

	[Test]
	public async Task An_existing_unbounded_limits_stream_is_brought_under_the_budget()
	{
		var options = Options(NewId());
		await using (var connection = new NatsConnection(new NatsOpts { Url = Url }))
		{
			// What a deployment created before budgets existed.
			await new NatsJSContext(connection).CreateStreamAsync(new StreamConfig(options.StreamName, [$"{options.SubjectPrefix}.>"])
			{
				MaxAge = TimeSpan.FromHours(24),
				MaxMsgSize = options.MaxMsgSize,
			});
		}

		await using var bus = await NatsJetStreamMessageBus.CreateAsync(options, NullLogger<NatsJetStreamMessageBus>.Instance);

		var config = await StreamConfigAsync(options.StreamName);
		await Assert.That(config.Retention).IsEqualTo(StreamConfigRetention.Interest);
		await Assert.That(config.Discard).IsEqualTo(StreamConfigDiscard.New);
		await Assert.That(config.MaxBytes).IsEqualTo(options.MaxBytes);
		await Assert.That(config.MaxAge).IsEqualTo(options.MaxAge);
	}

	private static NatsConsumerRegistry Registry(NatsOptions options, string durable,
		Func<IServiceProvider, object, CancellationToken, Task> handler)
	{
		var registry = new NatsConsumerRegistry();
		registry.Registrations.Add(new NatsConsumerRegistration(typeof(TelnetOutputMessage),
			NatsSubjects.For(typeof(TelnetOutputMessage), options.SubjectPrefix), durable, handler));
		return registry;
	}

	internal static async Task<RunningConsumer> StartAsync(NatsConsumerRegistry registry, NatsOptions options,
		NatsMessagingMetrics? metrics = null)
	{
		var provider = new ServiceCollection().BuildServiceProvider();
		var service = new NatsJetStreamConsumerService(registry, options, provider,
			NullLogger<NatsJetStreamConsumerService>.Instance, metrics);
		await service.StartAsync(CancellationToken.None);
		using var ready = new CancellationTokenSource(TimeSpan.FromSeconds(15));
		await WaitUntilActiveAsync(registry, ready.Token);
		return new RunningConsumer(service, provider);
	}

	private static async Task WaitUntilActiveAsync(NatsConsumerRegistry registry, CancellationToken ct)
	{
		while (!registry.AllActive) await Task.Delay(50, ct);
	}

	internal sealed class RunningConsumer(NatsJetStreamConsumerService service, ServiceProvider provider) : IAsyncDisposable
	{
		public async ValueTask DisposeAsync()
		{
			await service.StopAsync(CancellationToken.None);
			service.Dispose();
			await provider.DisposeAsync();
		}
	}

	private async Task<StreamConfig> StreamConfigAsync(string stream)
	{
		await using var connection = new NatsConnection(new NatsOpts { Url = Url });
		return (await new NatsJSContext(connection).GetStreamAsync(stream)).Info.Config;
	}

	private async Task<StreamInfo> WaitForAsync(string stream, Func<StreamInfo, bool> condition)
	{
		await using var connection = new NatsConnection(new NatsOpts { Url = Url });
		var js = new NatsJSContext(connection);
		var deadline = DateTime.UtcNow.AddSeconds(10);
		while (true)
		{
			var info = (await js.GetStreamAsync(stream)).Info;
			if (condition(info) || DateTime.UtcNow > deadline) return info;
			await Task.Delay(100);
		}
	}
}
