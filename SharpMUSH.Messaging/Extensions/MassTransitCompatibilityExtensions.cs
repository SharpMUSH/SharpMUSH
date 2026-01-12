using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Messaging.Abstractions;
using SharpMUSH.Messaging.Adapters;
using SharpMUSH.Messaging.Kafka;

namespace SharpMUSH.Messaging.Extensions;

/// <summary>
/// MassTransit-compatible extension methods for easy migration
/// </summary>
public static class MassTransitCompatibilityExtensions
{
/// <summary>
/// Compatibility method for MassTransit's AddConsumer that works with IConsumer<T>
/// </summary>
public static void AddConsumer<TConsumer>(this IKafkaConsumerConfigurator configurator, Action<object>? configure = null)
where TConsumer : class
{
// Find the IConsumer<T> interface implemented by TConsumer
var consumerInterfaces = typeof(TConsumer).GetInterfaces()
.Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IConsumer<>))
.ToArray();

if (consumerInterfaces.Length == 0)
{
throw new InvalidOperationException($"{typeof(TConsumer).Name} does not implement IConsumer<T>");
}

// Use the first IConsumer<T> interface found
var consumerInterface = consumerInterfaces[0];
var messageType = consumerInterface.GetGenericArguments()[0];

// Determine the topic name from the message type
var topic = GetTopicForMessageType(messageType);

// Check if this is a batch consumer (has BatchOptions configuration)
var enableBatching = configure != null;

// Register the consumer using reflection to call the generic method
var method = typeof(MassTransitCompatibilityExtensions).GetMethod(
nameof(AddConsumerInternal),
System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

var genericMethod = method!.MakeGenericMethod(messageType, typeof(TConsumer));
genericMethod.Invoke(null, [configurator, topic, enableBatching]);
}

private static void AddConsumerInternal<TMessage, TConsumer>(
IKafkaConsumerConfigurator configurator,
string topic,
bool enableBatching)
where TMessage : class
where TConsumer : class, IConsumer<TMessage>
{
// Register the inner consumer in DI
// The configurator's services collection should have this available
if (configurator is KafkaConsumerConfigurator kafkaConfig)
{
kafkaConfig.Services.AddTransient<IConsumer<TMessage>, TConsumer>();
kafkaConfig.Services.AddTransient<IMessageConsumer<TMessage>>(sp =>
{
var innerConsumer = sp.GetRequiredService<IConsumer<TMessage>>();
return new ConsumerAdapter<TMessage>(innerConsumer);
});

// Register with the consumer host
kafkaConfig.AddConsumer<TMessage, ConsumerAdapter<TMessage>>(topic, enableBatching);
}
}

private static string GetTopicForMessageType(Type messageType)
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
