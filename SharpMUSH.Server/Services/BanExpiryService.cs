using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Services;

/// <summary>
/// Lifts timed bans once they run out: the account is made active again, and the audit log records
/// <c>ban.expired</c> as the server's own action.
/// </summary>
/// <remarks>
/// A banned account cannot sign in whether or not this has run yet, so the pass only decides how soon after
/// the expiry the account works again. The clock is a <see cref="TimeProvider"/> so a test can move it.
/// </remarks>
public sealed class BanExpiryService(
	IAccountService accounts,
	IAuditLog audit,
	ILogger<BanExpiryService> logger,
	TimeProvider time) : BackgroundService
{
	public BanExpiryService(IAccountService accounts, IAuditLog audit, ILogger<BanExpiryService> logger)
		: this(accounts, audit, logger, TimeProvider.System)
	{
	}

	public static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		using var timer = new PeriodicTimer(Interval, time);
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
					logger.LogError(ex, "Ban expiry pass failed; it is retried on the next pass");
				}
			} while (await timer.WaitForNextTickAsync(stoppingToken));
		}
		catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
		{
			// The host is stopping; the next start lifts whatever ran out meanwhile.
		}
	}

	/// <summary>One pass: lifts every ban that has run out, and returns them.</summary>
	public async Task<IReadOnlyList<AccountBan>> SweepAsync(CancellationToken ct)
	{
		var lifted = await accounts.LiftExpiredBansAsync(time.GetUtcNow(), ct);
		foreach (var ban in lifted)
		{
			var target = await accounts.GetByIdAsync(ban.AccountId, ct) is { } account
				? AuditTargets.Of(account)
				: AuditTargets.Of(AuditTargetKinds.Account, ban.AccountId);
			await audit.RecordSystemAsync(AuditActions.BanExpired, target,
				$"banned {ban.At.UtcDateTime:yyyy-MM-dd HH:mm} UTC until {ban.ExpiresAt?.UtcDateTime:yyyy-MM-dd HH:mm} UTC", ct);
		}

		if (lifted.Count > 0)
			logger.LogInformation("Lifted {Count} expired ban(s)", lifted.Count);
		return lifted;
	}
}
