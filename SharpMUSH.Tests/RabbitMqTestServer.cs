using Testcontainers.Redpanda;
using TUnit.Core.Interfaces;

namespace SharpMUSH.Tests;

public class RedPandaTestServer : IAsyncInitializer, IAsyncDisposable
{
	public RedpandaContainer Instance { get; } = new RedpandaBuilder("docker.redpanda.com/redpandadata/redpanda:latest")
		.WithName("sharpmush-test-redpanda")
		.WithPortBinding(9092, true) // Use dynamic port to avoid conflicts
		// Configure 6MB message size limit for production compatibility
		.WithCommand("--set", "kafka_batch_max_bytes=6291456") // 6MB
		.WithReuse(true)
		.Build();

	public async Task InitializeAsync() => await Instance.StartAsync();
	public async ValueTask DisposeAsync() => await Instance.DisposeAsync();
}