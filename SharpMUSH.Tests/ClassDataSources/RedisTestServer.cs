using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using TUnit.Core.Interfaces;

namespace SharpMUSH.Tests.ClassDataSources;

/// <summary>
/// Testcontainer for Redis instance for connection state sharing.
/// Exposes Redis port (6379) with AOF persistence enabled.
/// </summary>
public class RedisTestServer : IAsyncInitializer, IAsyncDisposable
{
	private const int RedisPort = 6379;

	public IContainer Instance { get; } = new ContainerBuilder("redis:7-alpine")
		.WithName("sharpmush-test-redis")
		.WithLabel("reuse-hash", "sharpmush-redis-v1")
		.WithPortBinding(RedisPort, true) // Random host port
		.WithCommand("redis-server", "--appendonly", "yes")
		.WithWaitStrategy(Wait.ForUnixContainer().UntilCommandIsCompleted("redis-cli", "ping"))
		.WithReuse(true)
		.Build();

	public async Task InitializeAsync()
	{
		await Instance.StartAsync();
		Environment.SetEnvironmentVariable("REDIS_TEST_CONNECTION_STRING", $"localhost:{Instance.GetMappedPublicPort(RedisPort)}");
	}

	public async ValueTask DisposeAsync()
	{
		await Instance.DisposeAsync();
	}
}