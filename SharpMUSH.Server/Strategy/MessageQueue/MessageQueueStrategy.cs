namespace SharpMUSH.Server.Strategy.MessageQueue;

/// <summary>
/// Base class for message queue configuration strategies.
/// Reserved for future use to support different Kafka cluster configurations,
/// testing environments (real Kafka vs RedPanda), and production vs development setups.
/// </summary>
/// <remarks>
/// MassTransit has been replaced with direct Confluent.Kafka implementation.
/// See KAFKA_MIGRATION.md for details on the migration.
/// </remarks>
public abstract class MessageQueueStrategy
{
	// Reserved for future Kafka configuration options specific to deployment strategy
}
