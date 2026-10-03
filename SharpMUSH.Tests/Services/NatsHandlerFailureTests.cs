using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using SharpMUSH.Messaging.Abstractions;
using SharpMUSH.Messaging.Messages;
using SharpMUSH.Messaging.NATS;
using static SharpMUSH.Messaging.NATS.NatsJetStreamConsumerService;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// What a consumer does with a message its handler fails on (#1457): a retryable failure is retried in
/// place under an explicit policy, anything else is terminated rather than acknowledged as success or
/// redelivered forever, and a redelivery of finished work does not run the handler twice.
/// </summary>
public class NatsHandlerFailureTests
{
	[ClassDataSource<NatsTestServer>(Shared = SharedType.PerTestSession)]
	public required NatsTestServer NatsTestServer { get; init; }

	private string Url => $"nats://localhost:{NatsTestServer.Instance.GetMappedPublicPort(4222)}";

	private static readonly JsonElement Payload =
		JsonSerializer.SerializeToElement(new TelnetOutputMessage(7, "hello"u8.ToArray()));

	private static NatsConsumerRegistration Registration(string durable, Func<object, Task> handler) =>
		new(typeof(TelnetOutputMessage), "failure.telnet-output", durable, (_, message, _) => handler(message));

	private static NatsJetStreamConsumerService Service(NatsConsumerRegistration registration, NatsMessagingMetrics metrics,
		int maxAttempts = 3)
	{
		var registry = new NatsConsumerRegistry();
		registry.Registrations.Add(registration);
		return new NatsJetStreamConsumerService(registry,
			new NatsOptions { HandlerMaxAttempts = maxAttempts, HandlerRetryDelay = TimeSpan.FromMilliseconds(1) },
			new ServiceCollection().BuildServiceProvider(), NullLogger<NatsJetStreamConsumerService>.Instance, metrics);
	}

	[Test]
	public async Task A_retryable_failure_is_retried_in_place_and_then_succeeds()
	{
		var durable = "retry-" + Guid.NewGuid().ToString("N");
		var calls = 0;
		var progress = 0;
		using var metrics = new NatsMessagingMetrics();
		using var recorder = new MessagingMeterRecorder(metrics);
		var registration = Registration(durable, _ => ++calls == 1
			? throw new RetryableMessageException("dependency briefly unavailable")
			: Task.CompletedTask);
		using var service = Service(registration, metrics);

		var outcome = await service.HandleDeliveryAsync(registration, Payload, 11,
			_ => { progress++; return ValueTask.CompletedTask; }, CancellationToken.None);

		await Assert.That(outcome).IsEqualTo(DeliveryOutcome.Handled);
		await Assert.That(calls).IsEqualTo(2);
		await Assert.That(progress).IsEqualTo(1);
		await Assert.That(recorder.Sum("sharpmush.messaging.handler.retries", "consumer", durable)).IsEqualTo(1L);
	}

	[Test]
	public async Task A_retryable_failure_that_never_clears_is_terminated_after_the_last_attempt()
	{
		var durable = "exhaust-" + Guid.NewGuid().ToString("N");
		var calls = 0;
		using var metrics = new NatsMessagingMetrics();
		using var recorder = new MessagingMeterRecorder(metrics);
		var registration = Registration(durable, _ =>
		{
			calls++;
			throw new RetryableMessageException("still down");
		});
		using var service = Service(registration, metrics, maxAttempts: 4);

		var outcome = await service.HandleDeliveryAsync(registration, Payload, 12, _ => ValueTask.CompletedTask, CancellationToken.None);

		await Assert.That(outcome).IsEqualTo(DeliveryOutcome.Terminated);
		await Assert.That(calls).IsEqualTo(4);
		await Assert.That(recorder.Sum("sharpmush.messaging.handler.terminal_failures", "reason", "retries-exhausted")).IsEqualTo(1L);
	}

	[Test]
	public async Task Any_other_failure_is_terminal_after_one_run()
	{
		var durable = "terminal-" + Guid.NewGuid().ToString("N");
		var calls = 0;
		using var metrics = new NatsMessagingMetrics();
		using var recorder = new MessagingMeterRecorder(metrics);
		var registration = Registration(durable, _ =>
		{
			calls++;
			throw new InvalidOperationException("the handler may already have acted");
		});
		using var service = Service(registration, metrics);

		var outcome = await service.HandleDeliveryAsync(registration, Payload, 13, _ => ValueTask.CompletedTask, CancellationToken.None);

		await Assert.That(outcome).IsEqualTo(DeliveryOutcome.Terminated);
		await Assert.That(calls).IsEqualTo(1);
		await Assert.That(recorder.Sum("sharpmush.messaging.handler.terminal_failures", "reason", "handler-error")).IsEqualTo(1L);
	}

	[Test]
	public async Task An_unreadable_payload_is_terminal_without_running_the_handler()
	{
		var calls = 0;
		using var metrics = new NatsMessagingMetrics();
		var registration = Registration("unreadable-" + Guid.NewGuid().ToString("N"), _ => { calls++; return Task.CompletedTask; });
		using var service = Service(registration, metrics);

		var empty = await service.HandleDeliveryAsync(registration, default, 14, _ => ValueTask.CompletedTask, CancellationToken.None);
		var wrongShape = await service.HandleDeliveryAsync(registration, JsonSerializer.SerializeToElement("not a message"), 15,
			_ => ValueTask.CompletedTask, CancellationToken.None);

		await Assert.That(empty).IsEqualTo(DeliveryOutcome.Terminated);
		await Assert.That(wrongShape).IsEqualTo(DeliveryOutcome.Terminated);
		await Assert.That(calls).IsEqualTo(0);
	}

	[Test]
	public async Task A_redelivery_of_finished_work_does_not_repeat_its_side_effects()
	{
		var calls = 0;
		using var metrics = new NatsMessagingMetrics();
		var registration = Registration("dedupe-" + Guid.NewGuid().ToString("N"), _ => { calls++; return Task.CompletedTask; });
		using var service = Service(registration, metrics);

		var first = await service.HandleDeliveryAsync(registration, Payload, 21, _ => ValueTask.CompletedTask, CancellationToken.None);
		// The ACK for sequence 21 was lost; the broker delivers it again.
		var again = await service.HandleDeliveryAsync(registration, Payload, 21, _ => ValueTask.CompletedTask, CancellationToken.None);
		var next = await service.HandleDeliveryAsync(registration, Payload, 22, _ => ValueTask.CompletedTask, CancellationToken.None);

		await Assert.That(first).IsEqualTo(DeliveryOutcome.Handled);
		await Assert.That(again).IsEqualTo(DeliveryOutcome.Duplicate);
		await Assert.That(next).IsEqualTo(DeliveryOutcome.Handled);
		await Assert.That(calls).IsEqualTo(2);
	}

	[Test]
	public async Task A_failing_message_neither_blocks_its_subject_nor_comes_back()
	{
		var id = Guid.NewGuid().ToString("N")[..12];
		var options = Options(id);
		var poisonRuns = 0;
		var healthy = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var registry = Registry(options, "poison" + id, (_, message, _) =>
		{
			if (((TelnetOutputMessage)message).Handle == 666)
			{
				Interlocked.Increment(ref poisonRuns);
				throw new InvalidOperationException("poison");
			}
			healthy.TrySetResult();
			return Task.CompletedTask;
		});
		await using var running = await NatsStreamBudgetTests.StartAsync(registry, options);
		await using var bus = await NatsJetStreamMessageBus.CreateAsync(options, NullLogger<NatsJetStreamMessageBus>.Instance);

		await bus.Publish(new TelnetOutputMessage(666, "poison"u8.ToArray()));
		await bus.Publish(new TelnetOutputMessage(1, "healthy"u8.ToArray()));
		await healthy.Task.WaitAsync(TimeSpan.FromSeconds(15));
		// Wait out several ACK windows: a terminated message is not redelivered.
		await Task.Delay(options.AckWait * 3);

		await Assert.That(poisonRuns).IsEqualTo(1);
		await using var connection = new NatsConnection(new NatsOpts { Url = Url });
		var info = (await new NatsJSContext(connection).GetConsumerAsync(options.StreamName, "poison" + id)).Info;
		await Assert.That(info.NumAckPending).IsEqualTo(0);
		await Assert.That(info.NumRedelivered).IsEqualTo(0);
	}

	[Test]
	public async Task A_message_whose_process_was_lost_before_its_ACK_is_delivered_again()
	{
		var id = Guid.NewGuid().ToString("N")[..12];
		var options = Options(id);
		var durable = "lost" + id;
		var subject = NatsSubjects.For(typeof(TelnetOutputMessage), options.SubjectPrefix);
		await using var bus = await NatsJetStreamMessageBus.CreateAsync(options, NullLogger<NatsJetStreamMessageBus>.Instance);
		await using (var connection = new NatsConnection(new NatsOpts { Url = Url }))
		{
			var js = new NatsJSContext(connection);
			// A consumer process takes the message and dies before acknowledging it.
			var crashed = await js.CreateOrUpdateConsumerAsync(options.StreamName, new ConsumerConfig(durable)
			{
				FilterSubject = subject,
				DeliverPolicy = ConsumerConfigDeliverPolicy.New,
				AckPolicy = ConsumerConfigAckPolicy.Explicit,
				AckWait = options.AckWait,
				MaxDeliver = options.MaxDeliver,
			});
			await bus.Publish(new TelnetOutputMessage(5, "in flight"u8.ToArray()));
			var taken = 0;
			await foreach (var _ in crashed.FetchAsync<byte[]>(new NatsJSFetchOpts { MaxMsgs = 1, Expires = TimeSpan.FromSeconds(5) }))
				taken++;
			await Assert.That(taken).IsEqualTo(1);
		}

		var delivered = new TaskCompletionSource<TelnetOutputMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
		var registry = Registry(options, durable, (_, message, _) =>
		{
			delivered.TrySetResult((TelnetOutputMessage)message);
			return Task.CompletedTask;
		});
		await using var restarted = await NatsStreamBudgetTests.StartAsync(registry, options);

		var redelivered = await delivered.Task.WaitAsync(TimeSpan.FromSeconds(15));
		await Assert.That(redelivered.Handle).IsEqualTo(5L);
	}

	[Test]
	public async Task A_new_consumer_identity_starts_at_new_messages_instead_of_replaying_history()
	{
		var id = Guid.NewGuid().ToString("N")[..12];
		var options = Options(id);
		var subject = NatsSubjects.For(typeof(TelnetOutputMessage), options.SubjectPrefix);
		await using var bus = await NatsJetStreamMessageBus.CreateAsync(options, NullLogger<NatsJetStreamMessageBus>.Instance);
		await using (var connection = new NatsConnection(new NatsOpts { Url = Url }))
		{
			// Another consumer keeps interest in the subject, so the old message is retained.
			await new NatsJSContext(connection).CreateOrUpdateConsumerAsync(options.StreamName, new ConsumerConfig("other" + id)
			{
				FilterSubject = subject,
				AckPolicy = ConsumerConfigAckPolicy.Explicit,
			});
		}
		await bus.Publish(new TelnetOutputMessage(1, "before"u8.ToArray()));

		var received = new List<long>();
		var after = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var registry = Registry(options, "recreated" + id, (_, message, _) =>
		{
			var handle = ((TelnetOutputMessage)message).Handle;
			lock (received) received.Add(handle);
			if (handle == 2) after.TrySetResult();
			return Task.CompletedTask;
		});
		await using var running = await NatsStreamBudgetTests.StartAsync(registry, options);
		await bus.Publish(new TelnetOutputMessage(2, "after"u8.ToArray()));
		await after.Task.WaitAsync(TimeSpan.FromSeconds(15));

		await Assert.That(received).IsEquivalentTo(new[] { 2L });
	}

	private NatsOptions Options(string id) => new()
	{
		Url = Url,
		StreamName = "FAILURE-" + id,
		SubjectPrefix = "failure." + id,
		AckWait = TimeSpan.FromSeconds(1),
		HandlerRetryDelay = TimeSpan.FromMilliseconds(10),
	};

	private static NatsConsumerRegistry Registry(NatsOptions options, string durable,
		Func<IServiceProvider, object, CancellationToken, Task> handler)
	{
		var registry = new NatsConsumerRegistry();
		registry.Registrations.Add(new NatsConsumerRegistration(typeof(TelnetOutputMessage),
			NatsSubjects.For(typeof(TelnetOutputMessage), options.SubjectPrefix), durable, handler));
		return registry;
	}
}
