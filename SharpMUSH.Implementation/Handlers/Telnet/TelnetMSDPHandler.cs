using MassTransit;
using Mediator;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Notifications;
using SharpMUSH.Messages;

namespace SharpMUSH.Implementation.Handlers.Telnet;

public class TelnetMSDPHandler(ILogger<TelnetMSDPHandler> logger, IBus publisher) : INotificationHandler<UpdateMSDPNotification>
{
	public async ValueTask Handle(UpdateMSDPNotification notification, CancellationToken cancellationToken)
	{
		// Log MSDP update request
		logger.LogDebug("MSDP update requested for handle {Handle}, reset variable: {ResetVariable}",
			notification.handle, notification.ResetVariable);
		
		// For now, send an empty variable update to acknowledge the reset
		// In a full implementation, this would query game state and send relevant MSDP variables
		var variables = new Dictionary<string, string>();
		
		if (!string.IsNullOrEmpty(notification.ResetVariable))
		{
			// Reset specific variable - in full implementation, would query and send that variable
			logger.LogDebug("Resetting MSDP variable: {Variable}", notification.ResetVariable);
		}
		
		// Publish MSDP output message to ConnectionServer
		await publisher.Publish(
			new MSDPOutputMessage(notification.handle, variables),
			cancellationToken);
	}
}