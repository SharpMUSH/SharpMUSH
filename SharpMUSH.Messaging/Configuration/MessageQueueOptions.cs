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
	/// Number of retry attempts for failed messages
	/// </summary>
	public int RetryCount { get; set; } = 3;

	/// <summary>
	/// Consumer group ID
	/// </summary>
	public string ConsumerGroupId { get; set; } = "sharpmush-consumer-group";

	/// <summary>
	/// Enable idempotent producer (ensures exactly-once semantics)
	/// Note: Currently disabled for better performance (using acks=1 instead of acks=all)
	/// </summary>
	public bool EnableIdempotence { get; set; } = false;

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
	/// Optimized to 16ms for better throughput with acceptable latency (still well within 60fps)
	/// </summary>
	public int LingerMs { get; set; } = 16;

	/// <summary>
	/// Maximum number of in-flight requests per connection.
	/// Set to 1 to guarantee strict message ordering within a partition.
	/// Higher values (up to 5 with idempotence) can improve throughput but may reorder messages.
	/// Default: 1 for strict ordering guarantees.
	/// </summary>
	public int MaxInFlightRequests { get; set; } = 1;

	/// <summary>
	/// Number of worker threads for processing messages in parallel.
	/// With BytesSum distribution strategy, messages with the same partition key
	/// always go to the same worker, maintaining ordering while allowing parallelism.
	/// Default: Number of processor cores for optimal parallel processing.
	/// </summary>
	public int WorkerCount { get; set; } = Environment.ProcessorCount;

	/// <summary>
	/// Maximum message size in bytes (6MB for SharpMUSH production)
	/// </summary>
	public int MaxMessageBytes { get; set; } = 6 * 1024 * 1024; // 6MB

	/// <summary>
	/// Maximum number of messages to batch for consumer-side batching
	/// This solves the @dolist performance issue by processing multiple messages together
	/// </summary>
	public int BatchMaxSize { get; set; } = 100;

	/// <summary>
	/// Maximum time to wait for a full batch (in milliseconds)
	/// Combined with producer batching (8ms), provides ~16ms total latency (approaching 60fps)
	/// </summary>
	public TimeSpan BatchTimeLimit { get; set; } = TimeSpan.FromMilliseconds(8);
}
