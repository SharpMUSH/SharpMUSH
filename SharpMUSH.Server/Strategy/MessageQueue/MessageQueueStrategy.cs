using MassTransit;

namespace SharpMUSH.Server.Strategy.MessageQueue;

public abstract class MessageQueueStrategy
{
	public abstract void ConfigureKafka(IBusRegistrationContext context, IKafkaFactoryConfigurator cfg);
}
