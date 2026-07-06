using System.Collections.Concurrent;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

namespace SharpMUSH.ConnectionServer.Services;

/// <summary>
/// NATS JetStream-backed <see cref="ITerminalReplayStore"/>. Each handle's output is published to
/// <c>terminal.replay.&lt;handle&gt;</c> in a short-<see cref="StreamConfig.MaxAge"/> stream, so buffered
/// output survives a ConnectionServer restart / instance change and can be replayed on reconnect.
/// The per-handle sequence counter is in-memory (a handle is owned by one connection for its life);
/// durability of the buffer itself is what enables replay after a restart into a new handle.
/// </summary>
public sealed class JetStreamTerminalReplayStore : ITerminalReplayStore, IAsyncDisposable
{
	private const string StreamName = "TERMINAL_REPLAY";
	private const string SubjectPrefix = "terminal.replay";
	private static readonly TimeSpan DefaultRetention = TimeSpan.FromHours(24);

	private readonly NatsConnection _nats;
	private readonly NatsJSContext _js;
	private readonly ILogger<JetStreamTerminalReplayStore> _logger;
	private readonly ConcurrentDictionary<long, long> _seq = new();

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

	private static string Subject(long handle) => $"{SubjectPrefix}.{handle}";

	public async ValueTask<(long Seq, byte[] Wrapped)> AppendAsync(long handle, byte[] rawUtf8, CancellationToken ct = default)
	{
		var seq = _seq.AddOrUpdate(handle, 1, (_, v) => v + 1);
		var wrapped = SeqEnvelope.Wrap(seq, rawUtf8);
		await _js.PublishAsync(Subject(handle), wrapped, cancellationToken: ct);
		return (seq, wrapped);
	}

	public async ValueTask<IReadOnlyList<byte[]>> AfterAsync(long handle, long lastSeq, CancellationToken ct = default)
	{
		var result = new List<byte[]>();
		INatsJSConsumer consumer;
		try
		{
			// Ephemeral, no-ack consumer over just this handle's subject; auto-cleaned after inactivity.
			consumer = await _js.CreateOrUpdateConsumerAsync(StreamName, new ConsumerConfig
			{
				Name = $"replay-read-{handle}-{Guid.NewGuid():N}",
				FilterSubject = Subject(handle),
				DeliverPolicy = ConsumerConfigDeliverPolicy.All,
				AckPolicy = ConsumerConfigAckPolicy.None,
				InactiveThreshold = TimeSpan.FromSeconds(10),
			}, ct);
		}
		catch (NatsJSApiException ex)
		{
			_logger.LogWarning(ex, "Replay consumer creation failed for handle {Handle}", handle);
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

	// MaxAge bounds the stream; explicit purge is unnecessary for the minimal safety net.
	public ValueTask DropAsync(long handle, CancellationToken ct = default) => ValueTask.CompletedTask;

	public async ValueTask DisposeAsync()
	{
		_seq.Clear();
		await _nats.DisposeAsync();
	}
}
