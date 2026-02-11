using KafkaFlow;
using KafkaFlow.Producers;
using SharpMUSH.Messages;
using SharpMUSH.Messaging.Abstractions;

namespace SharpMUSH.Messaging.KafkaFlow;

/// <summary>
/// KafkaFlow-based implementation of IMessageBus
/// </summary>
public class KafkaFlowMessageBus(IProducerAccessor producerAccessor) : IMessageBus
{
	private const string ProducerName = "sharpmush-producer";

	public async Task Publish<T>(T message, CancellationToken cancellationToken = default) where T : class
	{
		var topic = GetTopicForMessageType<T>();
		
		var producer = producerAccessor.GetProducer(ProducerName);
		await producer.ProduceAsync(topic, message);
	}
	
	public async Task HandlePublish<T>(T message, CancellationToken cancellationToken = default) where T : IHandleMessage
	{
		var topic = GetTopicForMessageType<T>();
		var partitionKey = message.Handle;
		
		var producer = producerAccessor.GetProducer(ProducerName);
		await producer.ProduceAsync(topic, partitionKey, message);
	}

	public Task Send<T>(T message, CancellationToken cancellationToken = default) where T : class
	{
		return Publish(message, cancellationToken);
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
