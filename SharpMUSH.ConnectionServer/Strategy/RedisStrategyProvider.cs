namespace SharpMUSH.ConnectionServer.Strategy;

/// <summary>
/// Provides the appropriate Redis strategy based on environment configuration.
/// </summary>
public static class RedisStrategyProvider
{
	public static RedisStrategy GetStrategy()
	{
		var redisConnection = Environment.GetEnvironmentVariable("REDIS_CONNECTION");

		if (string.IsNullOrWhiteSpace(redisConnection))
		{
			// No Redis connection configured, use TestContainer for local development
			return new RedisTestContainerStrategy();
		}
		else
		{
			// Redis connection is configured (e.g., in Kubernetes or Docker Compose)
			return new RedisExternalStrategy(redisConnection);
		}
	}
}
