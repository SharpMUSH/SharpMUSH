using Testcontainers.Redpanda;
using TUnit.Core.Interfaces;

namespace SharpMUSH.Tests;

public class RedPandaTestServer : IAsyncInitializer, IAsyncDisposable
{
	private RedpandaContainer? _instance;
	
	public RedpandaContainer Instance => _instance ?? throw new InvalidOperationException("Container not initialized. Call InitializeAsync first.");

	public async Task InitializeAsync()
	{
		_instance = new RedpandaBuilder("docker.redpanda.com/redpandadata/redpanda:latest")
			.WithName("sharpmush-test-redpanda")
			.WithLabel("reuse-hash", "sharpmush-redpanda-v1")
			.WithPortBinding(9092, true) // Use dynamic port to avoid conflicts
			// Configure 6MB message size limit for production compatibility
			.WithCommand("--set", "kafka_batch_max_bytes=6291456") // 6MB
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