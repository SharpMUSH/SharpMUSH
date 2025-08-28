using Mediator;
using SharpMUSH.Library.Notifications;

namespace SharpMUSH.Implementation.Handlers.Telnet;

public class TelnetMSSPHandler : INotificationHandler<UpdateMSSPNotification>
{
	public ValueTask Handle(UpdateMSSPNotification notification, CancellationToken cancellationToken)
	{
		// TODO: Implement
		return ValueTask.CompletedTask;
	}
}