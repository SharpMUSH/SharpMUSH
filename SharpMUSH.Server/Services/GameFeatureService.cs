using Microsoft.Extensions.Logging;
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
/// Which optional applications this game has, and switching them on and off. The answer is what the
/// game has installed, not a setting beside it: the portal hides an application's pages exactly when
/// the package that serves them is gone.
/// </summary>
public class GameFeatureService(
	IPackageRegistryService registry,
	IPackageInstallService installer,
	IBundledPackageBootstrap bundled,
	IExpandedObjectDataService serverData,
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
		=> (await ApplicationsAsync()).Where(a => a.Enabled).Select(a => a.Id).ToList();

	/// <summary>Every optional application, whether it is on, and whether it can be.</summary>
	public async Task<IReadOnlyList<OptionalApplicationState>> ApplicationsAsync()
	{
		var states = new List<OptionalApplicationState>(All.Count);
		foreach (var app in All)
		{
			var available = IsAvailable(app);
			var installed = await registry.GetInstalledPackageAsync(app.PackageId) is InstalledPackageRecord;
			states.Add(new OptionalApplicationState(app.Id, available && installed, available));
		}

		return states;
	}

	/// <summary>
	/// Turns on the applications in <paramref name="enabled"/> and off every other one. An unknown id is
	/// refused before anything changes; one this server cannot run is left off.
	/// </summary>
	public async Task<Result<Success>> ApplyAsync(IReadOnlyCollection<string> enabled, CancellationToken cancellationToken)
	{
		var unknown = enabled.Where(id => !All.Any(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase))).ToList();
		if (unknown.Count > 0)
		{
			return new Error<string>($"Unknown application: {string.Join(", ", unknown)}.");
		}

		var failures = new List<string>();
		foreach (var app in All)
		{
			var wanted = enabled.Contains(app.Id, StringComparer.OrdinalIgnoreCase) && IsAvailable(app);
			var installed = await registry.GetInstalledPackageAsync(app.PackageId) is InstalledPackageRecord;

			if (wanted)
			{
				await SetDeclinedAsync(app.PackageId, declined: false);
				if (!installed
						&& !(await bundled.InstallBundledAsync([app.PackageId], cancellationToken)).Contains(app.PackageId))
				{
					failures.Add($"{app.Id} could not be installed; the server log says why.");
				}
			}
			else if (installed)
			{
				switch (await installer.UninstallAsync(app.PackageId, cancellationToken: cancellationToken))
				{
					case Success:
						await SetDeclinedAsync(app.PackageId, declined: true);
						logger.LogInformation("Optional application {Application} turned off.", app.Id);
						break;
					case Error<string> error:
						failures.Add($"{app.Id} could not be removed: {error.Value}");
						break;
				}
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

	private bool IsAvailable(OptionalApplication app)
		=> app.RequiredPlugin is null
			|| plugins.Plugins.Any(p => string.Equals(p.Id, app.RequiredPlugin, StringComparison.OrdinalIgnoreCase));
}
