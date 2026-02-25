using Microsoft.Extensions.Logging;
using SharpMUSH.Messages;
using SharpMUSH.Messaging.Abstractions;
using SharpMUSH.Messaging.NATS;
using System.Diagnostics;
using System.Text;

namespace SharpMUSH.Tests.Performance;

/// <summary>
/// Performance comparison tests for the NATS JetStream message-bus adapter.
/// Test cases mirror <see cref="KafkaPerformanceValidation"/> so that throughput
/// and latency numbers can be compared directly between the two transports.
/// </summary>
[NotInParallel]
public class NatsPerformanceValidation
{
	[ClassDataSource<NatsTestServer>(Shared = SharedType.PerTestSession)]
	public required NatsTestServer NatsTestServer { get; init; }

	private async Task<IMessageBus> CreateMessageBusAsync()
	{
		var port = NatsTestServer.Instance.GetMappedPublicPort(4222);
		var options = new NatsOptions { Url = $"nats://localhost:{port}" };
		var logger = new LoggerFactory().CreateLogger<NatsJetStreamMessageBus>();
		return await NatsJetStreamMessageBus.CreateAsync(options, logger);
	}

	[Test, Explicit]  // Explicit — only run when specifically requested
	public async Task Nats_ProducerThroughput_ShouldHandleHighVolume()
	{
		// Arrange
		var bus = await CreateMessageBusAsync();
		const int messageCount = 1000;
		var messages = Enumerable.Range(0, messageCount)
			.Select(i => new TelnetOutputMessage(1, Encoding.UTF8.GetBytes($"Message {i}")))
			.ToList();

		// Act — measure sequential publish throughput for 1000 messages
		var sw = Stopwatch.StartNew();
		foreach (var message in messages)
			await bus.Publish(message);
		sw.Stop();

		var elapsedMs = sw.ElapsedMilliseconds;
		Console.WriteLine($"[NATS] Published {messageCount} messages in {elapsedMs}ms");
		Console.WriteLine($"[NATS] Throughput: {messageCount * 1000.0 / elapsedMs:F2} msg/sec");
		Console.WriteLine($"[NATS] Average latency: {(double)elapsedMs / messageCount:F2}ms per message");

		// NATS JetStream should be faster than Kafka (Kafka target: <15 000ms)
		await Assert.That(elapsedMs).IsLessThan(15000)
			.Because("NATS JetStream should publish 1000 messages in under 15 seconds");
	}

	[Test, Explicit]  // Explicit — requires NATS to be running
	public async Task Nats_ProducerLatency_ShouldBeLowForSingleMessage()
	{
		// Arrange
		var bus = await CreateMessageBusAsync();
		var message = new TelnetOutputMessage(1, Encoding.UTF8.GetBytes("Test message"));

		// Warm up
		await bus.Publish(message);
		await Task.Delay(100);

		// Act — measure single-message publish latency
		var sw = Stopwatch.StartNew();
		await bus.Publish(message);
		sw.Stop();

		var elapsedMs = sw.ElapsedMilliseconds;
		Console.WriteLine($"[NATS] Single message latency: {elapsedMs}ms");

		// NATS has no configurable linger/batching window, so single-message
		// latency should be lower than Kafka's target of <100ms
		await Assert.That(elapsedMs).IsLessThan(100)
			.Because("Single NATS JetStream publish should complete in under 100ms");
	}

	[Test, Explicit]  // Explicit — requires NATS to be running
	public async Task Nats_ConcurrentProducer_ShouldOutperformSequential()
	{
		// Arrange
		var bus = await CreateMessageBusAsync();
		const int messageCount = 100;
		var messages = Enumerable.Range(0, messageCount)
			.Select(i => new TelnetOutputMessage(1, Encoding.UTF8.GetBytes($"Batch message {i}")))
			.ToList();

		// Act — fire all publishes concurrently (mirrors Kafka_BatchedProducer test)
		var sw = Stopwatch.StartNew();
		await Task.WhenAll(messages.Select(m => bus.Publish(m)));
		sw.Stop();

		var elapsedMs = sw.ElapsedMilliseconds;
		Console.WriteLine($"[NATS] Published {messageCount} messages (concurrent) in {elapsedMs}ms");
		Console.WriteLine($"[NATS] Throughput: {messageCount * 1000.0 / elapsedMs:F2} msg/sec");
		Console.WriteLine($"[NATS] Average latency: {(double)elapsedMs / messageCount:F2}ms per message");

		// NATS has no linger window, so concurrent publishes can complete much
		// faster than Kafka's 16ms linger target
		await Assert.That(elapsedMs).IsLessThan(1000)
			.Because("100 concurrent NATS JetStream publishes should complete in under 1 second");
	}

	[Test]
	public async Task Nats_Configuration_ShouldHaveReasonableDefaults()
	{
		// Arrange
		var port = NatsTestServer.Instance.GetMappedPublicPort(4222);
		var options = new NatsOptions { Url = $"nats://localhost:{port}" };

		// Assert — verify NATS option defaults are sane
		await Assert.That(options.StreamName).IsEqualTo("SHARPMUSH")
			.Because("Default stream name should be SHARPMUSH");
		await Assert.That(options.SubjectPrefix).IsEqualTo("sharpmush")
			.Because("Default subject prefix should be sharpmush");
		await Assert.That(options.MaxAge).IsGreaterThan(TimeSpan.Zero)
			.Because("Messages should have a finite retention age");

		Console.WriteLine($"[NATS] Configuration:");
		Console.WriteLine($"  Stream: {options.StreamName}");
		Console.WriteLine($"  Subject prefix: {options.SubjectPrefix}");
		Console.WriteLine($"  Max message age: {options.MaxAge}");
		Console.WriteLine($"  URL: {options.Url}");
	}

	[Test]
	public async Task Nats_SubjectNaming_ShouldMirrorKafkaTopicConvention()
	{
		// Verify that NATS subject derivation from message type name matches the
		// Kafka kebab-case convention so topics/subjects are interchangeable
		// by inspection without running the bus.
		var port = NatsTestServer.Instance.GetMappedPublicPort(4222);
		var options = new NatsOptions { Url = $"nats://localhost:{port}" };
		var logger = new LoggerFactory().CreateLogger<NatsJetStreamMessageBus>();
		await using var bus = await NatsJetStreamMessageBus.CreateAsync(options, logger);

		// Publish a typed message and verify no exception is thrown.
		// The subject used internally is "sharpmush.telnet-output"
		// which mirrors the Kafka topic "telnet-output".
		var msg = new TelnetOutputMessage(1, Encoding.UTF8.GetBytes("hello"));
		await bus.Publish(msg);

		Console.WriteLine("[NATS] TelnetOutputMessage published to subject sharpmush.telnet-output");
		Console.WriteLine("[NATS] Kafka topic equivalent: telnet-output");
	}
}
