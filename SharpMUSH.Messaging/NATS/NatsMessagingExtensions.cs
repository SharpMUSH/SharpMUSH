using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharpMUSH.Messaging.Abstractions;

namespace SharpMUSH.Messaging.NATS;

/// <summary>
/// Extension methods for registering NATS JetStream messaging in the DI container.
/// </summary>
public static class NatsMessagingExtensions
{
	/// <summary>
	/// Adds NATS JetStream messaging for the ConnectionServer role (publisher only, no consumers).
	/// The ConnectionServer publishes to stream <c>SHARPMUSH-CS</c> with subject prefix
	/// <c>sharpmush.cs</c>.
	/// </summary>
	public static IServiceCollection AddNatsConnectionServerMessaging(
		this IServiceCollection services,
		Action<NatsOptions> configureOptions)
	{
		var options = new NatsOptions
		{
			StreamName = "SHARPMUSH-CS",
			SubjectPrefix = "sharpmush.cs",
		};
		configureOptions(options);
		return services.AddNatsMessagingCore(options);
	}

	/// <summary>
	/// Adds NATS JetStream messaging for the ConnectionServer role with consumers.
	/// <list type="bullet">
	///   <item>Publishes to stream <c>SHARPMUSH-CS</c> (subject prefix <c>sharpmush.cs</c>)</item>
	///   <item>Consumes from stream <c>SHARPMUSH-MS</c> (subject prefix <c>sharpmush.ms</c>)</item>
	/// </list>
	/// </summary>
	public static IServiceCollection AddNatsConnectionServerMessaging(
		this IServiceCollection services,
		Action<NatsOptions> configureOptions,
		Action<INatsConsumerConfigurator> configureConsumers)
	{
		var options = new NatsOptions
		{
			StreamName = "SHARPMUSH-CS",
			SubjectPrefix = "sharpmush.cs",
			ConsumeStreamName = "SHARPMUSH-MS",
			ConsumeSubjectPrefix = "sharpmush.ms",
		};
		configureOptions(options);
		return services.AddNatsMessagingCore(options, configureConsumers, "connectionserver");
	}

	/// <summary>
	/// Adds NATS JetStream messaging for the main Server (game engine) role (publisher only, no consumers).
	/// The Server publishes to stream <c>SHARPMUSH-MS</c> with subject prefix <c>sharpmush.ms</c>.
	/// </summary>
	public static IServiceCollection AddNatsMainProcessMessaging(
		this IServiceCollection services,
		Action<NatsOptions> configureOptions)
	{
		var options = new NatsOptions
		{
			StreamName = "SHARPMUSH-MS",
			SubjectPrefix = "sharpmush.ms",
		};
		configureOptions(options);
		return services.AddNatsMessagingCore(options);
	}

	/// <summary>
	/// Adds NATS JetStream messaging for the main Server (game engine) role with consumers.
	/// <list type="bullet">
	///   <item>Publishes to stream <c>SHARPMUSH-MS</c> (subject prefix <c>sharpmush.ms</c>)</item>
	///   <item>Consumes from stream <c>SHARPMUSH-CS</c> (subject prefix <c>sharpmush.cs</c>)</item>
	/// </list>
	/// </summary>
	public static IServiceCollection AddNatsMainProcessMessaging(
		this IServiceCollection services,
		Action<NatsOptions> configureOptions,
		Action<INatsConsumerConfigurator> configureConsumers)
	{
		var options = new NatsOptions
		{
			StreamName = "SHARPMUSH-MS",
			SubjectPrefix = "sharpmush.ms",
			ConsumeStreamName = "SHARPMUSH-CS",
			ConsumeSubjectPrefix = "sharpmush.cs",
		};
		configureOptions(options);
		return services.AddNatsMessagingCore(options, configureConsumers, "mainprocess");
	}

	private static IServiceCollection AddNatsMessagingCore(
		this IServiceCollection services,
		NatsOptions options,
		Action<INatsConsumerConfigurator>? configureConsumers = null,
		string groupPrefix = "")
	{
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
	/// Registers <typeparamref name="TConsumer"/> as the handler for
	/// <c>IMessageConsumer&lt;TMessage&gt;</c> and creates a durable JetStream consumer for
	/// <typeparamref name="TMessage"/>'s subject.
	/// </summary>
	/// <remarks>
	/// C# cannot infer <typeparamref name="TMessage"/> from <typeparamref name="TConsumer"/>, so both are
	/// spelled at the call site. That is deliberate: the constraint makes a consumer that does not handle
	/// the named message a compile error, and it leaves nothing for the registration to discover at runtime.
	/// </remarks>
	void AddConsumer<TConsumer, TMessage>()
		where TConsumer : class, IMessageConsumer<TMessage>
		where TMessage : class;
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

	public void AddConsumer<TConsumer, TMessage>()
		where TConsumer : class, IMessageConsumer<TMessage>
		where TMessage : class
	{
		var messageType = typeof(TMessage);
		var subject = GetSubjectForMessageType(messageType, _options.GetConsumeSubjectPrefix());
		var durableName = GetDurableName(messageType);

		_services.AddTransient<IMessageConsumer<TMessage>, TConsumer>();

		_registry.Registrations.Add(new NatsConsumerRegistration(messageType, subject, durableName,
			static (sp, msg, ct) => sp.GetRequiredService<IMessageConsumer<TMessage>>().HandleAsync((TMessage)msg, ct)));
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
