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

	[ClassDataSource<DockerNetwork>(Shared = SharedType.PerTestSession)]
	public required DockerNetwork DockerNetwork { get; init; }

	public IContainer Instance => field ??= new ContainerBuilder("redis:7-alpine")
		.WithNetwork(DockerNetwork.Instance)
		.WithPortBinding(RedisPort, true) // Random host port
		.WithCommand("redis-server", "--appendonly", "yes")
		.WithWaitStrategy(Wait.ForUnixContainer().UntilCommandIsCompleted("redis-cli", "ping"))
		.WithReuse(false)
		.Build();

	public async Task InitializeAsync() => await Instance.StartAsync();
	public async ValueTask DisposeAsync() => await Instance.DisposeAsync();
}
