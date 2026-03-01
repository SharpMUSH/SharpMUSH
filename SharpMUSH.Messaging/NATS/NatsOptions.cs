namespace SharpMUSH.Messaging.NATS;

/// <summary>
/// Configuration options for NATS JetStream messaging.
/// Mirror of <see cref="Configuration.MessageQueueOptions"/> for the Kafka adapter,
/// allowing both transports to be tuned via the same fields.
/// </summary>
public class NatsOptions
{
	/// <summary>
	/// The NATS server URL (e.g. "nats://localhost:4222")
	/// </summary>
	public string Url { get; set; } = "nats://localhost:4222";

	/// <summary>
	/// Name of the JetStream stream that covers all SharpMUSH subjects.
	/// </summary>
	public string StreamName { get; set; } = "SHARPMUSH";

	/// <summary>
	/// Subject prefix used for all published messages (e.g. "sharpmush").
	/// The full subject becomes "{Prefix}.{kebab-case-type-name}".
	/// </summary>
	public string SubjectPrefix { get; set; } = "sharpmush";

	/// <summary>
	/// Maximum age for messages in the JetStream stream.
	/// After this time messages are evicted from storage.
	/// </summary>
	public TimeSpan MaxAge { get; set; } = TimeSpan.FromHours(24);

	/// <summary>
	/// Name of the JetStream stream to consume from.
	/// When null the value of <see cref="StreamName"/> is used (publish = consume,
	/// suitable for single-stream setups or tests).
	/// </summary>
	public string? ConsumeStreamName { get; set; }

	/// <summary>
	/// Subject prefix used for the consuming side (i.e. the publisher's prefix in the
	/// other application).  When null the value of <see cref="SubjectPrefix"/> is used.
	/// </summary>
	public string? ConsumeSubjectPrefix { get; set; }

	/// <summary>
	/// Maximum size in bytes for a single message accepted by the JetStream stream.
	/// Defaults to 6 MB. Set to -1 for unlimited.
	/// </summary>
	public long MaxMsgSize { get; set; } = 6 * 1024 * 1024;

	internal string GetConsumeStreamName() => ConsumeStreamName ?? StreamName;
	internal string GetConsumeSubjectPrefix() => ConsumeSubjectPrefix ?? SubjectPrefix;
}
