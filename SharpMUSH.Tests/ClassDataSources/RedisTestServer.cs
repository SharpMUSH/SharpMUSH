using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using TUnit.Core.Interfaces;

namespace SharpMUSH.Tests.ClassDataSources;

/// <summary>
/// Testcontainer for Redis instance for connection state sharing.
/// Exposes Redis port (6379) with AOF persistence enabled.
/// </summary>
public class RedisTestServer : IAsyncInitializer
{
	private const int RedisPort = 6379;
	
	private static readonly Lazy<IContainer> _container = new(() => 
		new ContainerBuilder("redis:7-alpine")
			.WithName("sharpmush-test-redis")
			.WithLabel("reuse-id", "SharpMUSH")
			.WithLabel("reuse-hash", "sharpmush-redis-v1")
			.WithPortBinding(RedisPort, true) // Random host port
			.WithCommand("redis-server", "--appendonly", "yes")
			.WithWaitStrategy(Wait.ForUnixContainer().UntilCommandIsCompleted("redis-cli", "ping"))
			.WithReuse(true)
			.Build());
	
	private static bool _initialized;
	private static readonly object _lock = new();

	public IContainer Instance => _container.Value;

	public async Task InitializeAsync()
	{
		// Ensure container is only started once across all test sessions
		lock (_lock)
		{
			if (_initialized) return;
			_initialized = true;
		}
		
		await Instance.StartAsync();
		Environment.SetEnvironmentVariable("REDIS_TEST_CONNECTION_STRING", $"localhost:{Instance.GetMappedPublicPort(RedisPort)}");
	}
}