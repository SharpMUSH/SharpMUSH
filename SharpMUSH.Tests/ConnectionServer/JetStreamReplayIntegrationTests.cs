using System.Text;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NATS.Client.KeyValueStore;
using Microsoft.Extensions.Logging.Abstractions;
using SharpMUSH.SocketServer.Configuration;
using SharpMUSH.SocketServer.Services;

namespace SharpMUSH.Tests.ConnectionServer;

public class JetStreamReplayIntegrationTests
{
	// A non-reused broker for this class. The reused SharpMUSH-NATS dev container keeps the host ports it was
	// first given, so restarting it after a stop fails when anything else on the host has taken one of them.
	[ClassDataSource<NatsTestServer>(Shared = SharedType.PerClass)]
	public required NatsTestServer NatsTestServer { get; init; }

	private string Url => $"nats://localhost:{NatsTestServer.Instance.GetMappedPublicPort(4222)}";

	private static Task<JetStreamTerminalReplayStore> Replay(string url, ReplayOptions? options = null) =>
		JetStreamTerminalReplayStore.CreateAsync(url, NullLogger<JetStreamTerminalReplayStore>.Instance, options);

	private static Task<NatsKvResumeTokenStore> Tokens(string url) =>
		NatsKvResumeTokenStore.CreateAsync(url, NullLogger<NatsKvResumeTokenStore>.Instance);

	[Test]
	public async Task Replay_and_resume_survive_a_simulated_restart()
	{
		var url = Url;
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
		var url = Url;
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
		var url = Url;
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
		var url = Url;
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
		var url = Url;
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
		var url = Url;
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

	[Test]
	public async Task Replay_reports_expired_history_when_the_clients_last_frame_is_gone()
	{
		var url = Url;
		await using var replay = await Replay(url);
		var session = Guid.NewGuid().ToString("N");
		var first = (await replay.AppendAsync(session, "one"u8.ToArray())).Seq;
		var second = (await replay.AppendAsync(session, "two"u8.ToArray())).Seq;
		await replay.AppendAsync(session, "three"u8.ToArray());
		await using var connection = new NatsConnection(new NatsOpts { Url = url });
		var js = new NatsJSContext(connection);
		// The client acknowledged "one"; age or the byte budget then evicted it.
		await js.DeleteMessageAsync(JetStreamTerminalReplayStore.ReplayStreamName, new StreamMsgDeleteRequest { Seq = (ulong)first });

		await Assert.That(await replay.OpenAsync(session, first) is IncompleteReplay { Reason: ReplayGap.Expired }).IsTrue();
		// A client that already has "two" loses nothing.
		await Assert.That((await replay.AfterAsync(session, second)).Count).IsEqualTo(1);
		await replay.DropAsync(session);
	}

	[Test]
	public async Task Replay_longer_than_the_frame_budget_is_incomplete()
	{
		await using var replay = await Replay(Url, new ReplayOptions { MaxFrames = 5 });
		var session = Guid.NewGuid().ToString("N");
		var sequences = new List<long>();
		for (var i = 0; i < 8; i++)
			sequences.Add((await replay.AppendAsync(session, Encoding.UTF8.GetBytes($"line {i}"))).Seq);

		await Assert.That(await replay.OpenAsync(session, 0) is IncompleteReplay { Reason: ReplayGap.OverBudget }).IsTrue();
		await Assert.That((await replay.AfterAsync(session, sequences[2])).Select(SeqEnvelope.ReadSeq).ToArray())
			.IsEquivalentTo(sequences.Skip(3).ToArray());
		await replay.DropAsync(session);
	}

	[Test]
	public async Task Replay_reads_a_page_at_a_time_and_reports_frames_lost_mid_replay()
	{
		await using var replay = await Replay(Url, new ReplayOptions { PageSize = 4 });
		var session = Guid.NewGuid().ToString("N");
		for (var i = 0; i < 10; i++)
			await replay.AppendAsync(session, Encoding.UTF8.GetBytes($"line {i}"));

		var opening = await replay.OpenAsync(session, 0);
		var frames = opening.Expect<ReplayFrames>();
		await using (frames)
		{
			var read = 0;
			// The frames are purged after the first page was fetched. Only that page was in memory, so the
			// replay delivers it and then reports the gap instead of claiming a complete history.
			await Assert.That(async () =>
			{
				await foreach (var _ in frames.ReadAsync())
				{
					if (++read == 1) await replay.DropAsync(session);
				}
			}).Throws<ReplayInterruptedException>();
			await Assert.That(read).IsEqualTo(4);
		}
	}

	[Test]
	public async Task Cancelling_a_replay_stops_it_and_leaves_the_history_readable()
	{
		await using var replay = await Replay(Url, new ReplayOptions { PageSize = 2 });
		var session = Guid.NewGuid().ToString("N");
		for (var i = 0; i < 6; i++)
			await replay.AppendAsync(session, Encoding.UTF8.GetBytes($"line {i}"));
		using var cancellation = new CancellationTokenSource();

		var frames = (await replay.OpenAsync(session, 0)).Expect<ReplayFrames>();
		await using (frames)
		{
			await Assert.That(async () =>
			{
				await foreach (var _ in frames.ReadAsync(cancellation.Token))
					await cancellation.CancelAsync();
			}).Throws<OperationCanceledException>();
		}

		await Assert.That((await replay.AfterAsync(session, 0)).Count).IsEqualTo(6);
		await replay.DropAsync(session);
	}

	[Test]
	public async Task Concurrent_reconnects_each_replay_their_own_session_in_order()
	{
		await using var replay = await Replay(Url, new ReplayOptions { PageSize = 16 });
		var sessions = Enumerable.Range(0, 8).Select(_ => Guid.NewGuid().ToString("N")).ToArray();
		var appended = sessions.ToDictionary(session => session, _ => new List<long>());
		for (var i = 0; i < 100; i++)
			foreach (var session in sessions)
				appended[session].Add((await replay.AppendAsync(session, Encoding.UTF8.GetBytes($"{session} {i}"))).Seq);

		var replayed = await Task.WhenAll(sessions.Select(async session =>
			(Session: session, Seqs: (await replay.AfterAsync(session, 0)).Select(SeqEnvelope.ReadSeq).ToArray())));

		foreach (var (session, seqs) in replayed)
			await Assert.That(seqs).IsEquivalentTo(appended[session].ToArray(), TUnit.Assertions.Enums.CollectionOrdering.Matching);
		foreach (var session in sessions) await replay.DropAsync(session);
	}

	[Test]
	public async Task Replay_stream_keeps_its_own_retention_and_byte_budget()
	{
		var url = Url;
		var defaults = new ReplayOptions();
		await using var replay = await Replay(url);
		await using var connection = new NatsConnection(new NatsOpts { Url = url });
		var js = new NatsJSContext(connection);

		var config = (await js.GetStreamAsync(JetStreamTerminalReplayStore.ReplayStreamName)).Info.Config;

		await Assert.That(config.MaxAge).IsEqualTo(defaults.Retention);
		await Assert.That(config.MaxBytes).IsEqualTo(defaults.MaxBytes);
		await Assert.That(config.Retention).IsEqualTo(StreamConfigRetention.Limits);
		await Assert.That(config.Discard).IsEqualTo(StreamConfigDiscard.Old);
		// Configured apart from the bus's transport retention.
		var custom = JetStreamTerminalReplayStore.StreamConfiguration(new ReplayOptions { Retention = TimeSpan.FromHours(6), MaxBytes = 1024 });
		await Assert.That(custom.MaxAge).IsEqualTo(TimeSpan.FromHours(6));
		await Assert.That(custom.MaxBytes).IsEqualTo(1024L);
	}
}
