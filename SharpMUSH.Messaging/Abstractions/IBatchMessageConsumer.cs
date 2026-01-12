namespace SharpMUSH.Messaging.Abstractions;

/// <summary>
/// Interface for batch Kafka message consumers (for performance optimization)
/// </summary>
/// <typeparam name="T">The message type to consume</typeparam>
public interface IBatchMessageConsumer<in T> where T : class
{
	/// <summary>
	/// Handles a batch of consumed messages from Kafka
	/// </summary>
	/// <param name="messages">The messages to handle</param>
	/// <param name="cancellationToken">Cancellation token</param>
	Task HandleBatchAsync(IReadOnlyList<T> messages, CancellationToken cancellationToken = default);
}
