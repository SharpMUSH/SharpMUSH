namespace SharpMUSH.Server.Strategy.MessageQueue;

public static class MessageQueueStrategyProvider
{
	public static MessageQueueStrategy GetStrategy()
	{
		var kafkaHost = Environment.GetEnvironmentVariable("KAFKA_HOST");

		if (string.IsNullOrWhiteSpace(kafkaHost))
		{
			return new RedPandaTestContainerStrategy();
		}
		else {
			return new RedPandaContainerStrategy();
		}
	}
}
