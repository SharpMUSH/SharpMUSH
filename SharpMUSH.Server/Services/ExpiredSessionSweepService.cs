using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Services;

/// <summary>
/// Reclaims account sessions that expired without being presented again. Validation deletes an expired
/// session only when its token comes back, so an abandoned token would otherwise stay on disk, in the
/// account and origin-IP indexes, and in every ban-enforcement scan of those addresses, for good.
/// </summary>
/// <remarks>
/// Each pass deletes in batches of <see cref="BatchSize"/>, one write per batch, so a large backlog never
/// holds the writer for long, and stops once a batch comes back short. Progress and the backlog left are
/// logged.
/// </remarks>
public sealed class ExpiredSessionSweepService(IAccountSessionStore sessions, ILogger<ExpiredSessionSweepService> logger)
	: BackgroundService
{
	public const int BatchSize = 500;
	private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		using var timer = new PeriodicTimer(Interval);
		try
		{
			do
			{
				try
				{
					await SweepAsync(stoppingToken);
				}
				catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
				{
					return;
				}
				catch (Exception ex)
				{
					logger.LogError(ex, "Expired session sweep failed; it is retried on the next pass");
				}
			} while (await timer.WaitForNextTickAsync(stoppingToken));
		}
		catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
		{
		}
	}

	/// <summary>One pass: batches until one comes back short. Returns how many sessions were deleted.</summary>
	public async Task<int> SweepAsync(CancellationToken ct)
	{
		var total = 0;
		IAccountSessionStore.SessionSweep step;
		do
		{
			ct.ThrowIfCancellationRequested();
			step = await sessions.SweepExpiredAsync(BatchSize, ct);
			total += step.Deleted;
		} while (step.Deleted >= BatchSize);

		if (total > 0 || step.Remaining > 0)
		{
			logger.LogInformation("Expired session sweep deleted {Deleted} session(s); {Remaining} expired session(s) remain",
				total, step.Remaining);
		}

		return total;
	}
}
