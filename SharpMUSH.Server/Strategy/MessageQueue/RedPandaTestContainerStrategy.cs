using MassTransit;

namespace SharpMUSH.Server.Strategy.MessageQueue;

public class RedPandaTestContainerStrategy : MessageQueueStrategy
{
	public override void ConfigureKafka(IBusRegistrationContext context, IKafkaFactoryConfigurator cfg)
	{
		cfg.Host("localhost:9092");
	}
}
