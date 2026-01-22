namespace SharpMUSH.ConnectionServer.Strategy;

/// <summary>
/// Provides the appropriate Redis strategy based on environment configuration.
/// </summary>
public static class RedisStrategyProvider
{
	public static RedisStrategy GetStrategy()
	{
		var redisTestConnection = Environment.GetEnvironmentVariable("REDIS_TEST_CONNECTION_STRING");
		var redisConnection = Environment.GetEnvironmentVariable("REDIS_CONNECTION");
		
		// Priority 1: Use test connection if available (from RedisTestServer)
		if (!string.IsNullOrWhiteSpace(redisTestConnection))
		{
			return new RedisExternalStrategy(redisTestConnection);
		}

		// Priority 2: Use production connection if configured (e.g., in Kubernetes or Docker Compose)
		if (!string.IsNullOrWhiteSpace(redisConnection))
		{
			return new RedisExternalStrategy(redisConnection);
		}
		
		// Priority 3: Fallback to TestContainer for local development (not recommended for tests)
		return new RedisTestContainerStrategy();
	}
}
