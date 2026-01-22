using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Testcontainers.Redpanda;
using TUnit.Core.Interfaces;

namespace SharpMUSH.Tests.ClassDataSources;

public class RedPandaTestServer : IAsyncInitializer
{
	private static readonly Lazy<RedpandaContainer> _container = new(() => 
		new RedpandaBuilder("docker.redpanda.com/redpandadata/redpanda:latest")
			.WithName("sharpmush-test-redpanda")
			.WithLabel("reuse-id", "SharpMUSH") // Group with other SharpMUSH containers
			.WithLabel("reuse-hash", "sharpmush-redpanda-v2") // v2 to force recreation with new settings
			.WithPortBinding(9092, true) // Use dynamic port to avoid conflicts
			// Configure 6MB message size limit and enable auto-creation of topics
			.WithCommand(
				"--set", "kafka_batch_max_bytes=6291456", // 6MB
				"--set", "auto_create_topics_enabled=true", // Enable auto-creation of topics
				"--set", "default_topic_partitions=3" // Default 3 partitions for new topics
			)
			.WithReuse(true)
			.Build());
	
	private static bool _initialized;
	private static readonly object _lock = new();

	public RedpandaContainer Instance => _container.Value;

	public async Task InitializeAsync()
	{
		// Ensure container is only started once across all test sessions
		lock (_lock)
		{
			if (_initialized) return;
			_initialized = true;
		}
		
		await Instance.StartAsync();
		
		// Set test-specific environment variables for Kafka/RedPanda connection
		var kafkaPort = Instance.GetMappedPublicPort(9092);
		Environment.SetEnvironmentVariable("KAFKA_TEST_HOST", "localhost");
		Environment.SetEnvironmentVariable("KAFKA_TEST_PORT", kafkaPort.ToString());
		
		// Pre-create all required Kafka topics to avoid "Unknown topic or partition" errors
		// that cause tests to hang while waiting for auto-creation
		await CreateTopicsAsync(Instance.GetBootstrapAddress());
	}
	
	private static async Task CreateTopicsAsync(string bootstrapServers)
	{
		var config = new AdminClientConfig { BootstrapServers = bootstrapServers };
		
		using var adminClient = new AdminClientBuilder(config).Build();
		
		// Define all topics used by the SharpMUSH application
		var topics = new List<TopicSpecification>
		{
			new() { Name = "telnet-input", NumPartitions = 3, ReplicationFactor = 1 },
			new() { Name = "connection-closed", NumPartitions = 3, ReplicationFactor = 1 },
			new() { Name = "connection-established", NumPartitions = 3, ReplicationFactor = 1 },
			new() { Name = "g-m-c-p-signal", NumPartitions = 3, ReplicationFactor = 1 },
			new() { Name = "m-s-d-p-update", NumPartitions = 3, ReplicationFactor = 1 },
			new() { Name = "n-a-w-s-update", NumPartitions = 3, ReplicationFactor = 1 }
		};
		
		try
		{
			await adminClient.CreateTopicsAsync(topics);
			
			// Wait briefly to ensure topics are fully created before tests start
			await Task.Delay(500);
		}
		catch (CreateTopicsException ex) when (ex.Results.All(r => r.Error.Code == ErrorCode.TopicAlreadyExists))
		{
			// Topics already exist (container reused from previous run) - this is fine
		}
	}
}