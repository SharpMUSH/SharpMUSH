using SharpMUSH.Messaging.Abstractions;

namespace SharpMUSH.Messaging.Adapters;

/// <summary>
/// Compatibility context for consumers migrating from MassTransit
/// </summary>
/// <typeparam name="T">The message type</typeparam>
public class ConsumeContext<T> where T : class
{
public required T Message { get; init; }
public CancellationToken CancellationToken { get; init; }
}

/// <summary>
/// Compatibility interface for consumers migrating from MassTransit
/// </summary>
/// <typeparam name="T">The message type</typeparam>
public interface IConsumer<T> where T : class
{
Task Consume(ConsumeContext<T> context);
}

/// <summary>
/// Adapter that wraps MassTransit-style consumers to work with the new Kafka messaging system
/// </summary>
/// <typeparam name="T">The message type</typeparam>
public class ConsumerAdapter<T> : IMessageConsumer<T> where T : class
{
private readonly IConsumer<T> _innerConsumer;

public ConsumerAdapter(IConsumer<T> innerConsumer)
{
_innerConsumer = innerConsumer;
}

public Task Consume(T message, CancellationToken cancellationToken = default)
{
var context = new ConsumeContext<T>
{
Message = message,
CancellationToken = cancellationToken
};

return _innerConsumer.Consume(context);
}
}
