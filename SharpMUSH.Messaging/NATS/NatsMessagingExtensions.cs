using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharpMUSH.Messaging.Abstractions;
using System.Reflection;

namespace SharpMUSH.Messaging.NATS;

/// <summary>
/// Extension methods for registering NATS JetStream messaging in the DI container.
/// </summary>
public static class NatsMessagingExtensions
{
	/// <summary>
	/// Adds NATS JetStream messaging for the ConnectionServer role (publisher only, no consumers).
	/// Use the overload with <paramref name="configureConsumers"/> to also register consumers.
	/// </summary>
	public static IServiceCollection AddNatsConnectionServerMessaging(
		this IServiceCollection services,
		Action<NatsOptions> configureOptions)
		=> services.AddNatsMessagingCore(configureOptions);

	/// <summary>
	/// Adds NATS JetStream messaging for the ConnectionServer role with consumers.
	/// </summary>
	public static IServiceCollection AddNatsConnectionServerMessaging(
		this IServiceCollection services,
		Action<NatsOptions> configureOptions,
		Action<INatsConsumerConfigurator> configureConsumers)
		=> services.AddNatsMessagingCore(configureOptions, configureConsumers, "connectionserver");

	/// <summary>
	/// Adds NATS JetStream messaging for the main Server (game engine) role (publisher only, no consumers).
	/// Use the overload with <paramref name="configureConsumers"/> to also register consumers.
	/// </summary>
	public static IServiceCollection AddNatsMainProcessMessaging(
		this IServiceCollection services,
		Action<NatsOptions> configureOptions)
		=> services.AddNatsMessagingCore(configureOptions);

	/// <summary>
	/// Adds NATS JetStream messaging for the main Server (game engine) role with consumers.
	/// </summary>
	public static IServiceCollection AddNatsMainProcessMessaging(
		this IServiceCollection services,
		Action<NatsOptions> configureOptions,
		Action<INatsConsumerConfigurator> configureConsumers)
		=> services.AddNatsMessagingCore(configureOptions, configureConsumers, "mainprocess");

	private static IServiceCollection AddNatsMessagingCore(
		this IServiceCollection services,
		Action<NatsOptions> configureOptions,
		Action<INatsConsumerConfigurator>? configureConsumers = null,
		string groupPrefix = "")
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

		if (configureConsumers is not null)
		{
			var registry = new NatsConsumerRegistry();
			var configurator = new NatsConsumerConfigurator(registry, services, options, groupPrefix);
			configureConsumers(configurator);

			services.AddSingleton(registry);
			services.AddHostedService<NatsJetStreamConsumerService>();
		}

		return services;
	}
}

/// <summary>
/// Fluent builder that collects NATS consumer registrations during DI setup.
/// </summary>
public interface INatsConsumerConfigurator
{
	/// <summary>
	/// Registers <typeparamref name="TConsumer"/> as the handler for its
	/// <c>IMessageConsumer&lt;TMessage&gt;</c> interface and creates a durable
	/// JetStream consumer for the corresponding NATS subject.
	/// </summary>
	void AddConsumer<TConsumer>() where TConsumer : class;
}

/// <summary>
/// Default implementation of <see cref="INatsConsumerConfigurator"/>.
/// </summary>
public sealed class NatsConsumerConfigurator : INatsConsumerConfigurator
{
	private readonly NatsConsumerRegistry _registry;
	private readonly IServiceCollection _services;
	private readonly NatsOptions _options;
	private readonly string _groupPrefix;

	public NatsConsumerConfigurator(
		NatsConsumerRegistry registry,
		IServiceCollection services,
		NatsOptions options,
		string groupPrefix)
	{
		_registry = registry;
		_services = services;
		_options = options;
		_groupPrefix = groupPrefix;
	}

	public void AddConsumer<TConsumer>() where TConsumer : class
	{
		var consumerInterface = typeof(TConsumer).GetInterfaces()
			.FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IMessageConsumer<>))
			?? throw new InvalidOperationException(
				$"{typeof(TConsumer).Name} does not implement IMessageConsumer<T>.");

		var messageType = consumerInterface.GetGenericArguments()[0];
		var subject = GetSubjectForMessageType(messageType, _options.SubjectPrefix);
		var durableName = GetDurableName(messageType);

		_services.AddTransient(consumerInterface, typeof(TConsumer));

		// Build a handler delegate that resolves the consumer from the DI scope and invokes it.
		// The MethodInfo is closed once at registration time; calling the compiled delegate on every
		// message avoids repeated MethodInfo.Invoke overhead.
		var invokeHelper = typeof(NatsConsumerConfigurator)
			.GetMethod(nameof(InvokeConsumerHelper), BindingFlags.Static | BindingFlags.NonPublic)!
			.MakeGenericMethod(messageType);

		var compiled = (Func<IServiceProvider, object, CancellationToken, Task>)
			Delegate.CreateDelegate(typeof(Func<IServiceProvider, object, CancellationToken, Task>), invokeHelper);

		_registry.Registrations.Add(new NatsConsumerRegistration(messageType, subject, durableName, compiled));
	}

	private static Task InvokeConsumerHelper<TMessage>(IServiceProvider sp, object msg, CancellationToken ct)
		where TMessage : class
	{
		var consumer = sp.GetRequiredService<IMessageConsumer<TMessage>>();
		return consumer.HandleAsync((TMessage)msg, ct);
	}

	private string GetDurableName(Type messageType)
	{
		var prefix = string.IsNullOrEmpty(_groupPrefix) ? "consumer" : _groupPrefix;
		return $"{prefix}-{GetKebabTypeName(messageType)}";
	}

	internal static string GetSubjectForMessageType(Type messageType, string subjectPrefix)
	{
		var typeName = messageType.Name;
		if (typeName.EndsWith("Message", StringComparison.Ordinal))
			typeName = typeName[..^7];

		var kebabCase = string.Concat(
			typeName.Select((c, i) => i > 0 && char.IsUpper(c) ? "-" + c : c.ToString())
		).ToLowerInvariant();

		return $"{subjectPrefix}.{kebabCase}";
	}

	private static string GetKebabTypeName(Type messageType)
	{
		var typeName = messageType.Name;
		if (typeName.EndsWith("Message", StringComparison.Ordinal))
			typeName = typeName[..^7];

		return string.Concat(
			typeName.Select((c, i) => i > 0 && char.IsUpper(c) ? "-" + c : c.ToString())
		).ToLowerInvariant();
	}
}
