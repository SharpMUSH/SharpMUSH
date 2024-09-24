using MediatR;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Requests;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Implementation.Handlers.Telnet;

public class TelnetInputRequestHandler(ILogger<TelnetInputRequestHandler> logger, ITaskScheduler scheduler)
	: INotificationHandler<TelnetInputRequest>
{
	public async Task Handle(TelnetInputRequest request, CancellationToken ct)
	{
		try
		{
			await scheduler.Write(request.Handle, MModule.single(request.Input), null);
		}
		catch (Exception ex)
		{
			logger.LogCritical(ex, nameof(TelnetInputRequest));
		}
	}
}