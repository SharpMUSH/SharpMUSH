using KafkaFlow;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharpMUSH.Messaging.Abstractions;

namespace SharpMUSH.Messaging.KafkaFlow;

/// <summary>
/// Adapter that bridges between KafkaFlow's IMessageHandler and our IMessageConsumer
/// </summary>
/// <typeparam name="TMessage">The message type</typeparam>
public class MessageConsumerAdapter<TMessage>(IServiceProvider serviceProvider) : IMessageHandler<TMessage>
	where TMessage : class
{
	public async Task Handle(IMessageContext context, TMessage message)
	{
		using var scope = serviceProvider.CreateScope();
		var logger = scope.ServiceProvider.GetRequiredService<ILogger<MessageConsumerAdapter<TMessage>>>();

		logger.LogTrace("[KAFKA-ADAPTER] Handling message - Type: {MessageType}, Topic: {Topic}, Partition: {Partition}, Offset: {Offset}",
			typeof(TMessage).Name,
			context.ConsumerContext.Topic,
			context.ConsumerContext.Partition,
			context.ConsumerContext.Offset);

		var consumer = scope.ServiceProvider.GetRequiredService<IMessageConsumer<TMessage>>();

		logger.LogTrace("[KAFKA-ADAPTER] Invoking consumer {ConsumerType} for message type {MessageType}",
			consumer.GetType().Name, typeof(TMessage).Name);

		await consumer.HandleAsync(message, context.ConsumerContext.WorkerStopped);

		logger.LogTrace("[KAFKA-ADAPTER] Successfully handled message - Type: {MessageType}, Consumer: {ConsumerType}",
			typeof(TMessage).Name, consumer.GetType().Name);
	}
}
