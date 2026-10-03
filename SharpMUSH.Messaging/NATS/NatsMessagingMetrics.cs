using System.Diagnostics.Metrics;

namespace SharpMUSH.Messaging.NATS;

/// <summary>
/// Bus and broker figures on the <c>SharpMUSH</c> meter, which both hosts export to Prometheus:
/// rejected publications, handler retries and terminal failures, and the latest stream, consumer and
/// JetStream storage readings taken by <see cref="NatsBrokerMonitor"/>.
/// </summary>
public sealed class NatsMessagingMetrics : IDisposable
{
	public const string MeterName = "SharpMUSH";

	private readonly Counter<long> _rejected;
	private readonly Counter<long> _retries;
	private readonly Counter<long> _terminal;
	private readonly Counter<long> _duplicates;
	private volatile NatsBrokerSnapshot _snapshot = NatsBrokerSnapshot.Empty;

	public NatsMessagingMetrics()
	{
		Meter = new Meter(MeterName, "1.0.0");
		_rejected = Meter.CreateCounter<long>("sharpmush.messaging.publish.rejected",
			description: "Publications the broker refused or did not confirm in time, by stream and reason");
		_retries = Meter.CreateCounter<long>("sharpmush.messaging.handler.retries",
			description: "In-process retries of a message whose handler reported a retryable failure");
		_terminal = Meter.CreateCounter<long>("sharpmush.messaging.handler.terminal_failures",
			description: "Messages terminated without success, by consumer and reason");
		_duplicates = Meter.CreateCounter<long>("sharpmush.messaging.handler.duplicates",
			description: "Redeliveries of a message this process had already handled, acknowledged without running again");
		Meter.CreateObservableGauge("sharpmush.messaging.stream.bytes",
			() => _snapshot.Streams.Select(stream => new Measurement<long>(stream.Bytes, Tag("stream", stream.Name))),
			unit: "By", description: "Bytes held by a JetStream stream");
		Meter.CreateObservableGauge("sharpmush.messaging.stream.max_bytes",
			() => _snapshot.Streams.Select(stream => new Measurement<long>(stream.MaxBytes, Tag("stream", stream.Name))),
			unit: "By", description: "A JetStream stream's byte budget (-1 for none)");
		Meter.CreateObservableGauge("sharpmush.messaging.stream.messages",
			() => _snapshot.Streams.Select(stream => new Measurement<long>(stream.Messages, Tag("stream", stream.Name))),
			description: "Messages held by a JetStream stream");
		Meter.CreateObservableGauge("sharpmush.messaging.consumer.pending",
			() => _snapshot.Consumers.Select(consumer => new Measurement<long>(consumer.Pending, ConsumerTags(consumer))),
			description: "Messages a consumer has not been delivered yet");
		Meter.CreateObservableGauge("sharpmush.messaging.consumer.ack_pending",
			() => _snapshot.Consumers.Select(consumer => new Measurement<long>(consumer.AckPending, ConsumerTags(consumer))),
			description: "Messages delivered to a consumer and not yet acknowledged");
		Meter.CreateObservableGauge("sharpmush.messaging.consumer.redelivered",
			() => _snapshot.Consumers.Select(consumer => new Measurement<long>(consumer.Redelivered, ConsumerTags(consumer))),
			description: "Messages a consumer has had delivered more than once");
		Meter.CreateObservableGauge("sharpmush.messaging.jetstream.storage.used",
			() => _snapshot.Storage is { } storage ? [new Measurement<long>(storage.Used)] : Array.Empty<Measurement<long>>(),
			unit: "By", description: "File storage JetStream uses for this account");
		Meter.CreateObservableGauge("sharpmush.messaging.jetstream.storage.available",
			() => _snapshot.Storage is { Limit: > 0 } storage ? [new Measurement<long>(storage.Limit - storage.Used)] : Array.Empty<Measurement<long>>(),
			unit: "By", description: "File storage left before JetStream's account budget (max_file_store) is reached");
	}

	/// <summary>The meter these instruments belong to; tests listen to this instance only.</summary>
	public Meter Meter { get; }

	/// <summary>The latest broker reading.</summary>
	public NatsBrokerSnapshot Snapshot => _snapshot;

	public void RecordRejected(string stream, string reason) =>
		_rejected.Add(1, Tag("stream", stream), Tag("reason", reason));

	public void RecordRetry(string durable) => _retries.Add(1, Tag("consumer", durable));

	public void RecordTerminal(string durable, string reason) =>
		_terminal.Add(1, Tag("consumer", durable), Tag("reason", reason));

	public void RecordDuplicate(string durable) => _duplicates.Add(1, Tag("consumer", durable));

	public void Record(NatsBrokerSnapshot snapshot) => _snapshot = snapshot;

	private static KeyValuePair<string, object?> Tag(string key, object? value) => new(key, value);

	private static KeyValuePair<string, object?>[] ConsumerTags(NatsConsumerReading consumer) =>
		[Tag("stream", consumer.Stream), Tag("consumer", consumer.Durable)];

	public void Dispose() => Meter.Dispose();
}

public sealed record NatsStreamReading(string Name, long Bytes, long Messages, long MaxBytes);

public sealed record NatsConsumerReading(string Stream, string Durable, long Pending, long AckPending, long Redelivered);

public readonly record struct NatsStorageReading(long Used, long Limit);

/// <summary>One reading of the broker. <see cref="Storage"/> is absent until the account has been read.</summary>
public sealed record NatsBrokerSnapshot(
	IReadOnlyList<NatsStreamReading> Streams,
	IReadOnlyList<NatsConsumerReading> Consumers,
	NatsStorageReading? Storage)
{
	public static NatsBrokerSnapshot Empty { get; } = new([], [], null);
}
