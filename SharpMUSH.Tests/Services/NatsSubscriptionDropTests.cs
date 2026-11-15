using NATS.Client.Core;
using SharpMUSH.Messaging.NATS;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// A core subscription whose pending buffer is full drops the newest message instead of blocking the
/// connection. <see cref="NatsMessagingMetrics.WatchDrops"/> counts each drop, tagged with the subscribed
/// subject, so the portal bridge's losses show on <c>/metrics</c> instead of vanishing.
/// </summary>
public class NatsSubscriptionDropTests
{
	[ClassDataSource<NatsTestServer>(Shared = SharedType.PerTestSession)]
	public required NatsTestServer NatsTestServer { get; init; }

	private string Url => $"nats://localhost:{NatsTestServer.Instance.GetMappedPublicPort(4222)}";

	[Test]
	public async Task AFullSubscriptionBuffer_CountsEachDroppedMessage()
	{
		var subject = TestIsolationHelpers.GenerateUniqueName("drops");
		using var metrics = new NatsMessagingMetrics();
		using var recorder = new MessagingMeterRecorder(metrics);

		await using var nats = new NatsConnection(new NatsOpts { Url = Url, SubPendingChannelCapacity = 1 });
		metrics.WatchDrops(nats);
		await using var subscription = await nats.SubscribeCoreAsync<string>($"{subject}.*");
		await nats.PingAsync();

		for (var i = 0; i < 20; i++)
		{
			await nats.PublishAsync($"{subject}.{i}", $"message {i}");
		}
		await nats.PingAsync();

		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
		while (recorder.Sum("sharpmush.messaging.subscription.dropped", "subscription", $"{subject}.*") < 19)
		{
			await Task.Delay(10, timeout.Token);
		}

		await Assert.That(recorder.Sum("sharpmush.messaging.subscription.dropped", "subscription", $"{subject}.*"))
			.IsEqualTo(19);
	}
}
