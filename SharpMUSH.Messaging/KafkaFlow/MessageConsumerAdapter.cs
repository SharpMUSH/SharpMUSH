using KafkaFlow;
using Microsoft.Extensions.DependencyInjection;
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
		var consumer = scope.ServiceProvider.GetRequiredService<IMessageConsumer<TMessage>>();
		await consumer.HandleAsync(message, context.ConsumerContext.WorkerStopped);
	}
}
