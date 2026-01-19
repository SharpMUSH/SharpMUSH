namespace SharpMUSH.Server.Strategy.MessageQueue;

public static class MessageQueueStrategyProvider
{
	public static MessageQueueStrategy GetStrategy()
	{
		// Check for test-specific environment variables first (set by TestClassFactory)
		var kafkaTestHost = Environment.GetEnvironmentVariable("KAFKA_TEST_HOST");
		
		if (!string.IsNullOrWhiteSpace(kafkaTestHost))
		{
			return new RedPandaTestContainerStrategy();
		}
		
		// Check for production environment variables
		var kafkaHost = Environment.GetEnvironmentVariable("KAFKA_HOST");
		
		if (!string.IsNullOrWhiteSpace(kafkaHost))
		{
			return new RedPandaContainerStrategy();
		}
		
		// Fallback to test container strategy for local development
		return new RedPandaTestContainerStrategy();
	}
}
