using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Plugins.World.Common;

/// <summary>Sweeps the lent rooms once a minute (<see cref="WorldRooms.SweepAsync"/>).</summary>
public sealed class WorldRoomSweep(IServiceProvider services, TimeProvider time, ILogger<WorldRoomSweep> logger) : BackgroundService
{
	public static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		using var timer = new PeriodicTimer(Interval, time);
		while (await timer.WaitForNextTickAsync(stoppingToken))
		{
			try
			{
				var parser = services.GetRequiredService<IMUSHCodeParser>();
				var (returned, destroyed) = await services.GetRequiredService<WorldRooms>().SweepAsync(parser, cancellationToken: stoppingToken);
				if (returned + destroyed > 0)
					logger.LogInformation("World rooms: {Returned} went back, {Destroyed} spare(s) destroyed", returned, destroyed);
			}
			catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
			{
				return;
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "Error sweeping the World plugin's lent rooms");
			}
		}
	}
}
