using Mediator;
using SharpMUSH.Library.Notifications;

namespace SharpMUSH.Implementation.Handlers.Telnet;

public class TelnetOutputHandler: INotificationHandler<TelnetOutputNotification>
{
	public ValueTask Handle(TelnetOutputNotification notification, CancellationToken cancellationToken)
	{
		// TODO: Implement
		return ValueTask.CompletedTask;
	}
}