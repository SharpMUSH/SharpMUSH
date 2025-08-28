using Mediator;
using SharpMUSH.Library.Notifications;

namespace SharpMUSH.Implementation.Handlers.Telnet;

public class TelnetGMCPHandler: INotificationHandler<SignalGMCPNotification>
{
	public ValueTask Handle(SignalGMCPNotification notification, CancellationToken cancellationToken)
	{
		// TODO: Implement
		return ValueTask.CompletedTask;
	}
}