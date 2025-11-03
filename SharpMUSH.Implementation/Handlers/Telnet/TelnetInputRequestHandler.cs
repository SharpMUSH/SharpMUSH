using Mediator;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Notifications;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Handlers.Telnet;

public class TelnetInputRequestHandler(ILogger<TelnetInputRequestHandler> logger, ITaskScheduler scheduler)
	: INotificationHandler<TelnetInputNotification>
{
	public async ValueTask Handle(TelnetInputNotification notification, CancellationToken cancellationToken)
	{
		try
		{
			await scheduler.WriteUserCommand(
				handle: notification.Handle,
				command: MModule.single(notification.Input),
				state: ParserState.Empty with { Handle = notification.Handle });
		}
		catch (Exception ex)
		{
			logger.LogCritical(ex, nameof(TelnetInputNotification));
		}
	}
}