using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Services;

/// <summary>
/// Runs once at startup. If no accounts exist, pre-generates the admin account linked
/// to player #1 (God) with an EMPTY password hash — unclaimed. It cannot be logged
/// into until first-run setup claims it (empty hashes never match in account login),
/// mirroring God's PennMUSH-default empty character password.
/// </summary>
public class BootstrapService(
	IAccountService accountService,
	ILogger<BootstrapService> logger) : IHostedService
{
	public async Task StartAsync(CancellationToken cancellationToken)
	{
		if (await accountService.HasAnyAccountAsync(cancellationToken))
		{
			logger.LogDebug("Bootstrap: accounts already exist, skipping.");
			return;
		}

		var account = await accountService.CreateUnclaimedAccountAsync("admin", cancellationToken);
		await accountService.LinkCharacterAsync(account.Id!, new DBRef(1), cancellationToken);

		logger.LogInformation(
			"Bootstrap: pre-generated unclaimed admin account linked to #1. " +
			"Complete first-run setup via the web portal (or set God's password in-game).");
	}

	public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
