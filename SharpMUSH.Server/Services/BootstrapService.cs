using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Services;

/// <summary>
/// Runs once at startup. If no accounts exist, creates a default admin account
/// linked to player #1 (God) using credentials from BootstrapOptions.
/// </summary>
public class BootstrapService(
	IAccountService accountService,
	IOptions<BootstrapOptions> options,
	ILogger<BootstrapService> logger) : IHostedService
{
	public async Task StartAsync(CancellationToken cancellationToken)
	{
		if (await accountService.HasAnyAccountAsync(cancellationToken))
		{
			logger.LogDebug("Bootstrap: accounts already exist, skipping.");
			return;
		}

		var opts = options.Value;
		var username = string.IsNullOrWhiteSpace(opts.AdminUsername) ? "admin" : opts.AdminUsername;
		string password;
		var generated = false;

		if (string.IsNullOrWhiteSpace(opts.AdminPassword))
		{
			password = GeneratePassword();
			generated = true;
		}
		else
		{
			password = opts.AdminPassword;
		}

		var result = await accountService.CreateAccountAsync(username, null, password, cancellationToken);
		if (result.IsT1)
		{
			logger.LogError("Bootstrap: failed to create admin account: {Error}", result.AsT1.Value);
			return;
		}

		var account = result.AsT0;
		await accountService.LinkCharacterAsync(account.Id!, new DBRef(1), cancellationToken);

		if (generated)
			await accountService.ForcePasswordChangeAsync(account.Id!, cancellationToken);

		if (generated)
		{
			logger.LogWarning("╔══════════════════════════════════════════════════════════╗");
			logger.LogWarning("║              SHARPMUSH FIRST-RUN SETUP                  ║");
			logger.LogWarning("║  Admin account created. Change this password NOW.       ║");
			logger.LogWarning("║  Username : {Username,-45}║", username);
			logger.LogWarning("║  Password : {Password,-45}║", password);
			logger.LogWarning("╚══════════════════════════════════════════════════════════╝");
		}
		else
		{
			logger.LogInformation("Bootstrap: created admin account '{Username}' linked to #1.", username);
		}
	}

	public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

	private static string GeneratePassword()
	{
		const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789!@#$%^&*";
		return new string(Enumerable.Range(0, 16)
			.Select(_ => chars[System.Security.Cryptography.RandomNumberGenerator.GetInt32(chars.Length)])
			.ToArray());
	}
}
