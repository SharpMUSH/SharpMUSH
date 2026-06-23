using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Services;

/// <summary>
/// Installs every bundled default package (see <see cref="BundledPackages.All"/>) at first boot,
/// in dependency order, through the package manager — proving the package manager can own a core
/// system's softcode (decision 20.3). Attach-mode packages (the HTTP verb routers / profile API)
/// land on the configured <c>http_handler</c> object and are skipped when none is configured;
/// create-mode packages (e.g. <c>common-functions</c>, <c>scene</c>) always install.
///
/// Idempotent per package: an already-installed package is left to the package manager (so admins
/// can upgrade/customize/uninstall independently), and pre-existing differing attributes resolve in
/// favor of the existing values (three-way merge protects local edits, nothing is clobbered).
/// </summary>
public class DefaultPackagesBootstrapService(
	IPackageManifestService manifests,
	IPackageRegistryService registry,
	IPackageInstallService installer,
	IOptionsWrapper<SharpMUSHOptions> options,
	ILogger<DefaultPackagesBootstrapService> logger) : IHostedService
{
	public async Task StartAsync(CancellationToken cancellationToken)
	{
		// Bundled packages can be turned off (SHARPMUSH_BOOTSTRAP_BUNDLED_PACKAGES=false) so a host can run
		// the bare engine without the bundled softcode — notably generic unit tests, which must not inherit
		// a package's server-global @hook/override capture (e.g. scene's @EMIT hook). Unset/anything-else
		// installs as normal, so production is unaffected; package- and plugin-dependent tests opt back in.
		if (string.Equals(Environment.GetEnvironmentVariable("SHARPMUSH_BOOTSTRAP_BUNDLED_PACKAGES"), "false",
			    StringComparison.OrdinalIgnoreCase))
		{
			logger.LogInformation("Bundled package bootstrap disabled via SHARPMUSH_BOOTSTRAP_BUNDLED_PACKAGES=false.");
			return;
		}

		var handler = options.CurrentValue.Database.HttpHandler;

		foreach (var package in BundledPackages.All)
		{
			if (package.RequiresHttpHandler && handler is null or 0)
			{
				logger.LogDebug("No http_handler configured; skipping attach-mode package {PackageId}.", package.PackageId);
				continue;
			}

			await InstallIfAbsentAsync(package.PackageId, cancellationToken);
		}
	}

	private async Task InstallIfAbsentAsync(string packageId, CancellationToken cancellationToken)
	{
		var already = await registry.GetInstalledPackageAsync(packageId);
		if (already.IsT0)
		{
			logger.LogDebug("Package {PackageId} already installed (v{Version}); leaving it to the package manager.",
				packageId, already.AsT0.Version);
			return;
		}

		var parsed = manifests.ParseManifest(BundledPackages.ManifestYaml(packageId));
		if (parsed.IsT1)
		{
			logger.LogError("Bundled {PackageId} manifest is invalid: {Issues}",
				packageId, string.Join("; ", parsed.AsT1.Issues.Select(i => i.ToString())));
			return;
		}

		var manifest = parsed.AsT0.Manifest;

		// Resolve any pre-existing conflicts in favor of what is already present (a game migrating
		// off old hardcoded seeding) so the install never clobbers an admin's customizations.
		var plan = await installer.PlanAsync(manifest, cancellationToken: cancellationToken);
		if (plan.IsBlocked)
		{
			logger.LogWarning("Cannot install {PackageId} (unresolved target or unmet dependency): {Issues}",
				packageId, string.Join("; ", plan.DependencyIssues.Select(i => i.PackageId)));
			return;
		}

		var decisions = plan.Attributes
			.Where(a => a.Action == PackageAttributeAction.Conflict)
			.Select(a => new PackageConflictDecision(a.TargetRef, a.Attribute, PackageConflictResolution.KeepMine))
			.ToList();

		var source = new PackageApplySource("bundled:sharpmush", packageId, "bundled", null);
		var result = await installer.ApplyAsync(manifest, new PackageApplyRequest(
			source, new Dictionary<string, string>(), decisions), cancellationToken);

		result.Switch(
			ok => logger.LogInformation("Installed bundled {PackageId} v{Version} (revision {Revision}).",
				packageId, manifest.Version, ok.Revision),
			error => logger.LogError("Failed to install {PackageId}: {Error}", packageId, error.Value));
	}

	public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
