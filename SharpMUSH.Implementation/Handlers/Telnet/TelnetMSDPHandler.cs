using Mediator;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Notifications;

namespace SharpMUSH.Implementation.Handlers.Telnet;

public class TelnetMSDPHandler(ILogger<TelnetMSDPHandler> logger) : INotificationHandler<UpdateMSDPNotification>
{
	public ValueTask Handle(UpdateMSDPNotification notification, CancellationToken cancellationToken)
	{
		// Log MSDP update request
		logger.LogDebug("MSDP update requested for handle {Handle}, reset variable: {ResetVariable}",
			notification.handle, notification.ResetVariable);
		
		// TODO: Implement full MSDP protocol - requires MSDP output message infrastructure
		// When implemented, this should publish MSDP variable updates to ConnectionServer
		// which will send them to the client via the MSDP protocol
		
		return ValueTask.CompletedTask;
	}
}