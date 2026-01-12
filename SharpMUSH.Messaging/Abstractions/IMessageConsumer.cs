namespace SharpMUSH.Messaging.Abstractions;

/// <summary>
/// Interface for Kafka message consumers
/// </summary>
/// <typeparam name="T">The message type to consume</typeparam>
public interface IMessageConsumer<in T> where T : class
{
	/// <summary>
	/// Handles a consumed message from Kafka
	/// </summary>
	/// <param name="message">The message to handle</param>
	/// <param name="cancellationToken">Cancellation token</param>
	Task HandleAsync(T message, CancellationToken cancellationToken = default);
}
