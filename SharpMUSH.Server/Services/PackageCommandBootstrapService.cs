using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Services;

/// <summary>
/// Adds and hooks the commands the installed packages declare (<c>commands:</c>) once at boot. The command table and
/// its hooks live in memory; the installed package records are what lasts.
/// </summary>
/// <remarks>
/// Registered after <see cref="PluginBootstrapService"/>, so a package command that a plugin's command took is
/// refused rather than covering it, and before <see cref="CommandRestrictionBootstrapService"/>, so a configured
/// restriction naming a package command finds it.
/// </remarks>
public sealed class PackageCommandBootstrapService(
	IPackageRegistryService registry,
	IPackageCommandRegistrar commands,
	ILogger<PackageCommandBootstrapService> logger) : IHostedService
{
	public async Task StartAsync(CancellationToken cancellationToken)
	{
		try
		{
			var added = 0;
			foreach (var package in await registry.GetInstalledPackagesAsync())
			{
				foreach (var command in package.Commands ?? [])
				{
					switch (await commands.RegisterAsync(command))
					{
						case Success:
							added++;
							break;
						case Error<string> error:
							logger.LogWarning("Package {Package}'s command {Command} was not added: {Reason}",
								package.Id, command.Name, error.Value);
							break;
					}
				}
			}

			if (added > 0)
			{
				logger.LogInformation("Added {Count} package command(s).", added);
			}
		}
		catch (Exception ex)
		{
			// The game still starts; the commands are missing until the packages are applied again.
			logger.LogError(ex, "Adding the installed packages' commands failed.");
		}
	}

	public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
