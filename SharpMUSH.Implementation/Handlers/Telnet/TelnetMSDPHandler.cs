using Mediator;
using SharpMUSH.Library.Notifications;

namespace SharpMUSH.Implementation.Handlers.Telnet;

public class TelnetMSDPHandler: INotificationHandler<UpdateMSDPNotification>
{
	public ValueTask Handle(UpdateMSDPNotification notification, CancellationToken cancellationToken)
	{
		// TODO: Implement
		return ValueTask.CompletedTask;
	}
}