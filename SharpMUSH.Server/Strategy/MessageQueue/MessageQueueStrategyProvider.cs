namespace SharpMUSH.Server.Strategy.MessageQueue;

public static class MessageQueueStrategyProvider
{
	public static MessageQueueStrategy GetStrategy()
	{
		var kafkaHost = Environment.GetEnvironmentVariable("KAFKA_HOST");
		var kafkaPort = Environment.GetEnvironmentVariable("KAFKA_PORT");

		if (!string.IsNullOrWhiteSpace(kafkaHost) && !string.IsNullOrWhiteSpace(kafkaPort))
		{
			return new RedPandaContainerStrategy(kafkaHost, kafkaPort);
		}

		var kafkaTestHost = Environment.GetEnvironmentVariable("KAFKA_TEST_HOST");
		var kafkaTestPort = Environment.GetEnvironmentVariable("KAFKA_TEST_PORT");

		if (!string.IsNullOrWhiteSpace(kafkaTestHost) && !string.IsNullOrWhiteSpace(kafkaTestPort))
		{
			return new RedPandaContainerStrategy(kafkaTestHost, kafkaTestPort);
		}

		return new RedPandaTestContainerStrategy();
	}
}