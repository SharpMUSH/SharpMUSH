using System.Collections.Concurrent;
using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharpMUSH.Messaging.Abstractions;
using SharpMUSH.Messaging.Configuration;

namespace SharpMUSH.Messaging.Kafka;

/// <summary>
/// Background service that hosts Kafka consumers with batching support
/// </summary>
public class KafkaConsumerHost : BackgroundService
{
	private readonly IServiceProvider _serviceProvider;
	private readonly ILogger<KafkaConsumerHost> _logger;
	private readonly MessageQueueOptions _options;
	private readonly List<ConsumerRegistration> _consumerRegistrations = [];
	private readonly List<Task> _consumerTasks = [];

	public KafkaConsumerHost(
		IServiceProvider serviceProvider,
		ILogger<KafkaConsumerHost> logger,
		MessageQueueOptions options)
	{
		_serviceProvider = serviceProvider;
		_logger = logger;
		_options = options;
	}

	/// <summary>
	/// Registers a consumer for a specific message type
	/// </summary>
	public void RegisterConsumer<TMessage>(string topic, bool enableBatching = false) where TMessage : class
	{
		_consumerRegistrations.Add(new ConsumerRegistration
		{
			MessageType = typeof(TMessage),
			Topic = topic,
			EnableBatching = enableBatching
		});
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		_logger.LogInformation("Starting Kafka consumer host with {Count} consumer(s)", _consumerRegistrations.Count);

		foreach (var registration in _consumerRegistrations)
		{
			var consumerTask = StartConsumer(registration, stoppingToken);
			_consumerTasks.Add(consumerTask);
		}

		await Task.WhenAll(_consumerTasks);
	}

	private Task StartConsumer(ConsumerRegistration registration, CancellationToken stoppingToken)
	{
		return Task.Run(async () =>
		{
			var config = new ConsumerConfig
			{
				BootstrapServers = $"{_options.Host}:{_options.Port}",
				GroupId = _options.ConsumerGroupId,
				AutoOffsetReset = AutoOffsetReset.Latest,
				EnableAutoCommit = true,
				EnableAutoOffsetStore = false, // We'll store offsets manually after processing
				SessionTimeoutMs = 30000,
				MaxPollIntervalMs = 300000,
				FetchMaxBytes = _options.MaxMessageBytes,
				// Performance optimizations
				FetchMinBytes = 1,
				FetchWaitMaxMs = 100,
			};

			using var consumer = new ConsumerBuilder<Ignore, string>(config)
				.SetErrorHandler((_, error) =>
				{
					_logger.LogError("Kafka consumer error on topic {Topic}: {ErrorCode} - {ErrorReason}",
						registration.Topic, error.Code, error.Reason);
				})
				.Build();

			try
			{
				consumer.Subscribe(registration.Topic);
				_logger.LogInformation("Consumer subscribed to topic: {Topic}", registration.Topic);

				if (registration.EnableBatching)
				{
					await ConsumeBatchedMessages(consumer, registration, stoppingToken);
				}
				else
				{
					await ConsumeMessages(consumer, registration, stoppingToken);
				}
			}
			catch (OperationCanceledException)
			{
				_logger.LogInformation("Consumer for topic {Topic} is shutting down", registration.Topic);
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Fatal error in consumer for topic {Topic}", registration.Topic);
			}
			finally
			{
				consumer.Close();
			}
		}, stoppingToken);
	}

	private async Task ConsumeMessages(
		IConsumer<Ignore, string> consumer,
		ConsumerRegistration registration,
		CancellationToken stoppingToken)
	{
		while (!stoppingToken.IsCancellationRequested)
		{
			try
			{
				var consumeResult = consumer.Consume(stoppingToken);
				
				if (consumeResult?.Message?.Value == null)
				{
					continue;
				}

				await ProcessMessage(consumeResult.Message.Value, registration, stoppingToken);
				
				// Store the offset after successful processing
				consumer.StoreOffset(consumeResult);
			}
			catch (ConsumeException ex)
			{
				_logger.LogError(ex, "Error consuming message from topic {Topic}: {ErrorCode} - {ErrorReason}",
					registration.Topic, ex.Error.Code, ex.Error.Reason);
				
				// If topic doesn't exist, wait before retrying to avoid spam
				if (ex.Error.Code == ErrorCode.UnknownTopicOrPart)
				{
					await Task.Delay(5000, stoppingToken);
				}
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error processing message from topic {Topic}", registration.Topic);
			}
		}
	}

	private async Task ConsumeBatchedMessages(
		IConsumer<Ignore, string> consumer,
		ConsumerRegistration registration,
		CancellationToken stoppingToken)
	{
		var batch = new List<(string message, TopicPartitionOffset offset)>();
		var batchDeadline = DateTime.UtcNow.Add(_options.BatchTimeLimit);

		while (!stoppingToken.IsCancellationRequested)
		{
			try
			{
				// Try to consume with a short timeout to allow batching
				var consumeResult = consumer.Consume(TimeSpan.FromMilliseconds(10));

				if (consumeResult?.Message?.Value != null)
				{
					batch.Add((consumeResult.Message.Value, consumeResult.TopicPartitionOffset));
				}

				// Process batch if we've reached the size limit or time limit
				var shouldProcessBatch = 
					batch.Count >= _options.BatchMaxSize ||
					(batch.Count > 0 && DateTime.UtcNow >= batchDeadline);

				if (shouldProcessBatch)
				{
					await ProcessBatch(batch.Select(x => x.message).ToList(), registration, stoppingToken);

					// Store the last offset after successful batch processing
					if (batch.Count > 0)
					{
						var lastOffset = batch[^1].offset;
						consumer.StoreOffset(lastOffset);
					}

					batch.Clear();
					batchDeadline = DateTime.UtcNow.Add(_options.BatchTimeLimit);
				}
			}
			catch (ConsumeException ex)
			{
				_logger.LogError(ex, "Error consuming batch from topic {Topic}: {ErrorCode} - {ErrorReason}",
					registration.Topic, ex.Error.Code, ex.Error.Reason);
				
				// Clear batch on error to avoid processing partial batches
				batch.Clear();
				batchDeadline = DateTime.UtcNow.Add(_options.BatchTimeLimit);
				
				// If topic doesn't exist, wait before retrying to avoid spam
				if (ex.Error.Code == ErrorCode.UnknownTopicOrPart)
				{
					await Task.Delay(5000, stoppingToken);
				}
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error processing batch from topic {Topic}", registration.Topic);
				
				// Clear batch on error
				batch.Clear();
				batchDeadline = DateTime.UtcNow.Add(_options.BatchTimeLimit);
			}
		}
	}

	private async Task ProcessMessage(string messageJson, ConsumerRegistration registration, CancellationToken cancellationToken)
	{
		using var scope = _serviceProvider.CreateScope();
		
		// Deserialize the message
		var message = JsonSerializer.Deserialize(messageJson, registration.MessageType);
		if (message == null)
		{
			_logger.LogWarning("Failed to deserialize message of type {Type}", registration.MessageType.Name);
			return;
		}

		// Get the consumer and invoke it
		var consumerType = typeof(IMessageConsumer<>).MakeGenericType(registration.MessageType);
		var consumer = scope.ServiceProvider.GetService(consumerType);

		if (consumer == null)
		{
			_logger.LogWarning("No consumer registered for message type {Type}", registration.MessageType.Name);
			return;
		}

		var handleMethod = consumerType.GetMethod("HandleAsync");
		if (handleMethod != null)
		{
			var task = (Task?)handleMethod.Invoke(consumer, [message, cancellationToken]);
			if (task != null)
			{
				await task;
			}
		}
	}

	private async Task ProcessBatch(List<string> messageJsonList, ConsumerRegistration registration, CancellationToken cancellationToken)
	{
		using var scope = _serviceProvider.CreateScope();

		// Deserialize all messages in the batch
		var messages = messageJsonList
			.Select(json => JsonSerializer.Deserialize(json, registration.MessageType))
			.Where(m => m != null)
			.ToList();

		if (messages.Count == 0)
		{
			return;
		}

		// Try to get a batch consumer first
		var batchConsumerType = typeof(IBatchMessageConsumer<>).MakeGenericType(registration.MessageType);
		var batchConsumer = scope.ServiceProvider.GetService(batchConsumerType);

		if (batchConsumer != null)
		{
			// Use batch consumer
			var handleBatchMethod = batchConsumerType.GetMethod("HandleBatchAsync");
			if (handleBatchMethod != null)
			{
				var task = (Task?)handleBatchMethod.Invoke(batchConsumer, [messages, cancellationToken]);
				if (task != null)
				{
					await task;
				}
			}
		}
		else
		{
			// Fall back to individual message consumer
			var consumerType = typeof(IMessageConsumer<>).MakeGenericType(registration.MessageType);
			var consumer = scope.ServiceProvider.GetService(consumerType);

			if (consumer == null)
			{
				_logger.LogWarning("No consumer registered for message type {Type}", registration.MessageType.Name);
				return;
			}

			var handleMethod = consumerType.GetMethod("HandleAsync");
			if (handleMethod != null)
			{
				foreach (var message in messages)
				{
					var task = (Task?)handleMethod.Invoke(consumer, [message, cancellationToken]);
					if (task != null)
					{
						await task;
					}
				}
			}
		}
	}

	private class ConsumerRegistration
	{
		public required Type MessageType { get; init; }
		public required string Topic { get; init; }
		public bool EnableBatching { get; init; }
	}
}
