namespace SharpMUSH.Messaging.Abstractions;

/// <summary>
/// Interface for batch message consumers (for performance optimization)
/// </summary>
/// <typeparam name="T">The message type to consume</typeparam>
public interface IBatchMessageConsumer<T> where T : class
{
	/// <summary>
	/// Consumes a batch of messages
	/// </summary>
	/// <param name="messages">The messages to consume</param>
	/// <param name="cancellationToken">Cancellation token</param>
	Task ConsumeBatch(IReadOnlyList<T> messages, CancellationToken cancellationToken = default);
}
