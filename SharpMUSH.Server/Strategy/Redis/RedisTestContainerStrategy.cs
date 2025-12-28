using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using StackExchange.Redis;

namespace SharpMUSH.Server.Strategy.Redis;

/// <summary>
/// Strategy for running Redis in a TestContainer for local development.
/// Automatically starts a Redis container when no external instance is configured.
/// </summary>
public class RedisTestContainerStrategy : RedisStrategy
{
	private IContainer? _container;
	private IConnectionMultiplexer? _connection;
	private const int RedisPort = 6379;

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
		_container = new ContainerBuilder()
			.WithImage("redis:7-alpine")
			.WithPortBinding(RedisPort, true) // Random host port
			.WithCommand("redis-server", "--appendonly", "yes")
			.WithWaitStrategy(Wait.ForUnixContainer().UntilCommandIsCompleted("redis-cli", "ping"))
			.WithReuse(false)
			.Build();

		await _container.StartAsync();

		// Get the mapped port and create connection
		var port = _container.GetMappedPublicPort(RedisPort);
		var connectionString = $"localhost:{port}";
		
		var configuration = ConfigurationOptions.Parse(connectionString);
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

		if (_container != null)
		{
			await _container.DisposeAsync();
			_container = null;
		}
	}
}
