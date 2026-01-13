namespace SharpMUSH.Messaging.Abstractions;

/// <summary>
/// Message bus abstraction for publishing messages to Kafka topics
/// </summary>
public interface IMessageBus
{
	/// <summary>
	/// Publishes a message to the appropriate Kafka topic
	/// </summary>
	/// <typeparam name="T">The message type</typeparam>
	/// <param name="message">The message to publish</param>
	/// <param name="cancellationToken">Cancellation token</param>
	Task Publish<T>(T message, CancellationToken cancellationToken = default) where T : class;

	/// <summary>
	/// Sends a message directly to a specific topic
	/// </summary>
	/// <typeparam name="T">The message type</typeparam>
	/// <param name="message">The message to send</param>
	/// <param name="cancellationToken">Cancellation token</param>
	Task Send<T>(T message, CancellationToken cancellationToken = default) where T : class;
}
