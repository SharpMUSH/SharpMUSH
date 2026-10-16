using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
namespace SharpMUSH.Server.Services;

public sealed class RemoteBackupScheduleService(RemoteBackupCoordinator coordinator,
	ILogger<RemoteBackupScheduleService> logger) : BackgroundService
{
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		try { await coordinator.RecoverAsync(); }
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			logger.LogError("Remote backup recovery failed ({Type})", ex.GetType().Name);
		}
		using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
		while (await timer.WaitForNextTickAsync(stoppingToken))
		{
			try { await coordinator.TickAsync(stoppingToken); }
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				logger.LogError("Remote backup coordinator tick failed ({Type})", ex.GetType().Name);
			}
		}
	}
}
