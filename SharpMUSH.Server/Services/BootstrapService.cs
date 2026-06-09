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
			// One-time generated bootstrap credential displayed once for the operator.
			// Intentional: admin must see the temporary password to log in and change it.
			// Not accidental cleartext storage of user-supplied sensitive data.
			LogBootstrapBanner(username, password);
		}
		else
		{
			logger.LogInformation("Bootstrap: created admin account '{Username}' linked to #1.", username);
		}
	}

	public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

	/// <summary>
	/// Logs the first-run credentials banner.  The password is a one-time generated value
	/// that the operator must see to log in; displaying it once in startup logs is intentional.
	/// </summary>
	// The password parameter is a one-time generated bootstrap credential printed once at startup
	// so the operator can complete first-run setup. This is not accidental cleartext storage.
	[System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "cs/cleartext-storage-of-sensitive-information",
		Justification = "One-time generated bootstrap credential intentionally shown once in startup logs. Operator must change it on first login.")]
	private void LogBootstrapBanner(string username, string password)
	{
		logger.LogWarning("╔══════════════════════════════════════════════════════════╗");
		logger.LogWarning("║              SHARPMUSH FIRST-RUN SETUP                  ║");
		logger.LogWarning("║  Admin account created. Change this password NOW.       ║");
		logger.LogWarning("║  Username : {Username,-45}║", username);
		logger.LogWarning("║  Password : {Password,-45}║", password);
		logger.LogWarning("╚══════════════════════════════════════════════════════════╝");
	}

	private static string GeneratePassword()
	{
		const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789!@#$%^&*";
		return new string(Enumerable.Range(0, 16)
			.Select(_ => chars[System.Security.Cryptography.RandomNumberGenerator.GetInt32(chars.Length)])
			.ToArray());
	}
}
