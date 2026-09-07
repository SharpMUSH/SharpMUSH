using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

namespace SharpMUSH.ConnectionServer.Services;

/// <summary>
/// Durable terminal output, sequenced by JetStream rather than a process-local counter. Sequence
/// gaps between sessions are expected; each session's sequence remains monotonic across restarts.
/// </summary>
public sealed class JetStreamTerminalReplayStore : ITerminalReplayStore, IAsyncDisposable
{
	// v1 persisted already-wrapped frames with process-local sequence numbers. Mixing those with
	// stream sequences would silently skip or duplicate output. Keep a distinct v2 stream; v1 data
	// ages out, and the matching v2 token upgrade deliberately requires one fresh connection.
	private const string StreamName = "TERMINAL_REPLAY_V2";
	private const string SubjectPrefix = "terminal.replay2";
	private static readonly TimeSpan DefaultRetention = TimeSpan.FromHours(24);
	private readonly NatsConnection _nats;
	private readonly NatsJSContext _js;
	private readonly ILogger<JetStreamTerminalReplayStore> _logger;

	private JetStreamTerminalReplayStore(NatsConnection nats, NatsJSContext js, ILogger<JetStreamTerminalReplayStore> logger)
	{
		_nats = nats;
		_js = js;
		_logger = logger;
	}

	public static async Task<JetStreamTerminalReplayStore> CreateAsync(
		string url, ILogger<JetStreamTerminalReplayStore> logger, TimeSpan? retention = null, CancellationToken ct = default)
	{
		var maxAge = retention ?? DefaultRetention;
		var nats = new NatsConnection(new NatsOpts { Url = url });
		await nats.ConnectAsync();
		var js = new NatsJSContext(nats);
		await js.CreateOrUpdateStreamAsync(new StreamConfig(StreamName, [$"{SubjectPrefix}.>"])
		{ MaxAge = maxAge }, ct);
		logger.LogInformation("JetStream replay stream '{Stream}' ready (MaxAge {MaxAge})", StreamName, maxAge);
		return new JetStreamTerminalReplayStore(nats, js, logger);
	}

	private static string Subject(string session) => $"{SubjectPrefix}.{session}";

	public async ValueTask<(long Seq, byte[] Wrapped)> AppendAsync(string session, byte[] rawUtf8, CancellationToken ct = default)
	{
		var acknowledgement = await _js.PublishAsync(Subject(session), rawUtf8, cancellationToken: ct);
		acknowledgement.EnsureSuccess();
		var seq = checked((long)acknowledgement.Seq);
		return (seq, SeqEnvelope.Wrap(seq, rawUtf8));
	}

	public async ValueTask<IReadOnlyList<byte[]>> AfterAsync(string session, long lastSeq, CancellationToken ct = default)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(lastSeq);
		var result = new List<byte[]>();
		var name = $"replay-read-{Guid.NewGuid():N}";
		var consumer = await _js.CreateOrUpdateConsumerAsync(StreamName, new ConsumerConfig
		{
			Name = name,
			FilterSubject = Subject(session),
			DeliverPolicy = ConsumerConfigDeliverPolicy.ByStartSequence,
			OptStartSeq = checked((ulong)lastSeq + 1),
			AckPolicy = ConsumerConfigAckPolicy.None,
			InactiveThreshold = TimeSpan.FromSeconds(10),
		}, ct);
		try
		{
			// Snapshot the available count once: pagination must not run forever on a busy session.
			// The session sink serializes replay/attachment with appends to preserve live ordering.
			var remaining = consumer.Info.NumPending;
			while (remaining > 0)
			{
				var fetched = 0;
				await foreach (var msg in consumer.FetchNoWaitAsync<byte[]>(
					new NatsJSFetchOpts { MaxMsgs = (int)Math.Min(remaining, 500UL) }, cancellationToken: ct))
				{
					fetched++;
					remaining--;
					if (msg.Data is null || msg.Metadata is not { } metadata)
						throw new InvalidDataException("Replay message lacks payload or sequence metadata.");
					result.Add(SeqEnvelope.Wrap(checked((long)metadata.Sequence.Stream), msg.Data));
				}
				if (fetched == 0)
					throw new IncompleteReplayException();
			}
			return result;
		}
		finally
		{
			try { await _js.DeleteConsumerAsync(StreamName, name, CancellationToken.None); }
			catch (Exception ex) { _logger.LogDebug(ex, "Replay consumer cleanup failed for session {Session}", session); }
		}
	}

	public async ValueTask<ReplayReadResult> ReadAsync(string session, long lastSeq, CancellationToken ct = default)
	{
		try { return new(true, await AfterAsync(session, lastSeq, ct)); }
		catch (IncompleteReplayException) { return new(false, []); }
	}

	private sealed class IncompleteReplayException : Exception { }

	public async ValueTask DropAsync(string session, CancellationToken ct = default)
	{
		await _js.PurgeStreamAsync(StreamName, new StreamPurgeRequest { Filter = $"{SubjectPrefix}.{session}" }, ct);
	}

	public async ValueTask DisposeAsync() => await _nats.DisposeAsync();
}
