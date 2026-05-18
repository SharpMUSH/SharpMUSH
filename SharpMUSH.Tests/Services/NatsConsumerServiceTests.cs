using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharpMUSH.Messaging.Messages;
using SharpMUSH.Messaging.NATS;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// Regression tests for <see cref="NatsJetStreamConsumerService"/> message deserialization.
///
/// These tests exercise the full publish → JetStream → ConsumeAsync → handler pipeline to
/// ensure that messages published by <see cref="NatsJetStreamMessageBus"/> (which uses
/// <c>NatsJsonSerializer&lt;T&gt;.Default</c>) are correctly deserialized by the consumer
/// service. Without an explicit <c>NatsJsonSerializer&lt;JsonElement&gt;.Default</c> passed to
/// <c>ConsumeAsync&lt;JsonElement&gt;</c>, the default <c>NatsDefaultSerializer</c> is used, which
/// only handles raw bytes and UTF-8 primitives — <c>JsonElement</c> is not supported and
/// deserialization silently fails.
/// </summary>
public class NatsConsumerServiceTests
{
	[ClassDataSource<NatsTestServer>(Shared = SharedType.PerTestSession)]
	public required NatsTestServer NatsTestServer { get; init; }

	private string GetUrl() =>
		$"nats://localhost:{NatsTestServer.Instance.GetMappedPublicPort(4222)}";

	[Test]
	public async Task ConsumerService_ShouldDeserializeJsonMessagesPublishedByMessageBus()
	{
		var url = GetUrl();
		var streamName = "SHARPMUSH-CONSUMER-SVC-TEST";
		var subjectPrefix = "sharpmush.consumer.svc.test";

		var received = new TaskCompletionSource<TelnetOutputMessage>(
			TaskCreationOptions.RunContinuationsAsynchronously);

		var registry = new NatsConsumerRegistry();
		registry.Registrations.Add(new NatsConsumerRegistration(
			typeof(TelnetOutputMessage),
			$"{subjectPrefix}.telnet-output",
			"svctest-telnet-output",
			(_, msg, _) =>
			{
				received.TrySetResult((TelnetOutputMessage)msg);
				return Task.CompletedTask;
			}));

		var consumerOptions = new NatsOptions
		{
			Url = url,
			StreamName = streamName,
			SubjectPrefix = subjectPrefix,
		};

		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

		var serviceLogger = new LoggerFactory().CreateLogger<NatsJetStreamConsumerService>();
		var serviceProvider = new ServiceCollection().BuildServiceProvider();
		var service = new NatsJetStreamConsumerService(registry, consumerOptions, serviceProvider, serviceLogger);

		await service.StartAsync(cts.Token);
		await Task.Delay(1000, cts.Token); // Allow consumer to connect and subscribe

		var pubOptions = new NatsOptions
		{
			Url = url,
			StreamName = streamName,
			SubjectPrefix = subjectPrefix,
		};
		var publisherLogger = new LoggerFactory().CreateLogger<NatsJetStreamMessageBus>();
		await using var bus = await NatsJetStreamMessageBus.CreateAsync(pubOptions, publisherLogger);

		var expectedHandle = 42L;
		var expectedData = "Hello, NATS!"u8.ToArray();
		await bus.Publish(new TelnetOutputMessage(expectedHandle, expectedData));

		var result = await received.Task.WaitAsync(TimeSpan.FromSeconds(10), cts.Token);

		await Assert.That(result).IsNotNull();
		await Assert.That(result.Handle).IsEqualTo(expectedHandle);
		await Assert.That(result.Data).IsEquivalentTo(expectedData);

		await cts.CancelAsync();
		await service.StopAsync(CancellationToken.None);
	}

	[Test]
	public async Task ConsumerService_ShouldDeserializeMultipleMessageTypes()
	{
		var url = GetUrl();
		var streamName = "SHARPMUSH-CONSUMER-MULTI-TEST";
		var subjectPrefix = "sharpmush.consumer.multi.test";

		var receivedTelnet = new TaskCompletionSource<TelnetOutputMessage>(
			TaskCreationOptions.RunContinuationsAsynchronously);
		var receivedBroadcast = new TaskCompletionSource<BroadcastMessage>(
			TaskCreationOptions.RunContinuationsAsynchronously);

		var registry = new NatsConsumerRegistry();
		registry.Registrations.Add(new NatsConsumerRegistration(
			typeof(TelnetOutputMessage),
			$"{subjectPrefix}.telnet-output",
			"multi-test-telnet-output",
			(_, msg, _) =>
			{
				receivedTelnet.TrySetResult((TelnetOutputMessage)msg);
				return Task.CompletedTask;
			}));
		registry.Registrations.Add(new NatsConsumerRegistration(
			typeof(BroadcastMessage),
			$"{subjectPrefix}.broadcast",
			"multi-test-broadcast",
			(_, msg, _) =>
			{
				receivedBroadcast.TrySetResult((BroadcastMessage)msg);
				return Task.CompletedTask;
			}));

		var consumerOptions = new NatsOptions
		{
			Url = url,
			StreamName = streamName,
			SubjectPrefix = subjectPrefix,
		};

		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

		var serviceLogger = new LoggerFactory().CreateLogger<NatsJetStreamConsumerService>();
		var serviceProvider = new ServiceCollection().BuildServiceProvider();
		var service = new NatsJetStreamConsumerService(registry, consumerOptions, serviceProvider, serviceLogger);

		await service.StartAsync(cts.Token);
		await Task.Delay(1000, cts.Token);

		var pubOptions = new NatsOptions
		{
			Url = url,
			StreamName = streamName,
			SubjectPrefix = subjectPrefix,
		};
		var publisherLogger = new LoggerFactory().CreateLogger<NatsJetStreamMessageBus>();
		await using var bus = await NatsJetStreamMessageBus.CreateAsync(pubOptions, publisherLogger);

		await bus.Publish(new TelnetOutputMessage(99L, "telnet payload"u8.ToArray()));
		await bus.Publish(new BroadcastMessage("broadcast payload"u8.ToArray()));

		var telnetResult = await receivedTelnet.Task.WaitAsync(TimeSpan.FromSeconds(10), cts.Token);
		var broadcastResult = await receivedBroadcast.Task.WaitAsync(TimeSpan.FromSeconds(10), cts.Token);

		await Assert.That(telnetResult.Handle).IsEqualTo(99L);
		await Assert.That(telnetResult.Data).IsEquivalentTo("telnet payload"u8.ToArray());
		await Assert.That(broadcastResult.Data).IsEquivalentTo("broadcast payload"u8.ToArray());

		await cts.CancelAsync();
		await service.StopAsync(CancellationToken.None);
	}
}
