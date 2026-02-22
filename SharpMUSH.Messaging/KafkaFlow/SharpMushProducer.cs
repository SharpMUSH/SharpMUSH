using KafkaFlow;
using Microsoft.Extensions.Logging;
using SharpMUSH.Messages;

namespace SharpMUSH.Messaging.KafkaFlow;

/// <summary>
/// KafkaFlow producer class that handles message production.
/// Following KafkaFlow's type-based producer pattern, this class encapsulates
/// the message production logic and can be injected where needed.
/// </summary>
public class SharpMushProducer(IMessageProducer<SharpMushProducer> producer, ILogger<SharpMushProducer> logger)
{
	/// <summary>
	/// Produces a message to the appropriate topic
	/// </summary>
	public async Task ProduceAsync<T>(T message, CancellationToken cancellationToken = default) where T : class
	{
		var topic = GetTopicForMessageType<T>();
		logger.LogTrace("[KAFKA-SEND] Publishing message to topic {Topic} - Type: {MessageType}",
			topic, typeof(T).Name);

		await producer.ProduceAsync(topic, null, message);

		logger.LogTrace("[KAFKA-SEND] Successfully published message to topic {Topic} - Type: {MessageType}",
			topic, typeof(T).Name);
	}

	/// <summary>
	/// Produces a message with a partition key based on the Handle property
	/// </summary>
	public async Task HandleProduceAsync<T>(T message, CancellationToken cancellationToken = default) where T : IHandleMessage
	{
		var topic = GetTopicForMessageType<T>();
		var partitionKey = message.Handle.ToString();

		logger.LogTrace("[KAFKA-SEND] Publishing handle-based message to topic {Topic} - Type: {MessageType}, Handle: {Handle}, PartitionKey: {PartitionKey}",
			topic, typeof(T).Name, message.Handle, partitionKey);

		await producer.ProduceAsync(topic, partitionKey, message);

		logger.LogTrace("[KAFKA-SEND] Successfully published handle-based message to topic {Topic} - Type: {MessageType}, Handle: {Handle}",
			topic, typeof(T).Name, message.Handle);
	}

	private static string GetTopicForMessageType<T>()
	{
		// TODO: If we stick with this, this needs caching.
		var messageType = typeof(T);

		// Use convention-based topic naming: convert "TelnetOutputMessage" to "telnet-output"
		var typeName = messageType.Name;
		if (typeName.EndsWith("Message"))
		{
			typeName = typeName[..^7]; // Remove "Message" suffix
		}

		// Convert PascalCase to kebab-case
		var kebabCase = string.Concat(
			typeName.Select((c, i) => i > 0 && char.IsUpper(c) ? "-" + c : c.ToString())
		).ToLowerInvariant();

		return kebabCase;
	}
}
