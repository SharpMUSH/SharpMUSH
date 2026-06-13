using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Services;

/// <summary>
/// Installs the bundled default-handler packages onto the configured
/// <c>http_handler</c> object (#4 by default) at first boot, in dependency
/// order: <c>http-handler</c> (verb routers) then <c>profile-handler</c>
/// (the read-only directory/profile API, which requires it). The default HTTP
/// softcode lives entirely in those package manifests now — the package manager
/// is its delivery mechanism, proving it can own a core system's softcode
/// (decision 20.3, attach mode).
///
/// Idempotent per package: skips one that is already installed, so an admin can
/// upgrade, customize (three-way merge protects local edits), or uninstall
/// either independently. Pre-existing differing attributes (e.g. a game
/// upgrading from the old hardcoded seeding) resolve in favor of the existing
/// values, so nothing is clobbered.
/// </summary>
public class DefaultHttpHandlerBootstrapService(
	IPackageManifestService manifests,
	IPackageRegistryService registry,
	IPackageInstallService installer,
	IOptionsWrapper<SharpMUSHOptions> options,
	ILogger<DefaultHttpHandlerBootstrapService> logger) : IHostedService
{
	public async Task StartAsync(CancellationToken cancellationToken)
	{
		var handler = options.CurrentValue.Database.HttpHandler;
		if (handler is null or 0)
		{
			logger.LogDebug("No http_handler configured; skipping default handler package install.");
			return;
		}

		foreach (var packageId in BundledHttpHooks.PackageIds)
		{
			await InstallIfAbsentAsync(packageId, handler.Value, cancellationToken);
		}
	}

	private async Task InstallIfAbsentAsync(string packageId, uint handler, CancellationToken cancellationToken)
	{
		var already = await registry.GetInstalledPackageAsync(packageId);
		if (already.IsT0)
		{
			logger.LogDebug("Package {PackageId} already installed (v{Version}); leaving it to the package manager.",
				packageId, already.AsT0.Version);
			return;
		}

		var parsed = manifests.ParseManifest(BundledHttpHooks.ManifestYaml(packageId));
		if (parsed.IsT1)
		{
			logger.LogError("Bundled {PackageId} manifest is invalid: {Issues}",
				packageId, string.Join("; ", parsed.AsT1.Issues.Select(i => i.ToString())));
			return;
		}

		var manifest = parsed.AsT0.Manifest;

		// Resolve any pre-existing conflicts in favor of what is already on the
		// handler (a game migrating off the old hardcoded seeding) so the
		// install never clobbers an admin's customizations.
		var plan = await installer.PlanAsync(manifest, cancellationToken: cancellationToken);
		if (plan.IsBlocked)
		{
			logger.LogWarning("Cannot install {PackageId} (http_handler #{Handler} unresolved or dependency unmet): {Issues}",
				packageId, handler, string.Join("; ", plan.DependencyIssues.Select(i => i.PackageId)));
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
			ok => logger.LogInformation(
				"Installed {PackageId} v{Version} onto http_handler #{Handler} (revision {Revision}).",
				packageId, manifest.Version, handler, ok.Revision),
			error => logger.LogError("Failed to install {PackageId}: {Error}", packageId, error.Value));
	}

	public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
