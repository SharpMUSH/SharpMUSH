namespace SharpMUSH.ConnectionServer.Strategy;

/// <summary>
/// Provides the appropriate Redis strategy based on environment configuration.
/// </summary>
public static class RedisStrategyProvider
{
	public static RedisStrategy GetStrategy()
	{
		var redisConnection = Environment.GetEnvironmentVariable("REDIS_CONNECTION");
		var redisTestConnection = Environment.GetEnvironmentVariable("REDIS_TEST_CONNECTION_STRING");
		
		if (string.IsNullOrWhiteSpace(redisConnection))
		{
			return new RedisExternalStrategy(redisConnection!);
		}

		if (string.IsNullOrWhiteSpace(redisTestConnection))
		{
			return new RedisExternalStrategy(redisTestConnection!);
		}
		
		// No Redis connection configured, use TestContainer for local development
		return new RedisTestContainerStrategy();
	}
}
