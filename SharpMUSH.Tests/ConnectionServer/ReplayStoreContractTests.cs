using Microsoft.Extensions.Logging.Abstractions;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NSubstitute;
using SharpMUSH.ConnectionServer.Services;

namespace SharpMUSH.Tests.ConnectionServer;

public class ReplayStoreContractTests
{
	[Test]
	public async Task Count_eviction_marks_only_missing_history_incomplete()
	{
		var store = new TerminalReplayStore();
		for (var i = 0; i < 201; i++) await store.AppendAsync("session", "frame"u8.ToArray());
		var missing = await store.ReadAsync("session", 0);
		await Assert.That(missing.Complete).IsFalse();
		await Assert.That(missing.Frames.Count).IsEqualTo(200);
		await Assert.That((await store.ReadAsync("session", 1)).Complete).IsTrue();
	}

	[Test]
	public async Task Age_eviction_reports_gaps_even_when_all_frames_expire()
	{
		var now = DateTimeOffset.UtcNow;
		var store = new TerminalReplayStore(() => now);
		await store.AppendAsync("session", "old"u8.ToArray());
		now = now.AddSeconds(31);
		await Assert.That((await store.ReadAsync("session", 0)).Complete).IsFalse();
		await Assert.That((await store.ReadAsync("session", 1)).Complete).IsTrue();
		await store.AppendAsync("session", "fresh"u8.ToArray());
		await Assert.That((await store.ReadAsync("session", 0)).Complete).IsFalse();
		await Assert.That((await store.ReadAsync("session", 1)).Complete).IsTrue();
	}

	[Test]
	public async Task Append_deadline_is_a_timeout_and_caller_cancellation_stays_cancellation()
	{
		var js = Substitute.For<INatsJSContext>();
		js.PublishAsync(Arg.Any<string>(), Arg.Any<byte[]>(), cancellationToken: Arg.Any<CancellationToken>())
			.Returns(call => new ValueTask<PubAckResponse>(Stall(call.Arg<CancellationToken>())));
		var nats = new NatsConnection();
		await using var store = new JetStreamTerminalReplayStore(nats, js,
			NullLogger<JetStreamTerminalReplayStore>.Instance, TimeSpan.FromMilliseconds(50));
		await Assert.That(async () => await store.AppendAsync("session", "frame"u8.ToArray()).AsTask()
			.WaitAsync(TimeSpan.FromSeconds(5))).Throws<TimeoutException>();
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();
		await Assert.That(async () => await store.AppendAsync("session", "frame"u8.ToArray(), cancellation.Token))
			.Throws<OperationCanceledException>();
	}

	[Test]
	[Arguments("*")]
	[Arguments(">")]
	[Arguments("session.other")]
	[Arguments("session other")]
	[Arguments("")]
	public async Task Invalid_subjects_are_rejected_before_broker_access(string session)
	{
		var js = Substitute.For<INatsJSContext>();
		var nats = new NatsConnection();
		await using var store = new JetStreamTerminalReplayStore(nats, js,
			NullLogger<JetStreamTerminalReplayStore>.Instance);
		await Assert.That(async () => await store.AppendAsync(session, [])).Throws<ArgumentException>();
		await Assert.That(async () => await store.AfterAsync(session, 0)).Throws<ArgumentException>();
		await Assert.That(async () => await store.DropAsync(session)).Throws<ArgumentException>();
		await Assert.That(js.ReceivedCalls().Count()).IsEqualTo(0);
	}

	private static async Task<PubAckResponse> Stall(CancellationToken ct)
	{
		await Task.Delay(Timeout.InfiniteTimeSpan, ct);
		throw new InvalidOperationException("The stalled publish must be cancelled.");
	}
}
