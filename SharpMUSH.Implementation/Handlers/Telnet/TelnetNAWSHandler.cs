using Mediator;
using SharpMUSH.Library.Notifications;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Handlers.Telnet;

public class TelnetNAWSHandler(IConnectionService connectionService): INotificationHandler<UpdateNAWSNotification>
{
	public ValueTask Handle(UpdateNAWSNotification notification, CancellationToken cancellationToken)
	{
		connectionService.Update(notification.Handle, "HEIGHT", notification.Height.ToString());
		connectionService.Update(notification.Handle, "WIDTH", notification.Width.ToString());
		return ValueTask.CompletedTask;
	}
}