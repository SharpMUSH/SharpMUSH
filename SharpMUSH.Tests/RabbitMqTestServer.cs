using Testcontainers.Redpanda;
using TUnit.Core.Interfaces;

namespace SharpMUSH.Tests;

public class RedPandaTestServer : IAsyncInitializer, IAsyncDisposable
{
	[ClassDataSource<DockerNetwork>(Shared = SharedType.PerTestSession)]
	public required DockerNetwork DockerNetwork { get; init; }

	public RedpandaContainer Instance => field ??= new RedpandaBuilder("docker.redpanda.com/redpandadata/redpanda:latest")
		.WithNetwork(DockerNetwork.Instance)
		.WithPortBinding(9092, true) // Use dynamic port to avoid conflicts
																 // Configure 6MB message size limit for production compatibility
		.WithCommand("--set", "kafka_batch_max_bytes=6291456") // 6MB
		.Build();

	public async Task InitializeAsync() => await Instance.StartAsync();
	public async ValueTask DisposeAsync() => await Instance.DisposeAsync();
}