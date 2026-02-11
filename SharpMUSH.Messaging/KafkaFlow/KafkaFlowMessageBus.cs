using KafkaFlow;
using KafkaFlow.Producers;
using SharpMUSH.Messages;
using SharpMUSH.Messaging.Abstractions;

namespace SharpMUSH.Messaging.KafkaFlow;

/// <summary>
/// KafkaFlow-based implementation of IMessageBus.
/// Delegates to SharpMushProducer following KafkaFlow best practices.
/// </summary>
public class KafkaFlowMessageBus(SharpMushProducer producer) : IMessageBus
{
	public Task Publish<T>(T message, CancellationToken cancellationToken = default) where T : class
	{
		return producer.ProduceAsync(message, cancellationToken);
	}
	
	public Task HandlePublish<T>(T message, CancellationToken cancellationToken = default) where T : IHandleMessage
	{
		return producer.HandleProduceAsync(message, cancellationToken);
	}
}
