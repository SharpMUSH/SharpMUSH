using SharpMUSH.Messaging.Adapters;
using Mediator;
using SharpMUSH.Library.Notifications;
using SharpMUSH.Messages;

namespace SharpMUSH.Implementation.Handlers.Telnet;

public class TelnetGMCPHandler(IBus publisher) : INotificationHandler<SignalGMCPNotification>
{
	public async ValueTask Handle(SignalGMCPNotification notification, CancellationToken cancellationToken)
	{
		// Publish GMCP output message to ConnectionServer for delivery to the client
		await publisher.Publish(
			new GMCPOutputMessage(notification.handle, notification.Module, notification.Writeback),
			cancellationToken);
	}
}