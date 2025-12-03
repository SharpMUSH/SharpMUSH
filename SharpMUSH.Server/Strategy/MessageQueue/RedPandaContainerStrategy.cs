using MassTransit;

namespace SharpMUSH.Server.Strategy.MessageQueue;

public class RedPandaContainerStrategy : MessageQueueStrategy
{
	public override void ConfigureKafka(IBusRegistrationContext context, IKafkaFactoryConfigurator cfg)
	{
		var kafkaHost = Environment.GetEnvironmentVariable("KAFKA_HOST") 
			?? throw new NullReferenceException("KAFKA_HOST");
		var kafkaPort = Environment.GetEnvironmentVariable("KAFKA_PORT") ?? "9092";
		
		cfg.Host($"{kafkaHost}:{kafkaPort}");
	}
}
