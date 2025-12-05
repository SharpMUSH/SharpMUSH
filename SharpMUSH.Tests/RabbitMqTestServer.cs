using Testcontainers.Redpanda;
using TUnit.Core.Interfaces;

namespace SharpMUSH.Tests;

public class RedPandaTestServer : IAsyncInitializer, IAsyncDisposable
{
	public RedpandaContainer Instance { get; } = new RedpandaBuilder()
		.WithPortBinding(9092, 9092)
		// Configure 6MB message size limit for production compatibility
		.WithCommand("--kafka-api", "plaintext://0.0.0.0:9092")
		.WithCommand("--set", "kafka_batch_max_bytes=6291456") // 6MB
		.Build();

	public async Task InitializeAsync() => await Instance.StartAsync();
	public async ValueTask DisposeAsync() => await Instance.DisposeAsync();
}