using Mediator;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Services;

/// <summary>
/// Notes who is connected every <see cref="Interval"/>, so MSSP's <c>PLAYERS DAY</c> and <c>PLAYERS WEEK</c> can
/// count distinct players (<see cref="PlayerActivity"/>). A player who connects and leaves between two samples is
/// not counted.
/// </summary>
public sealed class PlayerActivitySampler(
	PlayerActivity activity,
	IConnectionService connections,
	IMediator mediator,
	IOptionsMonitor<SharpMUSHOptions> options,
	TimeProvider time,
	ILogger<PlayerActivitySampler> logger) : BackgroundService
{
	public static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		using var timer = new PeriodicTimer(Interval, time);
		try
		{
			do
			{
				await SampleAsync(stoppingToken);
			}
			while (await timer.WaitForNextTickAsync(stoppingToken));
		}
		catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
		{
			// Stopping: what was noted is already in the server data.
		}
	}

	/// <summary>Notes the players connected now.</summary>
	public async Task SampleAsync(CancellationToken cancellationToken = default)
	{
		try
		{
			var online = await ConnectedPlayers.DistinctAsync(connections, mediator, options.CurrentValue.Cosmetic.CountAll);
			await activity.RecordAsync(online, time.GetUtcNow(), cancellationToken);
		}
		catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
		{
			logger.LogWarning(ex, "Could not note who is connected for MSSP's PLAYERS DAY and PLAYERS WEEK; it is tried again next minute");
		}
	}
}
