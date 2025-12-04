using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Messaging.Configuration;

namespace SharpMUSH.Messaging.Extensions;

/// <summary>
/// Extension methods for configuring MassTransit with Kafka/RedPanda
/// </summary>
public static class MassTransitExtensions
{
	/// <summary>
	/// Adds MassTransit with Kafka/RedPanda configuration for ConnectionServer
	/// </summary>
	public static IServiceCollection AddConnectionServerMessaging(
		this IServiceCollection services,
		Action<MessageQueueOptions> configureOptions,
		Action<IBusRegistrationConfigurator> configureConsumers)
	{
		var options = new MessageQueueOptions();
		configureOptions(options);

		services.AddSingleton(options);

		services.AddMassTransit(x =>
		{
			// Register consumers
			configureConsumers(x);

			// Configure Kafka/RedPanda (streaming-optimized)
			x.UsingInMemory((context, cfg) =>
			{
				cfg.ConfigureEndpoints(context);
			});

			x.AddRider(rider =>
			{
				rider.UsingKafka((context, k) =>
				{
					// Host configuration
					k.Host($"{options.Host}:{options.Port}");

					// Configure topic endpoint for telnet output messages with batching
					k.TopicEndpoint<Confluent.Kafka.Ignore, string>(options.TelnetOutputTopic, options.ConsumerGroupId, e =>
					{
						e.AutoOffsetReset = Confluent.Kafka.AutoOffsetReset.Latest;
						
						// Configure prefetch for performance optimization
						// This solves the @dolist performance issue by prefetching multiple
						// sequential messages before processing them
						e.PrefetchCount = options.BatchMaxSize;
						
						e.ConfigureConsumers(context);
					});
				});
			});
		});

		return services;
	}

	/// <summary>
	/// Adds MassTransit with Kafka/RedPanda configuration for MainProcess
	/// </summary>
	public static IServiceCollection AddMainProcessMessaging(
		this IServiceCollection services,
		Action<MessageQueueOptions> configureOptions,
		Action<IBusRegistrationConfigurator> configureConsumers)
	{
		var options = new MessageQueueOptions();
		configureOptions(options);

		services.AddSingleton(options);

		services.AddMassTransit(x =>
		{
			// Register consumers (Server consumes input messages from ConnectionServer)
			configureConsumers(x);

			// Configure Kafka/RedPanda (streaming-optimized)
			x.UsingInMemory((context, cfg) =>
			{
				cfg.ConfigureEndpoints(context);
			});

			x.AddRider(rider =>
			{
				rider.UsingKafka((context, k) =>
				{
					// Host configuration
					k.Host($"{options.Host}:{options.Port}");

					// Server does NOT consume from telnet-output topic
					// It produces TO telnet-output topic for ConnectionServer to consume
					// It consumes from other topics for input messages (configured via consumers)
				});
			});
		});

		return services;
	}
}
