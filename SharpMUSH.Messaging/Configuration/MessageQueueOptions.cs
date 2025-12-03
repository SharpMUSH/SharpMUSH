namespace SharpMUSH.Messaging.Configuration;

/// <summary>
/// Configuration options for Kafka/RedPanda message queue
/// </summary>
public class MessageQueueOptions
{
	/// <summary>
	/// The Kafka/RedPanda broker host address
	/// </summary>
	public string Host { get; set; } = "localhost";

	/// <summary>
	/// The Kafka/RedPanda broker port (default: 9092)
	/// </summary>
	public int Port { get; set; } = 9092;

	/// <summary>
	/// Timeout for request/response messages in seconds
	/// </summary>
	public int RequestTimeoutSeconds { get; set; } = 5;

	/// <summary>
	/// Number of retry attempts for failed messages
	/// </summary>
	public int RetryCount { get; set; } = 3;

	/// <summary>
	/// Delay between retry attempts in seconds
	/// </summary>
	public int RetryDelaySeconds { get; set; } = 5;

	/// <summary>
	/// Topic name for telnet output messages
	/// </summary>
	public string TelnetOutputTopic { get; set; } = "telnet-output";

	/// <summary>
	/// Consumer group ID
	/// </summary>
	public string ConsumerGroupId { get; set; } = "sharpmush-consumer-group";

	/// <summary>
	/// Number of partitions for topics (affects parallelism)
	/// </summary>
	public short TopicPartitions { get; set; } = 3;

	/// <summary>
	/// Replication factor for topics
	/// </summary>
	public short TopicReplicationFactor { get; set; } = 1;

	/// <summary>
	/// Enable idempotent producer (ensures exactly-once semantics)
	/// </summary>
	public bool EnableIdempotence { get; set; } = true;

	/// <summary>
	/// Compression type (none, gzip, snappy, lz4, zstd)
	/// </summary>
	public string CompressionType { get; set; } = "lz4";

	/// <summary>
	/// Batch size in bytes
	/// </summary>
	public int BatchSize { get; set; } = 32768; // 32KB

	/// <summary>
	/// Linger time in milliseconds (how long to wait for batching)
	/// </summary>
	public int LingerMs { get; set; } = 5;

	/// <summary>
	/// Maximum message size in bytes (6MB for SharpMUSH production)
	/// </summary>
	public int MaxMessageBytes { get; set; } = 6 * 1024 * 1024; // 6MB

	/// <summary>
	/// Gets Kafka producer configuration
	/// </summary>
	public Dictionary<string, string> GetKafkaProducerConfig()
	{
		return new Dictionary<string, string>
		{
			{ "enable.idempotence", EnableIdempotence.ToString().ToLower() },
			{ "compression.type", CompressionType },
			{ "batch.size", BatchSize.ToString() },
			{ "linger.ms", LingerMs.ToString() },
			{ "acks", "1" }, // Leader acknowledgment only (faster)
			{ "max.request.size", MaxMessageBytes.ToString() },
			{ "message.max.bytes", MaxMessageBytes.ToString() }
		};
	}

	/// <summary>
	/// Gets Kafka consumer configuration
	/// </summary>
	public Dictionary<string, string> GetKafkaConsumerConfig()
	{
		return new Dictionary<string, string>
		{
			{ "fetch.max.bytes", MaxMessageBytes.ToString() },
			{ "max.partition.fetch.bytes", MaxMessageBytes.ToString() },
			{ "message.max.bytes", MaxMessageBytes.ToString() }
		};
	}
}
