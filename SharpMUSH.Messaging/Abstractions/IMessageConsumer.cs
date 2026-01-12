namespace SharpMUSH.Messaging.Abstractions;

/// <summary>
/// Interface for message consumers
/// </summary>
/// <typeparam name="T">The message type to consume</typeparam>
public interface IMessageConsumer<T> where T : class
{
	/// <summary>
	/// Consumes a message
	/// </summary>
	/// <param name="message">The message to consume</param>
	/// <param name="cancellationToken">Cancellation token</param>
	Task Consume(T message, CancellationToken cancellationToken = default);
}
