using Mediator;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Notifications;

namespace SharpMUSH.Implementation.Handlers.Telnet;

public class TelnetMSSPHandler(ILogger<TelnetMSSPHandler> logger) : INotificationHandler<UpdateMSSPNotification>
{
	public ValueTask Handle(UpdateMSSPNotification notification, CancellationToken cancellationToken)
	{
		// Log MSSP configuration update
		logger.LogDebug("MSSP configuration update for handle {Handle}: Name={Name}, UTF-8={UTF8}",
			notification.handle, notification.Config.Name, notification.Config.UTF_8);
		
		// TODO: Implement full MSSP protocol - requires MSSP output message infrastructure
		// When implemented, this should publish MSSP configuration to ConnectionServer
		// which will send it to the client via the MSSP protocol
		
		return ValueTask.CompletedTask;
	}
}