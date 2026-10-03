using System.Buffers;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using SharpMUSH.SocketServer.Configuration;

namespace SharpMUSH.SocketServer.Services;

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
	public const string ReplayStreamName = StreamName;

	/// <summary>How long an abandoned replay reader survives on the broker if this process dies mid-replay.</summary>
	private static readonly TimeSpan ReaderInactiveThreshold = TimeSpan.FromSeconds(60);
	private readonly NatsConnection _nats;
	private readonly INatsJSContext _js;
	private readonly TimeSpan _publishTimeout;
	private readonly ReplayOptions _options;
	private readonly ILogger<JetStreamTerminalReplayStore> _logger;

	internal JetStreamTerminalReplayStore(NatsConnection nats, INatsJSContext js, ILogger<JetStreamTerminalReplayStore> logger,
		TimeSpan? publishTimeout = null, ReplayOptions? options = null)
	{
		_publishTimeout = publishTimeout ?? TimeSpan.FromSeconds(2);
		ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(_publishTimeout, TimeSpan.Zero);
		_options = options ?? new ReplayOptions();
		_options.Validate();
		_nats = nats;
		_js = js;
		_logger = logger;
	}

	/// <summary>
	/// The replay stream is an archive read back by sequence, so it keeps limits retention; when its
	/// byte budget is reached the oldest frames go (discard-old), never a session's newest output. This
	/// process is its only writer and owns its configuration.
	/// </summary>
	internal static StreamConfig StreamConfiguration(ReplayOptions options) => new(StreamName, [$"{SubjectPrefix}.>"])
	{
		Retention = StreamConfigRetention.Limits,
		Discard = StreamConfigDiscard.Old,
		MaxAge = options.Retention,
		MaxBytes = options.MaxBytes,
	};

	public static async Task<JetStreamTerminalReplayStore> CreateAsync(
		string url, ILogger<JetStreamTerminalReplayStore> logger, ReplayOptions? options = null, CancellationToken ct = default)
	{
		options ??= new ReplayOptions();
		options.Validate();
		var nats = new NatsConnection(new NatsOpts { Url = url });
		try
		{
			await nats.ConnectAsync();
			var js = new NatsJSContext(nats);
			await js.CreateOrUpdateStreamAsync(StreamConfiguration(options), ct);
			logger.LogInformation("JetStream replay stream '{Stream}' ready (MaxAge {MaxAge}, MaxBytes {MaxBytes})",
				StreamName, options.Retention, options.MaxBytes);
			return new JetStreamTerminalReplayStore(nats, js, logger, options: options);
		}
		catch
		{
			await nats.DisposeAsync();
			throw;
		}
	}

	/// <summary>
	/// Every character that cannot appear in a literal NATS subject token: the separator and the two
	/// wildcards, plus all whitespace and control characters.
	/// </summary>
	private static readonly SearchValues<char> NotASubjectToken = SearchValues.Create(
		Enumerable.Range(char.MinValue, char.MaxValue + 1)
			.Select(codePoint => (char)codePoint)
			.Where(c => c is '.' or '*' or '>' || char.IsWhiteSpace(c) || char.IsControl(c))
			.ToArray());

	private static string Subject(string session)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(session);
		if (session.AsSpan().ContainsAny(NotASubjectToken))
			throw new ArgumentException("A replay session must be a single literal NATS subject token.", nameof(session));
		return $"{SubjectPrefix}.{session}";
	}

	public async ValueTask<(long Seq, byte[] Wrapped)> AppendAsync(string session, byte[] rawUtf8, CancellationToken ct = default)
	{
		var subject = Subject(session);
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
		deadline.CancelAfter(_publishTimeout);
		PubAckResponse acknowledgement;
		try
		{
			acknowledgement = await _js.PublishAsync(subject, rawUtf8, cancellationToken: deadline.Token);
		}
		catch (OperationCanceledException ex) when (!ct.IsCancellationRequested && deadline.IsCancellationRequested)
		{
			throw new TimeoutException($"Replay publish did not complete within {_publishTimeout}.", ex);
		}
		acknowledgement.EnsureSuccess();
		var seq = checked((long)acknowledgement.Seq);
		return (seq, SeqEnvelope.Wrap(seq, rawUtf8));
	}

	public async ValueTask<ReplayOpening> OpenAsync(string session, long lastSeq, CancellationToken ct = default)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(lastSeq);
		var subject = Subject(session);
		var name = $"replay-read-{Guid.NewGuid():N}";
		// Start AT the client's last frame when it has one. A session's frames leave the stream oldest
		// first — by age, by the byte budget, by a purge — so finding that frame still retained proves
		// every later one is too. A client with nothing yet (lastSeq 0) has no such anchor.
		var consumer = await _js.CreateOrUpdateConsumerAsync(StreamName, new ConsumerConfig
		{
			Name = name,
			FilterSubject = subject,
			DeliverPolicy = lastSeq == 0 ? ConsumerConfigDeliverPolicy.All : ConsumerConfigDeliverPolicy.ByStartSequence,
			OptStartSeq = lastSeq == 0 ? 0 : checked((ulong)lastSeq),
			AckPolicy = ConsumerConfigAckPolicy.None,
			InactiveThreshold = ReaderInactiveThreshold,
		}, ct);
		var handedOff = false;
		try
		{
			// Snapshot the available count once: pagination must not run forever on a busy session.
			// The session sink serializes replay/attachment with appends to preserve live ordering.
			var pending = consumer.Info.NumPending;
			if (lastSeq > 0)
			{
				if (pending == 0 || await FirstSequenceAsync(consumer, ct) != checked((ulong)lastSeq))
					return new IncompleteReplay(ReplayGap.Expired);
				pending--;
			}
			if (_options.MaxFrames > 0 && pending > (ulong)_options.MaxFrames)
				return new IncompleteReplay(ReplayGap.OverBudget);
			handedOff = true;
			return new PagedFrames(this, consumer, name, session, pending);
		}
		finally
		{
			if (!handedOff) await DeleteReaderAsync(name, session);
		}
	}

	private static async Task<ulong?> FirstSequenceAsync(INatsJSConsumer consumer, CancellationToken ct)
	{
		await foreach (var msg in consumer.FetchNoWaitAsync<byte[]>(new NatsJSFetchOpts { MaxMsgs = 1 }, cancellationToken: ct))
			return msg.Metadata?.Sequence.Stream;
		return null;
	}

	private async ValueTask DeleteReaderAsync(string name, string session)
	{
		try { await _js.DeleteConsumerAsync(StreamName, name, CancellationToken.None); }
		// Best effort: the reader's inactivity threshold removes it on the broker regardless.
		catch (Exception ex) { _logger.LogDebug(ex, "Replay consumer cleanup failed for session {Session}", session); }
	}

	/// <summary>
	/// A replay read from its own ephemeral consumer, at most <see cref="ReplayOptions.PageSize"/> frames
	/// from the broker at a time, each page handed on before the next is fetched.
	/// </summary>
	private sealed class PagedFrames(JetStreamTerminalReplayStore store, INatsJSConsumer consumer, string name,
		string session, ulong count) : ReplayFrames
	{
		public override async IAsyncEnumerable<byte[]> ReadAsync(
			[System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
		{
			var remaining = count;
			while (remaining > 0)
			{
				var fetched = 0;
				await foreach (var msg in consumer.FetchNoWaitAsync<byte[]>(
					new NatsJSFetchOpts { MaxMsgs = (int)Math.Min(remaining, (ulong)store._options.PageSize) }, cancellationToken: ct))
				{
					fetched++;
					remaining--;
					if (msg.Data is null || msg.Metadata is not { } metadata)
						throw new InvalidDataException("Replay message lacks payload or sequence metadata.");
					yield return SeqEnvelope.Wrap(checked((long)metadata.Sequence.Stream), msg.Data);
				}
				if (fetched == 0)
					throw new ReplayInterruptedException();
			}
		}

		public override async ValueTask DisposeAsync() => await store.DeleteReaderAsync(name, session);
	}

	public async ValueTask DropAsync(string session, CancellationToken ct = default)
	{
		await _js.PurgeStreamAsync(StreamName, new StreamPurgeRequest { Filter = Subject(session) }, ct);
	}

	public async ValueTask DisposeAsync() => await _nats.DisposeAsync();
}
