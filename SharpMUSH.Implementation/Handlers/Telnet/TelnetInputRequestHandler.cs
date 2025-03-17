using Mediator;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Notifications;
using SharpMUSH.Library.Requests;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Implementation.Handlers.Telnet;

public class TelnetInputRequestHandler(ILogger<TelnetInputRequestHandler> logger, ITaskScheduler scheduler)
	: INotificationHandler<TelnetInputNotification>
{
	public async ValueTask Handle(TelnetInputNotification request, CancellationToken ct)
	{
		try
		{
			await scheduler.WriteUserCommand(request.Handle, MModule.single(request.Input), null);
		}
		catch (Exception ex)
		{
			logger.LogCritical(ex, nameof(TelnetInputNotification));
		}
	}
}