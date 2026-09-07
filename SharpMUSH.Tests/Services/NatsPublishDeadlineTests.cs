using Microsoft.Extensions.Logging.Abstractions;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NSubstitute;
using SharpMUSH.Messaging.Messages;
using SharpMUSH.Messaging.NATS;

namespace SharpMUSH.Tests.Services;

public class NatsPublishDeadlineTests
{
	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task PublishDeadlineIsReportedAsTimeout(bool handleBased)
	{
		var js = StalledPublisher();
		await using var bus = Create(js, TimeSpan.FromMilliseconds(20));
		using var guard = new CancellationTokenSource(TimeSpan.FromSeconds(5));
		var message = new TelnetInputMessage(42, "look");
		await Assert.That(async () => await (handleBased
			? bus.HandlePublish(message, guard.Token)
			: bus.Publish(message, guard.Token)).WaitAsync(guard.Token)).Throws<TimeoutException>();
		await Assert.That(guard.IsCancellationRequested).IsFalse();
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task CallerCancellationRemainsCancellation(bool handleBased)
	{
		var js = StalledPublisher();
		await using var bus = Create(js, TimeSpan.FromMinutes(1));
		using var caller = new CancellationTokenSource();
		var message = new TelnetInputMessage(42, "look");
		var publish = handleBased ? bus.HandlePublish(message, caller.Token) : bus.Publish(message, caller.Token);
		await caller.CancelAsync();
		await Assert.That(async () => await publish.WaitAsync(TimeSpan.FromSeconds(5)))
			.Throws<OperationCanceledException>();
	}

	private static NatsJetStreamMessageBus Create(INatsJSContext js, TimeSpan timeout) =>
		new(new NatsConnection(), js, new NatsOptions { PublishTimeout = timeout },
			NullLogger<NatsJetStreamMessageBus>.Instance);

	private static INatsJSContext StalledPublisher()
	{
		var js = Substitute.For<INatsJSContext>();
		js.PublishAsync(Arg.Any<string>(), Arg.Any<TelnetInputMessage>(),
			Arg.Any<INatsSerialize<TelnetInputMessage>>(), Arg.Any<NatsJSPubOpts>(),
			Arg.Any<NatsHeaders>(), Arg.Any<CancellationToken>())
			.Returns(call => StallAsync(call.Arg<CancellationToken>()));
		return js;
	}

	private static async ValueTask<PubAckResponse> StallAsync(CancellationToken ct)
	{
		await Task.Delay(Timeout.InfiniteTimeSpan, ct);
		throw new InvalidOperationException("The stalled publish must be cancelled.");
	}
}
