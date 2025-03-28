using Mediator;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Notifications;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Implementation.Handlers.Telnet;

public class TelnetInputRequestHandler(ILogger<TelnetInputRequestHandler> logger, ITaskScheduler scheduler)
	: INotificationHandler<TelnetInputNotification>
{
	public async ValueTask Handle(TelnetInputNotification request, CancellationToken ct)
	{
		try
		{
			await scheduler.WriteUserCommand(
				handle: request.Handle,
				command: MModule.single(request.Input),
				state: ParserState.Empty with { Handle = request.Handle });
		}
		catch (Exception ex)
		{
			logger.LogCritical(ex, nameof(TelnetInputNotification));
		}
	}
}