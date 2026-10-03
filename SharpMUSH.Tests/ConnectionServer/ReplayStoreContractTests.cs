using Microsoft.Extensions.Logging.Abstractions;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NSubstitute;
using SharpMUSH.SocketServer.Services;

namespace SharpMUSH.Tests.ConnectionServer;

public class ReplayStoreContractTests
{
	[Test]
	public async Task Count_eviction_marks_only_missing_history_incomplete()
	{
		var store = new TerminalReplayStore();
		for (var i = 0; i < 201; i++) await store.AppendAsync("session", "frame"u8.ToArray());
		await Assert.That(await store.OpenAsync("session", 0) is IncompleteReplay { Reason: ReplayGap.Expired }).IsTrue();
		await Assert.That(await store.OpenAsync("session", 1) is ReplayFrames).IsTrue();
		await Assert.That((await store.AfterAsync("session", 1)).Count).IsEqualTo(200);
	}

	[Test]
	public async Task Age_eviction_reports_gaps_even_when_all_frames_expire()
	{
		var now = DateTimeOffset.UtcNow;
		var store = new TerminalReplayStore(() => now);
		await store.AppendAsync("session", "old"u8.ToArray());
		now = now.AddSeconds(31);
		await Assert.That(await store.OpenAsync("session", 0) is IncompleteReplay).IsTrue();
		await Assert.That(await store.OpenAsync("session", 1) is ReplayFrames).IsTrue();
		await store.AppendAsync("session", "fresh"u8.ToArray());
		await Assert.That(await store.OpenAsync("session", 0) is IncompleteReplay).IsTrue();
		await Assert.That(await store.OpenAsync("session", 1) is ReplayFrames).IsTrue();
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
		await Assert.That(async () => await store.OpenAsync(session, 0)).Throws<ArgumentException>();
		await Assert.That(async () => await store.DropAsync(session)).Throws<ArgumentException>();
		await Assert.That(js.ReceivedCalls().Count()).IsEqualTo(0);
	}

	private static async Task<PubAckResponse> Stall(CancellationToken ct)
	{
		await Task.Delay(Timeout.InfiniteTimeSpan, ct);
		throw new InvalidOperationException("The stalled publish must be cancelled.");
	}
}
