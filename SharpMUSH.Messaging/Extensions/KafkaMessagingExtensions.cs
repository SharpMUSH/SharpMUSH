using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharpMUSH.Messaging.Abstractions;
using SharpMUSH.Messaging.Configuration;
using SharpMUSH.Messaging.Kafka;

namespace SharpMUSH.Messaging.Extensions;

/// <summary>
/// Extension methods for configuring Kafka messaging with Confluent.Kafka
/// </summary>
public static class KafkaMessagingExtensions
{
	/// <summary>
	/// Adds Kafka messaging for ConnectionServer
	/// </summary>
	public static IServiceCollection AddConnectionServerMessaging(
	this IServiceCollection services,
	Action<MessageQueueOptions> configureOptions,
	Action<IKafkaConsumerConfigurator> configureConsumers)
	{
		var options = new MessageQueueOptions();
		configureOptions(options);

		services.AddSingleton(options);
		services.AddSingleton<KafkaTopicManager>();
		services.AddSingleton<IMessageBus, KafkaMessageBus>();

		// Store consumer registrations to be applied later
		var consumerRegistrations = new List<(Type MessageType, Type ConsumerType, string Topic, bool EnableBatching)>();
		var configurator = new DeferredKafkaConsumerConfigurator(services, consumerRegistrations);
		configureConsumers(configurator);

		// Add hosted service that will create KafkaConsumerHost with proper service provider
		services.AddSingleton<IHostedService>(sp =>
		{
			var consumerHost = new KafkaConsumerHost(
				sp,
				sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<KafkaConsumerHost>>(),
				sp.GetRequiredService<MessageQueueOptions>(),
				sp.GetRequiredService<KafkaTopicManager>());

			// Register all consumers with the host
			foreach (var (messageType, _, topic, enableBatching) in consumerRegistrations)
			{
				consumerHost.RegisterConsumer(messageType, topic, enableBatching);
			}

			return consumerHost;
		});

		return services;
	}

	/// <summary>
	/// Adds Kafka messaging for MainProcess
	/// </summary>
	public static IServiceCollection AddMainProcessMessaging(
	this IServiceCollection services,
	Action<MessageQueueOptions> configureOptions,
	Action<IKafkaConsumerConfigurator> configureConsumers)
	{
		var options = new MessageQueueOptions();
		configureOptions(options);

		services.AddSingleton(options);
		services.AddSingleton<KafkaTopicManager>();
		services.AddSingleton<IMessageBus, KafkaMessageBus>();

		// Store consumer registrations to be applied later
		var consumerRegistrations = new List<(Type MessageType, Type ConsumerType, string Topic, bool EnableBatching)>();
		var configurator = new DeferredKafkaConsumerConfigurator(services, consumerRegistrations);
		configureConsumers(configurator);

		// Add hosted service that will create KafkaConsumerHost with proper service provider
		services.AddSingleton<IHostedService>(sp =>
		{
			var consumerHost = new KafkaConsumerHost(
				sp,
				sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<KafkaConsumerHost>>(),
				sp.GetRequiredService<MessageQueueOptions>(),
				sp.GetRequiredService<KafkaTopicManager>());

			// Register all consumers with the host
			foreach (var (messageType, _, topic, enableBatching) in consumerRegistrations)
			{
				consumerHost.RegisterConsumer(messageType, topic, enableBatching);
			}

			return consumerHost;
		});

		return services;
	}
}

/// <summary>
/// Interface for configuring Kafka consumers
/// </summary>
public interface IKafkaConsumerConfigurator
{
	/// <summary>
	/// Registers a consumer for a specific message type and Kafka topic
	/// </summary>
	void AddConsumer<TMessage, TConsumer>(string topic, bool enableBatching = false)
	where TMessage : class
	where TConsumer : class, IMessageConsumer<TMessage>;

	/// <summary>
	/// Registers a batch consumer for a specific message type and Kafka topic
	/// </summary>
	void AddBatchConsumer<TMessage, TConsumer>(string topic)
	where TMessage : class
	where TConsumer : class, IBatchMessageConsumer<TMessage>;
}
