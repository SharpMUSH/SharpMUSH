using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using TUnit.Core.Interfaces;

namespace SharpMUSH.Tests;

/// <summary>
/// Testcontainer for Redis instance for connection state sharing.
/// Exposes Redis port (6379) with AOF persistence enabled.
/// </summary>
public class RedisTestServer : IAsyncInitializer, IAsyncDisposable
{
	private const int RedisPort = 6379;
	private IContainer? _instance;

	public IContainer Instance => _instance ?? throw new InvalidOperationException("Container not initialized. Call InitializeAsync first.");

	public async Task InitializeAsync()
	{
		_instance = new ContainerBuilder("redis:7-alpine")
			.WithName("sharpmush-test-redis")
			.WithLabel("reuse-hash", "sharpmush-redis-v1")
			.WithPortBinding(RedisPort, true) // Random host port
			.WithCommand("redis-server", "--appendonly", "yes")
			.WithWaitStrategy(Wait.ForUnixContainer().UntilCommandIsCompleted("redis-cli", "ping"))
			.WithReuse(true)
			.Build();
		await _instance.StartAsync();
	}
	
	public async ValueTask DisposeAsync()
	{
		if (_instance != null)
		{
			await _instance.DisposeAsync();
		}
	}
}
