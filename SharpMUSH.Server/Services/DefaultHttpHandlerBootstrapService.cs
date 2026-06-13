using System.Reflection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Services;

/// <summary>
/// Installs the bundled <c>http-hooks</c> package onto the configured
/// <c>http_handler</c> object (#4 by default) at first boot. The default HTTP
/// handler softcode (verb routers + profile API) lives entirely in the package
/// manifest now — the package manager is its delivery mechanism, proving it can
/// own a core system's softcode (decision 20.3, attach mode).
///
/// Idempotent: skips when <c>http-hooks</c> is already installed, so an admin
/// can upgrade, customize (three-way merge protects local edits), or uninstall
/// it through the package panel without this service fighting them. If the
/// handler already carries differing attributes (e.g. a game upgrading from the
/// old hardcoded seeding), conflicts are resolved in favor of the existing
/// values so nothing is clobbered.
/// </summary>
public class DefaultHttpHandlerBootstrapService(
	IPackageManifestService manifests,
	IPackageRegistryService registry,
	IPackageInstallService installer,
	IOptionsWrapper<SharpMUSHOptions> options,
	ILogger<DefaultHttpHandlerBootstrapService> logger) : IHostedService
{
	private const string PackageId = "http-hooks";
	private const string ResourceName = "SharpMUSH.Server.BundledPackages.http-hooks.package.yaml";

	public async Task StartAsync(CancellationToken cancellationToken)
	{
		var handler = options.CurrentValue.Database.HttpHandler;
		if (handler is null or 0)
		{
			logger.LogDebug("No http_handler configured; skipping http-hooks package install.");
			return;
		}

		var already = await registry.GetInstalledPackageAsync(PackageId);
		if (already.IsT0)
		{
			logger.LogDebug("Package {PackageId} already installed (v{Version}); leaving it to the package manager.",
				PackageId, already.AsT0.Version);
			return;
		}

		var yaml = await ReadBundledManifestAsync(cancellationToken);
		if (yaml is null)
		{
			logger.LogError("Bundled {ResourceName} resource not found; cannot install default HTTP handler softcode.", ResourceName);
			return;
		}

		var parsed = manifests.ParseManifest(yaml);
		if (parsed.IsT1)
		{
			logger.LogError("Bundled http-hooks manifest is invalid: {Issues}",
				string.Join("; ", parsed.AsT1.Issues.Select(i => i.ToString())));
			return;
		}

		var manifest = parsed.AsT0.Manifest;

		// Resolve any pre-existing conflicts in favor of what is already on the
		// handler (a game migrating off the old hardcoded seeding), so the
		// install never clobbers an admin's customizations.
		var plan = await installer.PlanAsync(manifest, cancellationToken: cancellationToken);
		if (plan.IsBlocked)
		{
			logger.LogWarning("Cannot install http-hooks (http_handler #{Handler} unresolved or blocked): {Issues}",
				handler.Value, string.Join("; ", plan.DependencyIssues.Select(i => i.PackageId)));
			return;
		}

		var decisions = plan.Attributes
			.Where(a => a.Action == PackageAttributeAction.Conflict)
			.Select(a => new PackageConflictDecision(a.TargetRef, a.Attribute, PackageConflictResolution.KeepMine))
			.ToList();

		var source = new PackageApplySource("bundled:sharpmush", PackageId, "bundled", null);
		var result = await installer.ApplyAsync(manifest, new PackageApplyRequest(
			source, new Dictionary<string, string>(), decisions), cancellationToken);

		result.Switch(
			ok => logger.LogInformation(
				"Installed http-hooks v{Version} onto http_handler #{Handler} (revision {Revision}, {Created} object(s) touched).",
				manifest.Version, handler.Value, ok.Revision, ok.CreatedObjects.Count),
			error => logger.LogError("Failed to install http-hooks: {Error}", error.Value));
	}

	public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

	private static async Task<string?> ReadBundledManifestAsync(CancellationToken cancellationToken)
	{
		await using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
		if (stream is null)
		{
			return null;
		}

		using var reader = new StreamReader(stream);
		return await reader.ReadToEndAsync(cancellationToken);
	}
}
