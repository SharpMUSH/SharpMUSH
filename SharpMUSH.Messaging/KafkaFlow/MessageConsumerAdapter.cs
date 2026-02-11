using KafkaFlow;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Messaging.Abstractions;

namespace SharpMUSH.Messaging.KafkaFlow;

/// <summary>
/// Adapter that bridges between KafkaFlow's IMessageHandler and our IMessageConsumer
/// </summary>
/// <typeparam name="TMessage">The message type</typeparam>
public class MessageConsumerAdapter<TMessage> : IMessageHandler<TMessage>
	where TMessage : class
{
	private readonly IServiceProvider _serviceProvider;

	public MessageConsumerAdapter(IServiceProvider serviceProvider)
	{
		_serviceProvider = serviceProvider;
	}

	public async Task Handle(IMessageContext context, TMessage message)
	{
		// Create a scope for this message
		using var scope = _serviceProvider.CreateScope();
		
		// Get the registered consumer from DI
		var consumer = scope.ServiceProvider.GetRequiredService<IMessageConsumer<TMessage>>();
		
		// Handle the message
		await consumer.HandleAsync(message, context.ConsumerContext.WorkerStopped);
	}
}
