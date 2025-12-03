namespace SharpMUSH.Messaging.Configuration;

/// <summary>
/// Message broker type
/// </summary>
public enum MessageBrokerType
{
	/// <summary>
	/// RabbitMQ message broker
	/// </summary>
	RabbitMQ,
	
	/// <summary>
	/// Kafka/RedPanda message broker (streaming-optimized)
	/// </summary>
	Kafka
}

/// <summary>
/// Configuration options for the message queue
/// </summary>
public class MessageQueueOptions
{
	/// <summary>
	/// Message broker type to use
	/// </summary>
	public MessageBrokerType BrokerType { get; set; } = MessageBrokerType.RabbitMQ;

	/// <summary>
	/// The message broker host address (RabbitMQ or Kafka/RedPanda)
	/// </summary>
	public string Host { get; set; } = "localhost";

	/// <summary>
	/// The message broker port (5672 for RabbitMQ, 9092 for Kafka/RedPanda)
	/// </summary>
	public int Port { get; set; } = 5672;

	/// <summary>
	/// The RabbitMQ username
	/// </summary>
	public string Username { get; set; } = "guest";

	/// <summary>
	/// The RabbitMQ password
	/// </summary>
	public string Password { get; set; } = "guest";

	/// <summary>
	/// The virtual host to use
	/// </summary>
	public string VirtualHost { get; set; } = "/";

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
	/// Whether to use durable queues (survive broker restart)
	/// </summary>
	public bool DurableQueues { get; set; } = true;

	/// <summary>
	/// Message TTL in hours (0 = no expiration)
	/// </summary>
	public int MessageTtlHours { get; set; } = 24;

	/// <summary>
	/// Enable RabbitMQ Streams for high-throughput telnet output
	/// Requires RabbitMQ 3.9+ with streams plugin enabled
	/// </summary>
	public bool UseStreams { get; set; } = false;

	/// <summary>
	/// Stream name for telnet output when UseStreams is enabled
	/// </summary>
	public string TelnetOutputStreamName { get; set; } = "telnet-output-stream";

	/// <summary>
	/// Maximum stream age in hours (for automatic cleanup)
	/// </summary>
	public int StreamMaxAgeHours { get; set; } = 24;

	/// <summary>
	/// Kafka/RedPanda: Topic name for telnet output messages
	/// </summary>
	public string TelnetOutputTopic { get; set; } = "telnet-output";

	/// <summary>
	/// Kafka/RedPanda: Consumer group ID
	/// </summary>
	public string ConsumerGroupId { get; set; } = "sharpmush-consumer-group";

	/// <summary>
	/// Kafka/RedPanda: Number of partitions for topics (affects parallelism)
	/// </summary>
	public short TopicPartitions { get; set; } = 3;

	/// <summary>
	/// Kafka/RedPanda: Replication factor for topics
	/// </summary>
	public short TopicReplicationFactor { get; set; } = 1;

	/// <summary>
	/// Kafka/RedPanda: Enable idempotent producer (ensures exactly-once semantics)
	/// </summary>
	public bool EnableIdempotence { get; set; } = true;

	/// <summary>
	/// Kafka/RedPanda: Compression type (none, gzip, snappy, lz4, zstd)
	/// </summary>
	public string CompressionType { get; set; } = "lz4";

	/// <summary>
	/// Kafka/RedPanda: Batch size in bytes
	/// </summary>
	public int BatchSize { get; set; } = 32768; // 32KB

	/// <summary>
	/// Kafka/RedPanda: Linger time in milliseconds (how long to wait for batching)
	/// </summary>
	public int LingerMs { get; set; } = 5;

	/// <summary>
	/// Kafka/RedPanda: Maximum message size in bytes (6MB for SharpMUSH production)
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
