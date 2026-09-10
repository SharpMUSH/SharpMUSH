using System.Text;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NATS.Client.KeyValueStore;
using Microsoft.Extensions.Logging.Abstractions;
using SharpMUSH.ConnectionServer.Services;
using SharpMUSH.Messaging.NATS.Strategy;

namespace SharpMUSH.Tests.ConnectionServer;

[NotInParallel]
public class JetStreamReplayIntegrationTests
{
	private static Task<JetStreamTerminalReplayStore> Replay(string url) =>
		JetStreamTerminalReplayStore.CreateAsync(url, NullLogger<JetStreamTerminalReplayStore>.Instance);

	private static Task<NatsKvResumeTokenStore> Tokens(string url) =>
		NatsKvResumeTokenStore.CreateAsync(url, NullLogger<NatsKvResumeTokenStore>.Instance);

	[Test]
	public async Task Replay_and_resume_survive_a_simulated_restart()
	{
		await using var strategy = new NatsTestContainerStrategy(TestDiagnostics.ContainerLogger);
		var url = await strategy.GetUrlAsync();
		var session = Guid.NewGuid().ToString("N");
		string token;
		long first, second, third;
		await using (var replay = await Replay(url))
		await using (var tokens = await Tokens(url))
		{
			first = (await replay.AppendAsync(session, "one"u8.ToArray())).Seq;
			second = (await replay.AppendAsync(session, "two"u8.ToArray())).Seq;
			third = (await replay.AppendAsync(session, "three"u8.ToArray())).Seq;
			token = await tokens.MintAsync(42, session);
		}
		await using var restartedReplay = await Replay(url);
		await using var restartedTokens = await Tokens(url);
		var resolved = await restartedTokens.TryResolveAsync(token);
		await Assert.That(resolved.Found).IsTrue();
		await Assert.That(resolved.Handle).IsEqualTo(42L);
		await Assert.That(resolved.Session).IsEqualTo(session);
		var replayed = await restartedReplay.AfterAsync(session, first);
		await Assert.That(replayed.Select(SeqEnvelope.ReadSeq).ToArray()).IsEquivalentTo(new[] { second, third });
		var next = await restartedReplay.AppendAsync(session, "four"u8.ToArray());
		await Assert.That(next.Seq > third).IsTrue();
		await Assert.That((await restartedReplay.AfterAsync(session, third)).Select(SeqEnvelope.ReadSeq).ToArray())
			.IsEquivalentTo(new[] { next.Seq });
	}

	[Test]
	public async Task Legacy_tokens_and_frames_cannot_enter_v2_replay()
	{
		await using var strategy = new NatsTestContainerStrategy(TestDiagnostics.ContainerLogger);
		var url = await strategy.GetUrlAsync();
		var session = Guid.NewGuid().ToString("N");
		await using var connection = new NatsConnection(new NatsOpts { Url = url });
		await connection.ConnectAsync();
		var js = new NatsJSContext(connection);
		await js.CreateOrUpdateStreamAsync(new StreamConfig("TERMINAL_REPLAY", ["terminal.replay.>"])
		{ MaxAge = TimeSpan.FromHours(24) });
		await js.PublishAsync($"terminal.replay.{session}", SeqEnvelope.Wrap(99999, "legacy"));
		await using var replay = await Replay(url);
		await using var tokens = await Tokens(url);
		var legacyKv = await new NatsKVContext(js).GetStoreAsync("terminal_resume");
		var legacyToken = Guid.NewGuid().ToString("N");
		await legacyKv.PutAsync(legacyToken, $"1:42:{session}");
		await Assert.That((await tokens.TryConsumeAsync(legacyToken)).Found).IsFalse();
		await Assert.That(await replay.AfterAsync(session, 0)).IsEmpty();
	}

	[Test]
	public async Task Token_consumption_has_one_CAS_winner()
	{
		await using var strategy = new NatsTestContainerStrategy(TestDiagnostics.ContainerLogger);
		var url = await strategy.GetUrlAsync();
		await using var first = await Tokens(url);
		await using var second = await Tokens(url);
		var token = await first.MintAsync(42, Guid.NewGuid().ToString("N"));
		var consumed = await Task.WhenAll(first.TryConsumeAsync(token).AsTask(), second.TryConsumeAsync(token).AsTask());
		await Assert.That(consumed.Count(result => result.Found)).IsEqualTo(1);
		await Assert.That((await first.TryResolveAsync(token)).Found).IsFalse();
	}

	[Test]
	public async Task Replay_paginates_across_fetch_batches_after_restart()
	{
		await using var strategy = new NatsTestContainerStrategy(TestDiagnostics.ContainerLogger);
		var url = await strategy.GetUrlAsync();
		var session = Guid.NewGuid().ToString("N");
		var sequences = new List<long>();
		await using (var replay = await Replay(url))
		{
			for (var i = 0; i < 510; i++)
				sequences.Add((await replay.AppendAsync(session, Encoding.UTF8.GetBytes($"line {i}"))).Seq);
		}
		await using var restarted = await Replay(url);
		var paged = await restarted.AfterAsync(session, 0);
		await Assert.That(paged.Select(SeqEnvelope.ReadSeq).ToArray()).IsEquivalentTo(sequences.ToArray());
	}

	[Test]
	public async Task Session_revocation_is_visible_to_other_instances()
	{
		await using var strategy = new NatsTestContainerStrategy(TestDiagnostics.ContainerLogger);
		var url = await strategy.GetUrlAsync();
		var session = Guid.NewGuid().ToString("N");
		await using var first = await Tokens(url);
		await using var second = await Tokens(url);
		var token = await first.MintAsync(42, session);
		await second.RevokeSessionAsync(session);
		await Assert.That((await first.TryResolveAsync(token)).Found).IsFalse();
		await Assert.That((await first.TryConsumeAsync(token)).Found).IsFalse();
	}

	[Test]
	public async Task Dropping_replay_preserves_other_sessions()
	{
		await using var strategy = new NatsTestContainerStrategy(TestDiagnostics.ContainerLogger);
		var url = await strategy.GetUrlAsync();
		await using var replay = await Replay(url);
		var session = Guid.NewGuid().ToString("N");
		var otherSession = Guid.NewGuid().ToString("N");
		await replay.AppendAsync(session, "session"u8.ToArray());
		await replay.AppendAsync(otherSession, "other-session"u8.ToArray());
		await replay.DropAsync(session);
		await Assert.That(await replay.AfterAsync(session, 0)).IsEmpty();
		await Assert.That((await replay.AfterAsync(otherSession, 0)).Count).IsEqualTo(1);
		await replay.DropAsync(otherSession);
	}
}
