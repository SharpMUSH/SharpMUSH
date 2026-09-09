using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Server.Services;

/// <summary>Reauthorizes bounded sample batches outside parser execution.</summary>
public sealed class QueueProfileCollector(IQueueDiagnosticsService diagnostics,
	ILogger<QueueProfileCollector> logger) : BackgroundService
{
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
		try
		{
			while (await timer.WaitForNextTickAsync(stoppingToken))
			{
				try { await diagnostics.CollectProfilesAsync(stoppingToken); }
				catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
				catch (Exception) { logger.LogWarning("Queue profile collection failed."); }
			}
		}
		catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
	}
}
