using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Services.RecurringJobs;

namespace SharpMUSH.Server.Services;

public sealed class RecurringJobRunner(IRecurringJobService jobs, ILogger<RecurringJobRunner> logger) : BackgroundService
{
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
		var reported = false;
		try
		{
			do
			{
				try { await jobs.RunDueAsync(stoppingToken); reported = false; }
				catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
				catch (Exception ex)
				{
					if (!reported) logger.LogError(ex, "Recurring job polling failed; durable definitions will be retried");
					reported = true;
				}
			} while (await timer.WaitForNextTickAsync(stoppingToken));
		}
		catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
	}
}
