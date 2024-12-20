﻿using Mediator;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Requests;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Implementation.Handlers.Telnet;

public class TelnetInputRequestHandler(ILogger<TelnetInputRequestHandler> logger, ITaskScheduler scheduler)
	: INotificationHandler<TelnetInputRequest>
{
	public async ValueTask Handle(TelnetInputRequest request, CancellationToken ct)
	{
		try
		{
			await scheduler.WriteUserCommand(request.Handle, MModule.single(request.Input), null);
		}
		catch (Exception ex)
		{
			logger.LogCritical(ex, nameof(TelnetInputRequest));
		}
	}
}