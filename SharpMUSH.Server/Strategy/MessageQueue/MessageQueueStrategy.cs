namespace SharpMUSH.Server.Strategy.MessageQueue;

/// <summary>
/// Base class for message queue configuration strategies.
/// Supports different Kafka cluster configurations,
/// testing environments (real Kafka vs RedPanda), and production vs development setups.
/// </summary>
/// <remarks>
/// MassTransit has been replaced with direct Confluent.Kafka implementation.
/// See KAFKA_MIGRATION.md for details on the migration.
/// </remarks>
public abstract class MessageQueueStrategy
{
	/// <summary>
	/// Gets the Kafka/RedPanda host address.
	/// </summary>
	public abstract string Host { get; }
	
	/// <summary>
	/// Gets the Kafka/RedPanda port.
	/// </summary>
	public abstract int Port { get; }
}
