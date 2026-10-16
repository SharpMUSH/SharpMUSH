using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.OAuth;

namespace SharpMUSH.Server.Services;

/// <summary>
/// Deletes app codes and tokens that expired without being used again: a code an app never traded in, an access token
/// that ran out, a refresh token an app forgot. A grant is never swept; only the player or staff take one back.
/// </summary>
public sealed class OAuthTokenSweepService(IOAuthService oauth, ILogger<OAuthTokenSweepService> logger) : BackgroundService
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
				catch (Exception ex) when (ex is not OperationCanceledException)
				{
					logger.LogError(ex, "Expired app token sweep failed; it is retried on the next pass");
				}
			} while (await timer.WaitForNextTickAsync(stoppingToken));
		}
		catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
		{
			// The host is stopping; the next start sweeps whatever this pass left.
		}
	}

	/// <summary>One pass: batches until one comes back short. Returns how many went.</summary>
	public async Task<int> SweepAsync(CancellationToken ct)
	{
		var total = 0;
		int deleted;
		do
		{
			ct.ThrowIfCancellationRequested();
			deleted = await oauth.SweepExpiredAsync(BatchSize, ct);
			total += deleted;
		} while (deleted >= BatchSize);

		if (total > 0) logger.LogInformation("Expired app token sweep deleted {Deleted} code(s) and token(s)", total);
		return total;
	}
}
