using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.Extensions.Logging;
using SharpMUSH.Messaging.Configuration;

namespace SharpMUSH.Messaging.Kafka;

/// <summary>
/// Manages Kafka topic creation and configuration
/// </summary>
public class KafkaTopicManager : IAsyncDisposable
{
	private readonly IAdminClient _adminClient;
	private readonly ILogger<KafkaTopicManager> _logger;
	private readonly MessageQueueOptions _options;
	private readonly HashSet<string> _createdTopics = [];
	private readonly SemaphoreSlim _topicCreationLock = new(1, 1);

	public KafkaTopicManager(
		MessageQueueOptions options,
		ILogger<KafkaTopicManager> logger)
	{
		_options = options;
		_logger = logger;

		var config = new AdminClientConfig
		{
			BootstrapServers = $"{options.Host}:{options.Port}"
		};

		_adminClient = new AdminClientBuilder(config).Build();
	}

	/// <summary>
	/// Ensures a topic exists, creating it if necessary
	/// </summary>
	public async Task EnsureTopicExistsAsync(string topicName, CancellationToken cancellationToken = default)
	{
		// Check if we've already created/verified this topic
		if (_createdTopics.Contains(topicName))
		{
			return;
		}

		await _topicCreationLock.WaitAsync(cancellationToken);
		try
		{
			// Double-check after acquiring lock
			if (_createdTopics.Contains(topicName))
			{
				return;
			}

			// Check if topic exists
			var metadata = _adminClient.GetMetadata(TimeSpan.FromSeconds(5));
			var topicExists = metadata.Topics.Any(t => t.Topic == topicName);

			if (topicExists)
			{
				_logger.LogDebug("Topic {TopicName} already exists", topicName);
				_createdTopics.Add(topicName);
				return;
			}

			// Create the topic
			await CreateTopicAsync(topicName, cancellationToken);
			_createdTopics.Add(topicName);
		}
		finally
		{
			_topicCreationLock.Release();
		}
	}

	/// <summary>
	/// Creates a new topic with configured partitions and replication factor
	/// </summary>
	private async Task CreateTopicAsync(string topicName, CancellationToken cancellationToken)
	{
		try
		{
			var topicSpec = new TopicSpecification
			{
				Name = topicName,
				NumPartitions = _options.TopicPartitions,
				ReplicationFactor = _options.TopicReplicationFactor,
				Configs = new Dictionary<string, string>
				{
					{ "max.message.bytes", _options.MaxMessageBytes.ToString() },
					{ "compression.type", _options.CompressionType }
				}
			};

			_logger.LogInformation(
				"Creating topic {TopicName} with {Partitions} partition(s) and replication factor {ReplicationFactor}",
				topicName,
				_options.TopicPartitions,
				_options.TopicReplicationFactor);

			await _adminClient.CreateTopicsAsync([topicSpec]);

			_logger.LogInformation("Successfully created topic {TopicName}", topicName);
		}
		catch (CreateTopicsException ex)
		{
			// Check if error is because topic already exists
			var topicError = ex.Results.FirstOrDefault(r => r.Topic == topicName);
			if (topicError?.Error.Code == ErrorCode.TopicAlreadyExists)
			{
				_logger.LogDebug("Topic {TopicName} was created by another process", topicName);
			}
			else
			{
				_logger.LogError(ex, "Failed to create topic {TopicName}: {ErrorCode} - {ErrorReason}",
					topicName, topicError?.Error.Code, topicError?.Error.Reason);
				throw;
			}
		}
	}

	/// <summary>
	/// Ensures multiple topics exist, creating them if necessary
	/// </summary>
	public async Task EnsureTopicsExistAsync(IEnumerable<string> topicNames, CancellationToken cancellationToken = default)
	{
		foreach (var topicName in topicNames)
		{
			await EnsureTopicExistsAsync(topicName, cancellationToken);
		}
	}

	/// <summary>
	/// Deletes a topic (use with caution in production!)
	/// </summary>
	public async Task DeleteTopicAsync(string topicName, CancellationToken cancellationToken = default)
	{
		try
		{
			_logger.LogWarning("Deleting topic {TopicName}", topicName);
			await _adminClient.DeleteTopicsAsync([topicName]);
			_createdTopics.Remove(topicName);
			_logger.LogInformation("Successfully deleted topic {TopicName}", topicName);
		}
		catch (DeleteTopicsException ex)
		{
			var topicError = ex.Results.FirstOrDefault(r => r.Topic == topicName);
			_logger.LogError(ex, "Failed to delete topic {TopicName}: {ErrorCode} - {ErrorReason}",
				topicName, topicError?.Error.Code, topicError?.Error.Reason);
			throw;
		}
	}

	public async ValueTask DisposeAsync()
	{
		_adminClient?.Dispose();
		_topicCreationLock?.Dispose();
		await ValueTask.CompletedTask;
	}
}
