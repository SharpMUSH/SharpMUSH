using StackExchange.Redis;

namespace SharpMUSH.ConnectionServer.Strategy;

/// <summary>
/// Strategy for connecting to an external Redis instance.
/// Used in Docker Compose, Kubernetes, or production environments.
/// </summary>
public class RedisExternalStrategy : RedisStrategy
{
	private readonly string _connectionString;
	private IConnectionMultiplexer? _connection;

	public RedisExternalStrategy(string connectionString)
	{
		_connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
	}

	public override async ValueTask<IConnectionMultiplexer> GetConnectionAsync()
	{
		if (_connection == null)
		{
			throw new InvalidOperationException("Redis connection not initialized. Call InitializeAsync first.");
		}

		return await ValueTask.FromResult(_connection);
	}

	public override async ValueTask InitializeAsync()
	{
		var configuration = ConfigurationOptions.Parse(_connectionString);
		configuration.AbortOnConnectFail = false;
		configuration.ConnectRetry = 3;
		configuration.ConnectTimeout = 5000;

		_connection = await ConnectionMultiplexer.ConnectAsync(configuration);
	}

	public override async ValueTask DisposeAsync()
	{
		if (_connection != null)
		{
			await _connection.CloseAsync();
			_connection.Dispose();
			_connection = null;
		}
	}
}
