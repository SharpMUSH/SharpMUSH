using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Services;

/// <summary>
/// Installs every bundled default package (see <see cref="BundledPackages.All"/>) at first boot,
/// in dependency order, through the package manager — proving the package manager can own a core
/// system's softcode (decision 20.3). Attach-mode packages land on a configured handler object —
/// the HTTP verb routers / profile API on <c>http_handler</c>, the room.contents OOB pushes on
/// <c>event_handler</c> — and are skipped when that handler is not configured; create-mode
/// packages (e.g. <c>common-functions</c>, <c>scene</c>) always install.
///
/// Idempotent per package: an already-installed package is left to the package manager (so admins
/// can upgrade/customize/uninstall independently) unless this build ships a strictly newer version,
/// in which case it is upgraded in place. Either way pre-existing differing attributes resolve in
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

		var database = options.CurrentValue.Database;

		foreach (var package in BundledPackages.All)
		{
			var handler = package.Requires switch
			{
				BundledPackageHandler.Http => database.HttpHandler,
				BundledPackageHandler.Event => database.EventHandler,
				_ => null
			};

			if (package.Requires is not BundledPackageHandler.None && handler is null or 0)
			{
				logger.LogDebug("No {Handler} configured; skipping attach-mode package {PackageId}.",
					package.Requires is BundledPackageHandler.Http ? "http_handler" : "event_handler",
					package.PackageId);
				continue;
			}

			await InstallOrUpgradeAsync(package.PackageId, cancellationToken);
		}
	}

	private async Task InstallOrUpgradeAsync(string packageId, CancellationToken cancellationToken)
	{
		// Parse first and bail loudly on a bad manifest: an invalid embedded manifest is a build
		// defect, and it must be reported whether or not the package happens to be installed
		// already. Deciding the version gate first would swallow it on every game that has the
		// package, because an unparsed manifest has no version to compare.
		var parsed = manifests.ParseManifest(BundledPackages.ManifestYaml(packageId));
		if (parsed.IsT1)
		{
			logger.LogError("Bundled {PackageId} manifest is invalid: {Issues}",
				packageId, string.Join("; ", parsed.AsT1.Issues.Select(i => i.ToString())));
			return;
		}

		var manifest = parsed.AsT0.Manifest;

		var already = await registry.GetInstalledPackageAsync(packageId);
		if (already.IsT0)
		{
			// Already installed: only step in when this build ships a NEWER version than the
			// game has. Without this a game that installed an older bundled package never sees
			// later additions to it — the portal's "online now" list stayed empty on every game
			// created before GET`ONLINE was added to profile-handler, with no upgrade path short
			// of a manual reinstall. Same-or-newer installed (an admin upgrade, a local fork) is
			// left alone, and the apply below still resolves conflicts in favour of local edits.
			if (!IsNewer(manifest.Version, already.AsT0.Version))
			{
				logger.LogDebug("Package {PackageId} already installed (v{Version}); leaving it to the package manager.",
					packageId, already.AsT0.Version);
				return;
			}

			logger.LogInformation("Upgrading bundled {PackageId} v{Installed} → v{Bundled}.",
				packageId, already.AsT0.Version, manifest.Version);
		}

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

	/// <summary>
	/// True when the bundled version is strictly newer than the installed one. An absent or
	/// unparseable version on either side answers false: bootstrap only ever moves a game
	/// forward on a comparison it is sure of, and leaves anything else to the package manager.
	/// </summary>
	public static bool IsNewer(PackageVersion? bundled, string? installed) =>
		bundled is not null
		&& PackageVersion.TryParse(installed, out var installedVersion)
		&& bundled.CompareTo(installedVersion) > 0;

	public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
