using StackExchange.Redis;

namespace SharpMUSH.ConnectionServer.Strategy;

/// <summary>
/// Base strategy for configuring Redis connection.
/// </summary>
public abstract class RedisStrategy
{
	/// <summary>
	/// Gets the Redis connection multiplexer.
	/// </summary>
	/// <returns>The Redis connection multiplexer</returns>
	public abstract ValueTask<IConnectionMultiplexer> GetConnectionAsync();

	/// <summary>
	/// Performs any initialization required for the Redis configuration.
	/// </summary>
	public abstract ValueTask InitializeAsync();

	/// <summary>
	/// Performs cleanup when shutting down.
	/// </summary>
	public abstract ValueTask DisposeAsync();
}
