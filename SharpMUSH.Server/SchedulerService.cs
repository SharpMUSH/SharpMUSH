using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Server;

public class SchedulerService(ILogger<SchedulerService> logger, ITaskScheduler scheduler, IMUSHCodeParser parser) : BackgroundService
{
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		while (!stoppingToken.IsCancellationRequested)
		{
			try
			{
				await Task.Delay(1, stoppingToken);
				await scheduler.ExecuteAsync(parser, stoppingToken);
			}
			catch (Exception ex)
			{
				logger.LogCritical(ex, nameof(ExecuteAsync));
			}
		}
	}
}