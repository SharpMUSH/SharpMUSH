using SharpMUSH.Messaging.Abstractions;
using Mediator;
using SharpMUSH.Library.Notifications;
using SharpMUSH.Messages;
using System.Text;

namespace SharpMUSH.Implementation.Handlers.Telnet;

public class TelnetOutputHandler(IMessageBus publisher) : INotificationHandler<TelnetOutputNotification>
{
	public async ValueTask Handle(TelnetOutputNotification notification, CancellationToken cancellationToken)
	{
		// Convert output string to bytes and publish to ConnectionServer for each handle
		var outputBytes = Encoding.UTF8.GetBytes(notification.Output);
		
		foreach (var handle in notification.Handles)
		{
			if (long.TryParse(handle, out var handleLong))
			{
				await publisher.Publish(
					new TelnetOutputMessage(handleLong, outputBytes),
					cancellationToken);
			}
		}
	}
}