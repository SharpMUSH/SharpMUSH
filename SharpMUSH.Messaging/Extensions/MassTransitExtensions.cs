using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Messaging.Configuration;

namespace SharpMUSH.Messaging.Extensions;

/// <summary>
/// Extension methods for configuring MassTransit with RabbitMQ or Kafka/RedPanda
/// </summary>
public static class MassTransitExtensions
{
	/// <summary>
	/// Adds MassTransit with RabbitMQ or Kafka/RedPanda configuration for ConnectionServer
	/// </summary>
	public static IServiceCollection AddConnectionServerMessaging(
		this IServiceCollection services,
		Action<MessageQueueOptions> configureOptions)
	{
		var options = new MessageQueueOptions();
		configureOptions(options);

		services.AddSingleton(options);

		services.AddMassTransit(x =>
		{
			if (options.BrokerType == MessageBrokerType.Kafka)
			{
				// Configure Kafka/RedPanda (streaming-optimized)
				x.UsingInMemory((context, cfg) =>
				{
					cfg.ConfigureEndpoints(context);
				});

				x.AddRider(rider =>
				{
					rider.UsingKafka((context, k) =>
					{
						// Host configuration with 6MB message size support
						k.Host($"{options.Host}:{options.Port}");

						k.ClientId = "sharpmush-connection-server";
						k.SecurityProtocol = Confluent.Kafka.SecurityProtocol.Plaintext;
					});
				});
			}
			else
			{
				// Configure RabbitMQ (traditional)
				x.UsingRabbitMq((context, cfg) =>
				{
					cfg.Host(options.Host, options.VirtualHost, h =>
					{
						h.Username(options.Username);
						h.Password(options.Password);
						// Optimize for high throughput
						h.PublisherConfirmation = false; // Disable confirms for telnet output (fire-and-forget)
						h.RequestedChannelMax(2047); // Increase channel limit
					});

					// Increase prefetch for better consumer throughput
					cfg.PrefetchCount = 100;

					// Configure message retry
					cfg.UseMessageRetry(r => r.Interval(options.RetryCount, TimeSpan.FromSeconds(options.RetryDelaySeconds)));

					// Configure endpoints
					cfg.ConfigureEndpoints(context);
				});
			}
		});

		return services;
	}

	/// <summary>
	/// Adds MassTransit with RabbitMQ or Kafka/RedPanda configuration for MainProcess
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
			// Register consumers
			configureConsumers(x);

			if (options.BrokerType == MessageBrokerType.Kafka)
			{
				// Configure Kafka/RedPanda (streaming-optimized)
				x.UsingInMemory((context, cfg) =>
				{
					cfg.ConfigureEndpoints(context);
				});

				x.AddRider(rider =>
				{
					rider.UsingKafka((context, k) =>
					{
						// Host configuration with 6MB message size support
						k.Host($"{options.Host}:{options.Port}");

						k.ClientId = "sharpmush-server";
						k.SecurityProtocol = Confluent.Kafka.SecurityProtocol.Plaintext;

						// Configure topic endpoints for consumers
						k.TopicEndpoint<Confluent.Kafka.Ignore, string>(options.TelnetOutputTopic, options.ConsumerGroupId, e =>
						{
							e.AutoOffsetReset = Confluent.Kafka.AutoOffsetReset.Latest;
							e.ConfigureConsumers(context);
						});
					});
				});
			}
			else
			{
				// Configure RabbitMQ (traditional)
				x.UsingRabbitMq((context, cfg) =>
				{
					cfg.Host(options.Host, options.VirtualHost, h =>
					{
						h.Username(options.Username);
						h.Password(options.Password);
						// Optimize for high throughput
						h.PublisherConfirmation = false; // Disable confirms for telnet output (fire-and-forget)
						h.RequestedChannelMax(2047); // Increase channel limit
					});

					// Increase prefetch for better consumer throughput
					cfg.PrefetchCount = 100;

					// Configure message retry
					cfg.UseMessageRetry(r => r.Interval(options.RetryCount, TimeSpan.FromSeconds(options.RetryDelaySeconds)));

					// Configure endpoints
					cfg.ConfigureEndpoints(context);
				});
			}
		});

		return services;
	}
}
