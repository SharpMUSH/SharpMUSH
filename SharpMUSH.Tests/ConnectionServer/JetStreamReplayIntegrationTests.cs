using System.Text;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NATS.Client.KeyValueStore;
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

			// Existing v1 streams/credentials coexist during migration, but cannot authorize v2 resume.
			await using var legacyConnection = new NatsConnection(new NatsOpts { Url = url });
			await legacyConnection.ConnectAsync();
			var legacyJs = new NatsJSContext(legacyConnection);
			await legacyJs.CreateOrUpdateStreamAsync(new StreamConfig("TERMINAL_REPLAY", ["terminal.replay.>"])
			{ MaxAge = TimeSpan.FromHours(24) });
			await legacyJs.PublishAsync($"terminal.replay.{session}", SeqEnvelope.Wrap(99999, "legacy"));

			// --- First "instance": produce three output frames + mint a resume token, then go away. ---
			var replay1 = await JetStreamTerminalReplayStore.CreateAsync(url, NullLogger<JetStreamTerminalReplayStore>.Instance);
			var tokens1 = await NatsKvResumeTokenStore.CreateAsync(url, NullLogger<NatsKvResumeTokenStore>.Instance);

			var first = await replay1.AppendAsync(session, Encoding.UTF8.GetBytes("one"));
			var second = await replay1.AppendAsync(session, Encoding.UTF8.GetBytes("two"));
			var third = await replay1.AppendAsync(session, Encoding.UTF8.GetBytes("three"));
			var token = await tokens1.MintAsync(h, session);
			var legacyKv = await new NatsKVContext(legacyJs).GetStoreAsync("terminal_resume");
			var legacyToken = Guid.NewGuid().ToString("N");
			await legacyKv.PutAsync(legacyToken, $"1:{h}:{session}");
			await Assert.That((await tokens1.TryConsumeAsync(legacyToken)).Found).IsFalse();

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
			var replayed = await replay2.AfterAsync(session, lastSeq: first.Seq);
			var seqs = replayed.Select(SeqEnvelope.ReadSeq).OrderBy(x => x).ToArray();
			await Assert.That(seqs).IsEquivalentTo(new[] { second.Seq, third.Seq });

			var afterRestart = await replay2.AppendAsync(session, Encoding.UTF8.GetBytes("four"));
			await Assert.That(afterRestart.Seq > third.Seq).IsTrue();
			var tail = await replay2.AfterAsync(session, third.Seq);
			await Assert.That(tail.Select(SeqEnvelope.ReadSeq).ToArray()).IsEquivalentTo(new[] { afterRestart.Seq });

			// Two independent clients race the same durable token. Only the revision-CAS winner gets it.
			await using var tokens3 = await NatsKvResumeTokenStore.CreateAsync(url, NullLogger<NatsKvResumeTokenStore>.Instance);
			var consumed = await Task.WhenAll(tokens2.TryConsumeAsync(token).AsTask(), tokens3.TryConsumeAsync(token).AsTask());
			await Assert.That(consumed.Count(result => result.Found)).IsEqualTo(1);
			await Assert.That((await tokens2.TryResolveAsync(token)).Found).IsFalse();

			// More than one fetch batch must survive a restart and replay without omissions.
			var sequences = new List<long>();
			for (var i = 0; i < 510; i++)
				sequences.Add((await replay2.AppendAsync(session, Encoding.UTF8.GetBytes($"line {i}"))).Seq);
			var paged = await replay2.AfterAsync(session, afterRestart.Seq);
			await Assert.That(paged.Select(SeqEnvelope.ReadSeq).ToArray()).IsEquivalentTo(sequences.ToArray());

			var revocable = await tokens2.MintAsync(h, session);
			await tokens3.RevokeSessionAsync(session);
			await Assert.That((await tokens2.TryResolveAsync(revocable)).Found).IsFalse();
			await Assert.That((await tokens2.TryConsumeAsync(revocable)).Found).IsFalse();

			var otherSession = Guid.NewGuid().ToString("N");
			await replay2.AppendAsync(otherSession, "other-session"u8.ToArray());
			await replay2.DropAsync(session);
			await Assert.That((await replay2.AfterAsync(session, 0)).Count).IsEqualTo(0);
			await Assert.That((await replay2.AfterAsync(otherSession, 0)).Count).IsEqualTo(1);
			await replay2.DropAsync(otherSession);
			await replay2.DisposeAsync();
			await tokens2.DisposeAsync();
		}
		finally
		{
			await strategy.DisposeAsync();
		}
	}
}
