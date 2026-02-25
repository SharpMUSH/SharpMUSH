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
}
