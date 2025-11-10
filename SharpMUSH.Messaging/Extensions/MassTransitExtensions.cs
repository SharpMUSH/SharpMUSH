using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Messaging.Configuration;

namespace SharpMUSH.Messaging.Extensions;

/// <summary>
/// Extension methods for configuring MassTransit with RabbitMQ
/// </summary>
public static class MassTransitExtensions
{
	/// <summary>
	/// Adds MassTransit with RabbitMQ configuration for ConnectionServer
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
			x.UsingRabbitMq((context, cfg) =>
			{
				cfg.Host(options.Host, options.VirtualHost, h =>
				{
					h.Username(options.Username);
					h.Password(options.Password);
				});

				// Configure message retry
				cfg.UseMessageRetry(r => r.Interval(options.RetryCount, TimeSpan.FromSeconds(options.RetryDelaySeconds)));

				// Configure endpoints
				cfg.ConfigureEndpoints(context);
			});
		});

		return services;
	}

	/// <summary>
	/// Adds MassTransit with RabbitMQ configuration for MainProcess
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

			x.UsingRabbitMq((context, cfg) =>
			{
				cfg.Host(options.Host, options.VirtualHost, h =>
				{
					h.Username(options.Username);
					h.Password(options.Password);
				});

				// Configure message retry
				cfg.UseMessageRetry(r => r.Interval(options.RetryCount, TimeSpan.FromSeconds(options.RetryDelaySeconds)));

				// Configure endpoints
				cfg.ConfigureEndpoints(context);
			});
		});

		return services;
	}
}
