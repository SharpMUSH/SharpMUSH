using SharpMUSH.Server.Strategy.RabbitMQ;

namespace SharpMUSH.Server.Strategy.ArangoDB;

public static class RabbitMQStrategyProvider
{
	public static RabbitMQServiceStrategy GetStrategy()
	{
		var rabbitMqHost = Environment.GetEnvironmentVariable("RABBITMQ_HOST");

		if (string.IsNullOrWhiteSpace(rabbitMqHost))
		{
			return new RabbitMQTestContainerServiceStrategy();
		}
		else {
			return new RabbitMQContainerServiceStrategy();
		}
	}
}
