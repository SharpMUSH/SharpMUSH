using KafkaFlow;
using KafkaFlow.Producers;
using SharpMUSH.Messages;
using SharpMUSH.Messaging.Abstractions;

namespace SharpMUSH.Messaging.KafkaFlow;

/// <summary>
/// KafkaFlow-based implementation of IMessageBus using type-based producer pattern.
/// Injects IMessageProducer&lt;SharpMushProducer&gt; following KafkaFlow best practices.
/// </summary>
public class KafkaFlowMessageBus(IMessageProducer<SharpMushProducer> producer) : IMessageBus
{
	public async Task Publish<T>(T message, CancellationToken cancellationToken = default) where T : class
	{
		var topic = GetTopicForMessageType<T>();
		await producer.ProduceAsync(topic, null, message);
	}
	
	public async Task HandlePublish<T>(T message, CancellationToken cancellationToken = default) where T : IHandleMessage
	{
		var topic = GetTopicForMessageType<T>();
		var partitionKey = message.Handle.ToString();
		
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
