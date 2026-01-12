using SharpMUSH.Messaging.Adapters;
using Mediator;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Notifications;
using SharpMUSH.Messages;

namespace SharpMUSH.Implementation.Handlers.Telnet;

public class TelnetMSSPHandler(ILogger<TelnetMSSPHandler> logger, IBus publisher) : INotificationHandler<UpdateMSSPNotification>
{
	public async ValueTask Handle(UpdateMSSPNotification notification, CancellationToken cancellationToken)
	{
		// Log MSSP configuration update
		logger.LogDebug("MSSP configuration update for handle {Handle}: Name={Name}, UTF-8={UTF8}",
			notification.handle, notification.Config.Name, notification.Config.UTF_8);
		
		// Convert MSSP config to dictionary for output
		var configuration = new Dictionary<string, string>
		{
			["NAME"] = notification.Config.Name ?? "SharpMUSH",
			["UTF-8"] = (notification.Config.UTF_8 ?? false) ? "1" : "0"
		};
		
		// Publish MSSP output message to ConnectionServer
		await publisher.Publish(
			new MSSPOutputMessage(notification.handle, configuration),
			cancellationToken);
	}
}