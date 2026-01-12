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
