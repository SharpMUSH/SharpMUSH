using MassTransit;

namespace SharpMUSH.Server.Strategy.RabbitMQ;

public abstract class RabbitMQServiceStrategy
{
	public abstract void ConfigureRabbitMq(IBusRegistrationContext context, IRabbitMqBusFactoryConfigurator cfg);
}
