using System.Collections.Concurrent;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

namespace SharpMUSH.ConnectionServer.Services;

/// <summary>
/// NATS JetStream-backed <see cref="ITerminalReplayStore"/>. Each session's output is published to
/// <c>terminal.replay.&lt;session&gt;</c> in a short-<see cref="StreamConfig.MaxAge"/> stream, so buffered
/// output survives a ConnectionServer restart / instance change and can be replayed on reconnect.
/// The subject is keyed by a per-incarnation session id (not the reusable handle), so a recycled handle's
/// new occupant can never read a prior session's frames, and a restart that loses the in-memory seq
/// counter cannot collide two sessions onto one subject.
/// </summary>
public sealed class JetStreamTerminalReplayStore : ITerminalReplayStore, IAsyncDisposable
{
	private const string StreamName = "TERMINAL_REPLAY";
	private const string SubjectPrefix = "terminal.replay";
	private static readonly TimeSpan DefaultRetention = TimeSpan.FromHours(24);

	private readonly NatsConnection _nats;
	private readonly NatsJSContext _js;
	private readonly ILogger<JetStreamTerminalReplayStore> _logger;
	private readonly ConcurrentDictionary<string, long> _seq = new();

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
		await js.CreateOrUpdateStreamAsync(
			new StreamConfig(StreamName, [$"{SubjectPrefix}.>"]) { MaxAge = maxAge },
			ct);
		logger.LogInformation("JetStream replay stream '{Stream}' ready (MaxAge {MaxAge})", StreamName, maxAge);
		return new JetStreamTerminalReplayStore(nats, js, logger);
	}

	private static string Subject(string session) => $"{SubjectPrefix}.{session}";

	public async ValueTask<(long Seq, byte[] Wrapped)> AppendAsync(string session, byte[] rawUtf8, CancellationToken ct = default)
	{
		var seq = _seq.AddOrUpdate(session, 1, (_, v) => v + 1);
		var wrapped = SeqEnvelope.Wrap(seq, rawUtf8);
		await _js.PublishAsync(Subject(session), wrapped, cancellationToken: ct);
		return (seq, wrapped);
	}

	public async ValueTask<IReadOnlyList<byte[]>> AfterAsync(string session, long lastSeq, CancellationToken ct = default)
	{
		var result = new List<byte[]>();
		INatsJSConsumer consumer;
		try
		{
			// Ephemeral, no-ack consumer over just this session's subject; auto-cleaned after inactivity.
			consumer = await _js.CreateOrUpdateConsumerAsync(StreamName, new ConsumerConfig
			{
				Name = $"replay-read-{Guid.NewGuid():N}",
				FilterSubject = Subject(session),
				DeliverPolicy = ConsumerConfigDeliverPolicy.All,
				AckPolicy = ConsumerConfigAckPolicy.None,
				InactiveThreshold = TimeSpan.FromSeconds(10),
			}, ct);
		}
		catch (NatsJSApiException ex)
		{
			_logger.LogWarning(ex, "Replay consumer creation failed for session {Session}", session);
			return result;
		}

		await foreach (var msg in consumer.FetchNoWaitAsync<byte[]>(
			new NatsJSFetchOpts { MaxMsgs = 500 }, cancellationToken: ct))
		{
			if (msg.Data is null) continue;
			if (SeqEnvelope.TryReadSeq(msg.Data, out var seq) && seq > lastSeq)
				result.Add(msg.Data);
		}

		return result;
	}

	/// <summary>
	/// Purges a session's buffered frames from the stream and drops its seq counter. Session ids are
	/// unique per incarnation, so isolation does not depend on this — but purging reclaims stream storage
	/// that MaxAge would otherwise hold for the full retention window.
	/// </summary>
	public async ValueTask DropAsync(string session, CancellationToken ct = default)
	{
		_seq.TryRemove(session, out _);
		try
		{
			await _js.PurgeStreamAsync(StreamName, new StreamPurgeRequest { Filter = Subject(session) }, ct);
		}
		catch (NatsJSApiException ex)
		{
			_logger.LogWarning(ex, "Replay purge failed for session {Session}", session);
		}
	}

	public async ValueTask DisposeAsync()
	{
		_seq.Clear();
		await _nats.DisposeAsync();
	}
}
