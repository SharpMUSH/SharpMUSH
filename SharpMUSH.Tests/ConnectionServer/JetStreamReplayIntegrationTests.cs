using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using SharpMUSH.ConnectionServer.Services;
using SharpMUSH.Messaging.NATS.Strategy;

namespace SharpMUSH.Tests.ConnectionServer;

/// <summary>
/// Integration test for the NATS-backed replay path. Spins a real NATS (JetStream) via Testcontainers
/// and proves the key durability property the pivot targets: buffered terminal output and the resume
/// token survive a ConnectionServer restart / instance change (modeled by disposing the first pair of
/// stores and creating a fresh pair against the same NATS).
/// </summary>
public class JetStreamReplayIntegrationTests
{
	[Test]
	public async Task Replay_and_resume_survive_a_simulated_restart()
	{
		var strategy = new NatsTestContainerStrategy();
		// Unique ids per run so the reused NATS container (24h retention) doesn't accumulate
		// output from prior runs on the same subject.
		var h = Math.Abs(BitConverter.ToInt64(Guid.NewGuid().ToByteArray()));
		var session = Guid.NewGuid().ToString("N");
		try
		{
			var url = await strategy.GetUrlAsync();

			// --- First "instance": produce three output frames + mint a resume token, then go away. ---
			var replay1 = await JetStreamTerminalReplayStore.CreateAsync(url, NullLogger<JetStreamTerminalReplayStore>.Instance);
			var tokens1 = await NatsKvResumeTokenStore.CreateAsync(url, NullLogger<NatsKvResumeTokenStore>.Instance);

			await replay1.AppendAsync(session, Encoding.UTF8.GetBytes("one"));   // seq 1
			await replay1.AppendAsync(session, Encoding.UTF8.GetBytes("two"));   // seq 2
			await replay1.AppendAsync(session, Encoding.UTF8.GetBytes("three")); // seq 3
			var token = await tokens1.MintAsync(h, session);

			await replay1.DisposeAsync();
			await tokens1.DisposeAsync();

			// --- Second "instance" (restart): fresh stores, same NATS. ---
			var replay2 = await JetStreamTerminalReplayStore.CreateAsync(url, NullLogger<JetStreamTerminalReplayStore>.Instance);
			var tokens2 = await NatsKvResumeTokenStore.CreateAsync(url, NullLogger<NatsKvResumeTokenStore>.Instance);

			// Resume token still resolves to the old handle and its session id.
			var (found, handle, resolvedSession) = await tokens2.TryResolveAsync(token);
			await Assert.That(found).IsTrue();
			await Assert.That(handle).IsEqualTo(h);
			await Assert.That(resolvedSession).IsEqualTo(session);

			// Buffered output after the client's acked seq is still replayable.
			var replayed = await replay2.AfterAsync(session, lastSeq: 1);
			var seqs = replayed.Select(SeqEnvelope.ReadSeq).OrderBy(x => x).ToArray();
			await Assert.That(seqs).IsEquivalentTo(new[] { 2L, 3L });

			await replay2.DisposeAsync();
			await tokens2.DisposeAsync();
		}
		finally
		{
			await strategy.DisposeAsync();
		}
	}

	[Test]
	public async Task DropAsync_purges_a_handles_buffered_output()
	{
		var strategy = new NatsTestContainerStrategy();
		var session = Guid.NewGuid().ToString("N");
		try
		{
			var url = await strategy.GetUrlAsync();
			var replay = await JetStreamTerminalReplayStore.CreateAsync(url, NullLogger<JetStreamTerminalReplayStore>.Instance);

			await replay.AppendAsync(session, Encoding.UTF8.GetBytes("one"));
			await replay.AppendAsync(session, Encoding.UTF8.GetBytes("two"));
			await Assert.That((await replay.AfterAsync(session, lastSeq: 0)).Count).IsEqualTo(2);

			// Dropping a session reclaims its buffered output.
			await replay.DropAsync(session);

			await Assert.That(await replay.AfterAsync(session, lastSeq: 0)).IsEmpty();

			await replay.DisposeAsync();
		}
		finally
		{
			await strategy.DisposeAsync();
		}
	}
}
