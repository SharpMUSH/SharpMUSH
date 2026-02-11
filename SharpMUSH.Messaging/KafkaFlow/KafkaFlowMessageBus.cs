using KafkaFlow;
using KafkaFlow.Producers;
using SharpMUSH.Messaging.Abstractions;

namespace SharpMUSH.Messaging.KafkaFlow;

/// <summary>
/// KafkaFlow-based implementation of IMessageBus
/// </summary>
public class KafkaFlowMessageBus : IMessageBus
{
	private readonly IProducerAccessor _producerAccessor;
	private const string ProducerName = "sharpmush-producer";

	public KafkaFlowMessageBus(IProducerAccessor producerAccessor)
	{
		_producerAccessor = producerAccessor;
	}

	public async Task Publish<T>(T message, CancellationToken cancellationToken = default) where T : class
	{
		var topic = GetTopicForMessageType<T>();
		var partitionKey = GetPartitionKey(message);
		
		var producer = _producerAccessor.GetProducer(ProducerName);
		await producer.ProduceAsync(topic, partitionKey, message);
	}

	public Task Send<T>(T message, CancellationToken cancellationToken = default) where T : class
	{
		// For KafkaFlow, Publish and Send are the same operation
		return Publish(message, cancellationToken);
	}

	/// <summary>
	/// Gets a partition key for the message to ensure ordering of related messages.
	/// Messages with the same key are guaranteed to be delivered to the same partition in order.
	/// </summary>
	private static string GetPartitionKey<T>(T message) where T : class
	{
		// Check for a Handle property using reflection
		var handleProperty = typeof(T).GetProperty("Handle");
		if (handleProperty?.PropertyType == typeof(long))
		{
			var handle = (long?)handleProperty.GetValue(message);
			if (handle.HasValue)
			{
				// Use the handle as the partition key to ensure all messages
				// for the same connection go to the same partition
				return handle.Value.ToString();
			}
		}

		// For messages without a Handle, use a random key for load balancing
		return Guid.NewGuid().ToString();
	}

	private static string GetTopicForMessageType<T>()
	{
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
