using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharpMUSH.Messaging.Abstractions;

namespace SharpMUSH.Messaging.NATS;

/// <summary>
/// Extension methods for registering NATS JetStream messaging in the DI container.
/// Mirrors <see cref="KafkaFlow.KafkaFlowMessagingExtensions"/> so that either
/// transport can be substituted by swapping a single registration call.
/// </summary>
public static class NatsMessagingExtensions
{
	/// <summary>
	/// Adds NATS JetStream messaging for the ConnectionServer role.
	/// Registers <see cref="NatsJetStreamMessageBus"/> as <see cref="IMessageBus"/>.
	/// </summary>
	public static IServiceCollection AddNatsConnectionServerMessaging(
		this IServiceCollection services,
		Action<NatsOptions> configureOptions)
	{
		return services.AddNatsMessaging(configureOptions);
	}

	/// <summary>
	/// Adds NATS JetStream messaging for the main Server (game engine) role.
	/// Registers <see cref="NatsJetStreamMessageBus"/> as <see cref="IMessageBus"/>.
	/// </summary>
	public static IServiceCollection AddNatsMainProcessMessaging(
		this IServiceCollection services,
		Action<NatsOptions> configureOptions)
	{
		return services.AddNatsMessaging(configureOptions);
	}

	private static IServiceCollection AddNatsMessaging(
		this IServiceCollection services,
		Action<NatsOptions> configureOptions)
	{
		var options = new NatsOptions();
		configureOptions(options);

		services.AddSingleton(options);

		services.AddSingleton<NatsJetStreamMessageBus>(sp =>
		{
			var logger = sp.GetRequiredService<ILogger<NatsJetStreamMessageBus>>();
			return NatsJetStreamMessageBus.CreateAsync(options, logger).GetAwaiter().GetResult();
		});

		services.AddSingleton<IMessageBus>(sp => sp.GetRequiredService<NatsJetStreamMessageBus>());

		return services;
	}
}
