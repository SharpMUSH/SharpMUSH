using SharpMUSH.Messaging.Abstractions;

namespace SharpMUSH.Messaging.Adapters;

/// <summary>
/// Compatibility alias for IBus to minimize changes when migrating from MassTransit
/// This allows existing code using "IBus" to continue working with minimal changes
/// </summary>
public interface IBus : IMessageBus
{
}

/// <summary>
/// Adapter that provides IBus compatibility
/// </summary>
public class BusAdapter : IBus
{
private readonly IMessageBus _messageBus;

public BusAdapter(IMessageBus messageBus)
{
_messageBus = messageBus;
}

public Task Publish<T>(T message, CancellationToken cancellationToken = default) where T : class
=> _messageBus.Publish(message, cancellationToken);

public Task Send<T>(T message, CancellationToken cancellationToken = default) where T : class
=> _messageBus.Send(message, cancellationToken);
}
