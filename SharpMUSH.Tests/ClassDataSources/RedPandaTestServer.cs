using Testcontainers.Redpanda;
using TUnit.Core.Interfaces;

namespace SharpMUSH.Tests.ClassDataSources;

public class RedPandaTestServer : IAsyncInitializer, IAsyncDisposable
{
	public RedpandaContainer Instance { get; } = new RedpandaBuilder("docker.redpanda.com/redpandadata/redpanda:latest")
		.WithName("sharpmush-test-redpanda")
		.WithLabel("reuse-hash", "sharpmush-redpanda-v2") // v2 to force recreation with new settings
		.WithPortBinding(9092, true) // Use dynamic port to avoid conflicts
		// Configure 6MB message size limit and enable auto-creation of topics
		.WithCommand(
			"--set", "kafka_batch_max_bytes=6291456", // 6MB
			"--set", "auto_create_topics_enabled=true", // Enable auto-creation of topics
			"--set", "default_topic_partitions=3" // Default 3 partitions for new topics
		)
		.WithReuse(true)
		.Build();

	public async Task InitializeAsync()
	{
		await Instance.StartAsync();
		
		// Set test-specific environment variables for Kafka/RedPanda connection
		var kafkaPort = Instance.GetMappedPublicPort(9092);
		Environment.SetEnvironmentVariable("KAFKA_TEST_HOST", "localhost");
		Environment.SetEnvironmentVariable("KAFKA_TEST_PORT", kafkaPort.ToString());
	}

	public async ValueTask DisposeAsync()
	{
		await Instance.DisposeAsync();
	}
}