namespace SharpMUSH.Server.Strategy.Redis;

/// <summary>
/// Provides the appropriate Redis strategy based on environment configuration.
/// </summary>
public static class RedisStrategyProvider
{
	public static RedisStrategy GetStrategy()
	{
		// Connect to REDIS_CONNECTION if set, otherwise default to the instance owned by ConnectionServer.
		// Mirrors how Server connects to Kafka: KAFKA_HOST defaults to "localhost" without starting a container.
		var redisConnection = Environment.GetEnvironmentVariable("REDIS_CONNECTION") ?? "localhost:6379";
		return new RedisExternalStrategy(redisConnection);
	}
}
