using SharpMUSH.ConnectionServer.Services;
using SharpMUSH.Messaging.Messages;
using SharpMUSH.Messaging.Abstractions;

namespace SharpMUSH.ConnectionServer.Consumers;

public class MainProcessReadyConsumer(EngineLifecycleNoticeService notices)
	: IMessageConsumer<MainProcessReadyMessage>
{
	public Task HandleAsync(MainProcessReadyMessage message, CancellationToken cancellationToken = default)
		=> notices.NotifyAsync(message.Timestamp, true, cancellationToken);
}

public class MainProcessShutdownConsumer(EngineLifecycleNoticeService notices)
	: IMessageConsumer<MainProcessShutdownMessage>
{
	public Task HandleAsync(MainProcessShutdownMessage message, CancellationToken cancellationToken = default)
		=> notices.NotifyAsync(message.Timestamp, false, cancellationToken);
}
