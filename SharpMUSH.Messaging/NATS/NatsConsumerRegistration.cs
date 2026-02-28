namespace SharpMUSH.Messaging.NATS;

/// <summary>
/// Holds the metadata and dispatch delegate for a single NATS JetStream consumer.
/// </summary>
public record NatsConsumerRegistration(
	Type MessageType,
	string Subject,
	string DurableName,
	Func<IServiceProvider, object, CancellationToken, Task> Handler);

/// <summary>
/// Registry that accumulates <see cref="NatsConsumerRegistration"/> entries built by
/// <see cref="NatsConsumerConfigurator"/> during DI setup.
/// </summary>
public sealed class NatsConsumerRegistry
{
	public List<NatsConsumerRegistration> Registrations { get; } = [];
}
