using Testcontainers.Redpanda;

namespace SharpMUSH.Server.Strategy.MessageQueue;

/// <summary>
/// Strategy for RedPanda test container configuration.
/// Reads Kafka connection settings from test-specific environment variables set by test infrastructure.
/// Uses KAFKA_TEST_HOST and KAFKA_TEST_PORT to distinguish from production settings.
/// </summary>
public class RedPandaTestContainerStrategy : MessageQueueStrategy
{
	public RedPandaTestContainerStrategy()
	{
		var instance = new RedpandaBuilder("docker.redpanda.com/redpandadata/redpanda:latest")
			.WithName("sharpmush-test-redpanda")
			.WithLabel("reuse-hash", "sharpmush-redpanda-v1")
			.WithPortBinding(9092, true) // Use dynamic port to avoid conflicts
			// Configure 6MB message size limit for production compatibility
			.WithCommand("--set", "kafka_batch_max_bytes=6291456") // 6MB
			.WithReuse(true)
			.Build();
		
		instance.StartAsync().GetAwaiter().GetResult();

		Port =  instance.GetMappedPublicPort(9092);
		Host = instance.Hostname;
	}
	
	public override string Host { get; }

	public override int Port { get; }
}
