using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using SharpMUSH.Messaging.Abstractions;
using SharpMUSH.Messaging.Configuration;

namespace SharpMUSH.Messaging.Kafka;

/// <summary>
/// Kafka-based implementation of IMessageBus using Confluent.Kafka directly
/// </summary>
public class KafkaMessageBus : IMessageBus, IAsyncDisposable
{
	private readonly IProducer<string, string> _producer;
	private readonly ILogger<KafkaMessageBus> _logger;
	private readonly MessageQueueOptions _options;
	private readonly Dictionary<Type, string> _topicMappings;
	private readonly KafkaTopicManager _topicManager;

	public KafkaMessageBus(
		MessageQueueOptions options,
		ILogger<KafkaMessageBus> logger,
		KafkaTopicManager topicManager)
	{
		_options = options;
		_logger = logger;
		_topicManager = topicManager;

		// Configure Kafka producer
		var config = new ProducerConfig
		{
			BootstrapServers = $"{options.Host}:{options.Port}",
			EnableIdempotence = options.EnableIdempotence,
			CompressionType = ParseCompressionType(options.CompressionType),
			BatchSize = options.BatchSize,
			LingerMs = options.LingerMs,
			// When idempotence is enabled, acks must be set to All
			Acks = options.EnableIdempotence ? Acks.All : Acks.Leader,
			MaxInFlight = 5,
			MessageMaxBytes = options.MaxMessageBytes,
			// Performance optimizations
			SocketKeepaliveEnable = true,
			QueueBufferingMaxMessages = 100000,
			QueueBufferingMaxKbytes = 1048576,
			AllowAutoCreateTopics = true
		};

		_producer = new ProducerBuilder<string, string>(config)
			.SetErrorHandler((_, error) =>
			{
				_logger.LogError("Kafka producer error: {ErrorCode} - {ErrorReason}", error.Code, error.Reason);
			})
			.Build();

		// Map message types to topics
		_topicMappings = InitializeTopicMappings();
	}

	private Dictionary<Type, string> InitializeTopicMappings()
	{
		// Map message types to their corresponding Kafka topics
		// This needs to match the topic routing used by the system
		var mappings = new Dictionary<Type, string>();
		
		// These will be populated based on message types from SharpMUSH.Messages
		// For now, we'll use a convention-based approach where the topic name
		// is derived from the message type name
		
		return mappings;
	}

	private static CompressionType ParseCompressionType(string compressionType)
	{
		return compressionType.ToLowerInvariant() switch
		{
			"none" => CompressionType.None,
			"gzip" => CompressionType.Gzip,
			"snappy" => CompressionType.Snappy,
			"lz4" => CompressionType.Lz4,
			"zstd" => CompressionType.Zstd,
			_ => CompressionType.Lz4
		};
	}

	public async Task Publish<T>(T message, CancellationToken cancellationToken = default) where T : class
	{
		var topic = GetTopicForMessageType<T>();
		await SendToTopic(topic, message, cancellationToken);
	}

	public async Task Send<T>(T message, CancellationToken cancellationToken = default) where T : class
	{
		var topic = GetTopicForMessageType<T>();
		await SendToTopic(topic, message, cancellationToken);
	}

	private async Task SendToTopic<T>(string topic, T message, CancellationToken cancellationToken) where T : class
	{
		try
		{
			// Ensure topic exists before sending
			await _topicManager.EnsureTopicExistsAsync(topic, cancellationToken);

			var messageJson = JsonSerializer.Serialize(message);
			var kafkaMessage = new Message<string, string>
			{
				Key = Guid.NewGuid().ToString(), // Random key for load balancing
				Value = messageJson
			};

			var result = await _producer.ProduceAsync(topic, kafkaMessage, cancellationToken);

			_logger.LogTrace("Message published to topic {Topic}, partition {Partition}, offset {Offset}",
				topic, result.Partition.Value, result.Offset.Value);
		}
		catch (ProduceException<string, string> ex)
		{
			_logger.LogError(ex, "Failed to publish message to topic {Topic}: {ErrorCode} - {ErrorReason}",
				topic, ex.Error.Code, ex.Error.Reason);
			throw;
		}
	}

	private string GetTopicForMessageType<T>()
	{
		var messageType = typeof(T);
		
		// Check if we have an explicit mapping
		if (_topicMappings.TryGetValue(messageType, out var topic))
		{
			return topic;
		}

		// Use convention-based topic naming: convert "TelnetOutputMessage" to "telnet-output"
		var typeName = messageType.Name;
		if (typeName.EndsWith("Message"))
		{
			typeName = typeName[..^7]; // Remove "Message" suffix
		}

		// Convert PascalCase to kebab-case
		var kebabCase = string.Concat(
			typeName.Select((c, i) => i > 0 && char.IsUpper(c) ? "-" + c : c.ToString())
		).ToLowerInvariant();

		return kebabCase;
	}

	public async ValueTask DisposeAsync()
	{
		_producer?.Flush(TimeSpan.FromSeconds(10));
		_producer?.Dispose();
		await _topicManager.DisposeAsync();
	}
}
