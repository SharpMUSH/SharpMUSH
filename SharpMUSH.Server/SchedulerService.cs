using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Server;

public class SchedulerService(ILogger<SchedulerService> logger, ITaskScheduler scheduler, IMUSHCodeParser parser) : BackgroundService
{
	private readonly PeriodicTimer _timer = new(TimeSpan.FromMilliseconds(1));
	
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		while (await _timer.WaitForNextTickAsync(stoppingToken))
		{
			try
			{
				await scheduler.ExecuteAsync(parser, stoppingToken);
			}
			catch (Exception ex)
			{
				logger.LogCritical(ex, nameof(ExecuteAsync));
			}
		}
	}
}