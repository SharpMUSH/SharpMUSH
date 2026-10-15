using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Services;

/// <summary>
/// Writes the activity counted in memory (<see cref="IActivityLedger"/>) once a minute, and once more as the
/// server stops, so a pose never waits on a write of its own.
/// </summary>
public sealed class ActivityFlushService(IActivityLedger ledger, ILogger<ActivityFlushService> logger) : BackgroundService
{
	private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		using var timer = new PeriodicTimer(Interval);
		try
		{
			while (await timer.WaitForNextTickAsync(stoppingToken))
			{
				await ledger.FlushAsync(stoppingToken);
			}
		}
		catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
		{
			// Stopping: StopAsync writes what is left.
		}
	}

	public override async Task StopAsync(CancellationToken cancellationToken)
	{
		await base.StopAsync(cancellationToken);
		try
		{
			await ledger.FlushAsync(cancellationToken);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Writing the last activity counts at shutdown failed");
		}
	}
}
