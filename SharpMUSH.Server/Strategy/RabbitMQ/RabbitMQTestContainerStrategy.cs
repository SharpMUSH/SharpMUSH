using MassTransit;

namespace SharpMUSH.Server.Strategy.RabbitMQ;

public class RabbitMQTestContainerServiceStrategy : RabbitMQServiceStrategy
{
	public override void ConfigureRabbitMq(IBusRegistrationContext context, IRabbitMqBusFactoryConfigurator cfg)
	{
		// Do nothing for now.
	}
}
