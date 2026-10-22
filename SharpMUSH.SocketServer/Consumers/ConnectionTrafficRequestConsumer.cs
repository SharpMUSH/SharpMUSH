using SharpMUSH.Messaging.Abstractions;
using SharpMUSH.Messaging.Messages;
using SharpMUSH.SocketServer.Services;

namespace SharpMUSH.SocketServer.Consumers;

/// <summary>Answers <c>SESSION</c>'s question: the bytes each connection here has moved.</summary>
public class ConnectionTrafficRequestConsumer(ConnectionTrafficCounters counters, IMessageBus bus)
	: IMessageConsumer<ConnectionTrafficRequestMessage>
{
	public Task HandleAsync(ConnectionTrafficRequestMessage message, CancellationToken cancellationToken = default) =>
		bus.Publish(new ConnectionTrafficReportMessage(message.RequestId, counters.Snapshot()), cancellationToken);
}
