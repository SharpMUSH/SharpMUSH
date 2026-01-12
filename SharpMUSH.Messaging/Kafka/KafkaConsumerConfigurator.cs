using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Messaging.Abstractions;
using SharpMUSH.Messaging.Extensions;

namespace SharpMUSH.Messaging.Kafka;

/// <summary>
/// Implementation of Kafka consumer configurator
/// </summary>
public class KafkaConsumerConfigurator : IKafkaConsumerConfigurator
{
private readonly KafkaConsumerHost _consumerHost;
private readonly IServiceCollection _services;

public IServiceCollection Services => _services;

public KafkaConsumerConfigurator(KafkaConsumerHost consumerHost, IServiceCollection services)
{
_consumerHost = consumerHost;
_services = services;
}

public void AddConsumer<TMessage, TConsumer>(string topic, bool enableBatching = false)
where TMessage : class
where TConsumer : class, IMessageConsumer<TMessage>
{
// Register the consumer in DI
_services.AddTransient<IMessageConsumer<TMessage>, TConsumer>();

// Register with the consumer host
_consumerHost.RegisterConsumer<TMessage>(topic, enableBatching);
}

public void AddBatchConsumer<TMessage, TConsumer>(string topic)
where TMessage : class
where TConsumer : class, IBatchMessageConsumer<TMessage>
{
// Register the batch consumer in DI
_services.AddTransient<IBatchMessageConsumer<TMessage>, TConsumer>();

// Register with the consumer host (with batching enabled)
_consumerHost.RegisterConsumer<TMessage>(topic, enableBatching: true);
}
}

/// <summary>
/// Extension methods for simplified consumer registration
/// </summary>
public static class KafkaConsumerConfiguratorExtensions
{
/// <summary>
/// Registers a consumer with automatic message type and topic inference
/// </summary>
public static void AddConsumer<TConsumer>(this IKafkaConsumerConfigurator configurator)
where TConsumer : class
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

// Use reflection to call the generic AddConsumer method
var method = typeof(IKafkaConsumerConfigurator).GetMethod(nameof(IKafkaConsumerConfigurator.AddConsumer));
var genericMethod = method!.MakeGenericMethod(messageType, typeof(TConsumer));
genericMethod.Invoke(configurator, [topic, false]);
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
