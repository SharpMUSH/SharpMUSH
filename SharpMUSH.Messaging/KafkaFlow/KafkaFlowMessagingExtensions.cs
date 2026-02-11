using KafkaFlow;
using KafkaFlow.Configuration;
using KafkaFlow.Serializer;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Messaging.Abstractions;
using SharpMUSH.Messaging.Configuration;
using ConfluentCompressionType = Confluent.Kafka.CompressionType;
using KFAcks = KafkaFlow.Acks;
using KFAutoOffsetReset = KafkaFlow.AutoOffsetReset;

namespace SharpMUSH.Messaging.KafkaFlow;

/// <summary>
/// Extension methods for configuring KafkaFlow messaging
/// </summary>
public static class KafkaFlowMessagingExtensions
{
	/// <summary>
	/// Adds KafkaFlow messaging for ConnectionServer
	/// </summary>
	public static IServiceCollection AddConnectionServerMessaging(
		this IServiceCollection services,
		Action<MessageQueueOptions> configureOptions,
		Action<IKafkaFlowConsumerConfigurator> configureConsumers)
	{
		var options = new MessageQueueOptions();
		configureOptions(options);

		services.AddSingleton(options);

		// Configure KafkaFlow with type-based producer
		services.AddKafka(kafka => kafka
			.AddCluster(cluster =>
			{
				cluster
					.WithBrokers([$"{options.Host}:{options.Port}"])
					// Use type-based producer following KafkaFlow best practices
					.AddProducer<SharpMushProducer>(
						producer => producer
							.WithCompression(ParseCompressionType(options.CompressionType), null)
							.WithAcks(options.EnableIdempotence ? KFAcks.All : KFAcks.Leader)
							.WithLingerMs(options.LingerMs) // Producer-level batching
							.AddMiddlewares(m => m.AddSerializer<JsonCoreSerializer>())
					);

				// Configure consumers using the configurator
				var configurator = new KafkaFlowConsumerConfigurator(cluster, services, options);
				configureConsumers(configurator);
			})
		);

		// Register IMessageBus implementation
		services.AddSingleton<IMessageBus, KafkaFlowMessageBus>();

		return services;
	}

	/// <summary>
	/// Adds KafkaFlow messaging for MainProcess
	/// </summary>
	public static IServiceCollection AddMainProcessMessaging(
		this IServiceCollection services,
		Action<MessageQueueOptions> configureOptions,
		Action<IKafkaFlowConsumerConfigurator> configureConsumers)
	{
		var options = new MessageQueueOptions();
		configureOptions(options);

		services.AddSingleton(options);

		// Configure KafkaFlow with type-based producer
		services.AddKafka(kafka => kafka
			.AddCluster(cluster =>
			{
				cluster
					.WithBrokers([$"{options.Host}:{options.Port}"])
					// Use type-based producer following KafkaFlow best practices
					.AddProducer<SharpMushProducer>(
						producer => producer
							.WithCompression(ParseCompressionType(options.CompressionType), null)
							.WithAcks(options.EnableIdempotence ? KFAcks.All : KFAcks.Leader)
							.WithLingerMs(options.LingerMs) // Producer-level batching
							.AddMiddlewares(m => m.AddSerializer<JsonCoreSerializer>())
					);

				// Configure consumers using the configurator
				var configurator = new KafkaFlowConsumerConfigurator(cluster, services, options);
				configureConsumers(configurator);
			})
		);

		// Register IMessageBus implementation
		services.AddSingleton<IMessageBus, KafkaFlowMessageBus>();

		return services;
	}

	private static ConfluentCompressionType ParseCompressionType(string compressionType)
	{
		return compressionType.ToLowerInvariant() switch
		{
			"none" => ConfluentCompressionType.None,
			"gzip" => ConfluentCompressionType.Gzip,
			"snappy" => ConfluentCompressionType.Snappy,
			"lz4" => ConfluentCompressionType.Lz4,
			"zstd" => ConfluentCompressionType.Zstd,
			_ => ConfluentCompressionType.Lz4
		};
	}
}

/// <summary>
/// Interface for configuring KafkaFlow consumers
/// </summary>
public interface IKafkaFlowConsumerConfigurator
{
	/// <summary>
	/// Registers a consumer for a specific message type
	/// </summary>
	void AddConsumer<TConsumer>() where TConsumer : class;
}

/// <summary>
/// Implementation of KafkaFlow consumer configurator
/// </summary>
public class KafkaFlowConsumerConfigurator : IKafkaFlowConsumerConfigurator
{
	private readonly IClusterConfigurationBuilder _clusterBuilder;
	private readonly IServiceCollection _services;
	private readonly MessageQueueOptions _options;

	public KafkaFlowConsumerConfigurator(
		IClusterConfigurationBuilder clusterBuilder,
		IServiceCollection services,
		MessageQueueOptions options)
	{
		_clusterBuilder = clusterBuilder;
		_services = services;
		_options = options;
	}

	public void AddConsumer<TConsumer>() where TConsumer : class
	{
		// Find the IMessageConsumer<T> interface
		var consumerInterface = typeof(TConsumer).GetInterfaces()
			.FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IMessageConsumer<>));

		if (consumerInterface == null)
		{
			throw new InvalidOperationException($"{typeof(TConsumer).Name} does not implement IMessageConsumer<T>");
		}

		var messageType = consumerInterface.GetGenericArguments()[0];
		var topic = GetTopicFromMessageType(messageType);

		// Register the consumer in DI
		var consumerServiceType = typeof(IMessageConsumer<>).MakeGenericType(messageType);
		_services.AddTransient(consumerServiceType, typeof(TConsumer));

		// Register the adapter
		var adapterType = typeof(MessageConsumerAdapter<>).MakeGenericType(messageType);
		_services.AddTransient(adapterType);

		// Add KafkaFlow consumer
		_clusterBuilder.AddConsumer(consumer => consumer
			.Topic(topic)
			.WithGroupId(_options.ConsumerGroupId)
			.WithBufferSize(_options.BatchMaxSize)
			.WithWorkersCount(1) // Parallel processing
			.WithAutoOffsetReset(KFAutoOffsetReset.Latest)
			.AddMiddlewares(middlewares => middlewares
				.AddDeserializer<JsonCoreDeserializer>()
				.AddTypedHandlers(h =>
				{
					// Register the adapter as a handler
					try
					{
						var addHandlerMethod = h.GetType().GetMethod("AddHandler", []);
						if (addHandlerMethod == null)
						{
							throw new InvalidOperationException(
								$"Could not find AddHandler method on handler configurator for message type {messageType.Name}");
						}

						var genericMethod = addHandlerMethod.MakeGenericMethod(adapterType);
						genericMethod.Invoke(h, null);
					}
					catch (Exception ex)
					{
						throw new InvalidOperationException(
							$"Failed to register consumer adapter for message type {messageType.Name}. " +
							$"Consumer type: {typeof(TConsumer).Name}, Adapter type: {adapterType.Name}",
							ex);
					}
				})
			)
		);
	}

	private static string GetTopicFromMessageType(Type messageType)
	{
		// Convert "TelnetOutputMessage" to "telnet-output"
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
}
