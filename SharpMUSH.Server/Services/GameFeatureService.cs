using Microsoft.Extensions.Logging;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Implementation.Services;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Models.Portal.Setup;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Services;

/// <summary>
/// An optional application: the bundled package that switches it on, and the plugin it runs on, if any.
/// </summary>
/// <param name="Id">A <see cref="GameFeatures"/> id.</param>
/// <param name="PackageId">The bundled package whose presence is the application being on.</param>
/// <param name="RequiredPlugin">
/// The plugin the package's commands and the portal's pages call into; without it the application is not
/// available on this server, whatever the package says.
/// </param>
public sealed record OptionalApplication(string Id, string PackageId, string? RequiredPlugin = null);

/// <summary>Persisted first-run wizard state (expanded server data).</summary>
public sealed class SetupWizardState
{
	/// <summary>Set by the claim, cleared when the administrator finishes the wizard.</summary>
	public bool Pending { get; set; }
}

/// <summary>
/// The first-boot bundled packages an administrator has turned off (expanded server data). Bootstrap
/// installs a first-boot package into any game that lacks it, so without this record a package the
/// administrator removed came back on the next restart.
/// </summary>
public sealed class DeclinedBundledPackages
{
	public List<string> PackageIds { get; set; } = [];
}

/// <summary>What the portal is told about which optional applications the game has on.</summary>
public interface IGameFeatureReader
{
	/// <summary>The <see cref="GameFeatures"/> ids of the applications this game has on.</summary>
	Task<IReadOnlyList<string>> EnabledAsync();
}

/// <summary>
/// Which optional applications and bundled packages this game has, and switching them on and off. The
/// answer is what the game has installed, not a setting beside it: the portal hides an application's pages
/// exactly when the package that serves them is gone.
/// </summary>
public class GameFeatureService(
	IPackageRegistryService registry,
	IPackageInstallService installer,
	IPackageManifestService manifests,
	IBundledPackageBootstrap bundled,
	IExpandedObjectDataService serverData,
	IOptionsWrapper<SharpMUSHOptions> options,
	PluginCatalog plugins,
	ILogger<GameFeatureService> logger) : IGameFeatureReader
{
	/// <summary>The optional applications, in the order the wizard offers them.</summary>
	public static readonly IReadOnlyList<OptionalApplication> All =
	[
		new(GameFeatures.Scenes, "scene", RequiredPlugin: "scene"),
		new(GameFeatures.WikiReader, "wiki-reader"),
	];

	/// <inheritdoc />
	public async Task<IReadOnlyList<string>> EnabledAsync()
	{
		return await All
			.Where(app => PluginLoaded(app.RequiredPlugin))
			.ToAsyncEnumerable()
			.Where(async (app, _) => await registry.GetInstalledPackageAsync(app.PackageId) is InstalledPackageRecord)
			.Select(app => app.Id)
			.ToListAsync();
	}

	/// <summary>Every package this server ships, whether the game has it, and whether it can.</summary>
	public async Task<IReadOnlyList<BundledPackageState>> PackagesAsync()
	{
		var declined = (await serverData.GetExpandedServerDataAsync<DeclinedBundledPackages>())?.PackageIds ?? [];
		var states = new List<BundledPackageState>(BundledPackages.All.Count);
		foreach (var package in BundledPackages.All)
		{
			var (description, dependsOn) = Catalogue.GetValueOrDefault(package.PackageId);
			states.Add(new BundledPackageState(
				package.PackageId,
				description ?? string.Empty,
				await registry.GetInstalledPackageAsync(package.PackageId) is InstalledPackageRecord,
				HandlerKind(package.Requires),
				IsAvailable(package),
				dependsOn ?? [],
				package.InstallAtFirstBoot && !declined.Contains(package.PackageId, StringComparer.OrdinalIgnoreCase)));
		}

		return states;
	}

	/// <summary>
	/// Installs the bundled packages in <paramref name="wanted"/>, with the bundled packages they depend on, and
	/// removes every other bundled package. An unknown id is refused before anything changes. A package that
	/// cannot be installed now (its handler or plugin is missing) is reported, and the rest still apply.
	/// </summary>
	public async Task<Result<Success>> ApplyPackagesAsync(IReadOnlyCollection<string> wanted, CancellationToken cancellationToken)
	{
		var order = BundledPackages.All.Select(p => p.PackageId).ToList();
		var unknown = wanted.Where(id => !order.Contains(id, StringComparer.OrdinalIgnoreCase)).ToList();
		if (unknown.Count > 0)
		{
			return new Error<string>($"Unknown package: {string.Join(", ", unknown)}.");
		}

		var installed = (await PackagesAsync()).Where(p => p.Installed).Select(p => p.Id)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);
		var plan = BundledPackagePlan.For(
			order.Where(id => wanted.Contains(id, StringComparer.OrdinalIgnoreCase)),
			installed,
			Catalogue.ToDictionary(entry => entry.Key, entry => entry.Value.DependsOn ?? []),
			order);

		var failures = new List<string>();
		foreach (var id in plan.Remove)
		{
			switch (await installer.UninstallAsync(id, cancellationToken: cancellationToken))
			{
				case Success:
					await SetDeclinedAsync(id, declined: true);
					logger.LogInformation("Bundled package {PackageId} removed by the setup wizard.", id);
					break;
				case Error<string> error:
					failures.Add($"{id} could not be removed: {error.Value}");
					break;
			}
		}

		foreach (var id in plan.Install)
		{
			await SetDeclinedAsync(id, declined: false);
			var package = BundledPackages.All.First(p => p.PackageId == id);
			if (!IsAvailable(package))
			{
				failures.Add(package.Requires is BundledPackageHandler.None
					? $"{id} needs a plugin this server has not loaded."
					: $"{id} needs an {HandlerKind(package.Requires)} handler; set one first.");
				continue;
			}

			if (!(await bundled.InstallBundledAsync([id], cancellationToken)).Contains(id))
			{
				failures.Add($"{id} could not be installed; the server log says why.");
			}
		}

		return failures.Count == 0 ? new Success() : new Error<string>(string.Join(" ", failures));
	}

	/// <summary>Whether the first-run wizard is still waiting on the administrator.</summary>
	public async Task<bool> WizardPendingAsync()
		=> (await serverData.GetExpandedServerDataAsync<SetupWizardState>())?.Pending ?? false;

	public async Task SetWizardPendingAsync(bool pending)
		=> await serverData.SetExpandedServerDataAsync(new SetupWizardState { Pending = pending });

	/// <summary>
	/// Records that the administrator removed (or put back) a first-boot package, so bootstrap leaves the
	/// choice standing. Only first-boot packages are recorded; bootstrap never installs the others.
	/// </summary>
	public async Task SetDeclinedAsync(string packageId, bool declined)
	{
		if (!BundledPackages.All.Any(p => p.InstallAtFirstBoot
				&& string.Equals(p.PackageId, packageId, StringComparison.OrdinalIgnoreCase)))
		{
			return;
		}

		var record = await serverData.GetExpandedServerDataAsync<DeclinedBundledPackages>() ?? new DeclinedBundledPackages();
		var present = record.PackageIds.Contains(packageId, StringComparer.OrdinalIgnoreCase);
		if (present == declined)
		{
			return;
		}

		record.PackageIds = declined
			? [.. record.PackageIds, packageId]
			: record.PackageIds.Where(id => !string.Equals(id, packageId, StringComparison.OrdinalIgnoreCase)).ToList();
		await serverData.SetExpandedServerDataAsync(record);
	}

	/// <summary>The <see cref="HandlerKinds"/> value for a bundled package's handler, or null.</summary>
	public static string? HandlerKind(BundledPackageHandler handler) => handler switch
	{
		BundledPackageHandler.Http => HandlerKinds.Http,
		BundledPackageHandler.Event => HandlerKinds.Event,
		_ => null
	};

	/// <summary>Whether <paramref name="package"/> can be installed now: its handler is set, its plugin loaded.</summary>
	private bool IsAvailable(BundledPackages.Descriptor package)
	{
		var database = options.CurrentValue.Database;
		var handlerSet = package.Requires switch
		{
			BundledPackageHandler.Http => database.HttpHandler is not (null or 0),
			BundledPackageHandler.Event => database.EventHandler is not (null or 0),
			_ => true
		};

		return handlerSet
			&& PluginLoaded(All.FirstOrDefault(a => a.PackageId == package.PackageId)?.RequiredPlugin);
	}

	private bool PluginLoaded(string? plugin)
		=> plugin is null || plugins.Plugins.Any(p => string.Equals(p.Id, plugin, StringComparison.OrdinalIgnoreCase));

	/// <summary>Each bundled package's description and bundled dependencies, read from its embedded manifest once.</summary>
	private IReadOnlyDictionary<string, (string? Description, IReadOnlyList<string>? DependsOn)> Catalogue
		=> _catalogue ??= BundledPackages.All.ToDictionary(
			p => p.PackageId,
			p => manifests.ParseManifest(BundledPackages.ManifestYaml(p.PackageId)) switch
			{
				ParsedPackageManifest parsed => ((string?)parsed.Manifest.Description,
					(IReadOnlyList<string>?)parsed.Manifest.Dependencies.Select(d => d.PackageId)
						.Where(BundledPackages.Contains).ToList()),
				_ => (null, null)
			});

	private IReadOnlyDictionary<string, (string? Description, IReadOnlyList<string>? DependsOn)>? _catalogue;
}
