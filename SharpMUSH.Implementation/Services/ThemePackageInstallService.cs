using System.Text.Json;
using Microsoft.Extensions.Logging;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Models.Portal;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Services;

/// <summary>
/// Installs, upgrades and removes theme packages (<c>kind: theme</c>, format 1.22) and hands every other kind to
/// <paramref name="inner"/>. A theme package's themes become themes of the game's (<see cref="PortalThemeService"/>),
/// marked with the package, and the pictures their stylesheets show are stored with the game's media
/// (<see cref="IWikiAssetService"/>) and served from there. Each operation holds the package-operation gate, as
/// every other package operation does.
/// </summary>
public sealed class ThemePackageInstallService(
	IPackageInstallService inner,
	IPackageRegistryService registry,
	IPackageOperationGate gate,
	PortalThemeService themes,
	IWikiAssetService assets,
	EngineVersion engine,
	IOptionsWrapper<SharpMUSHOptions> configuration,
	ILogger<ThemePackageInstallService>? logger = null) : IPackageInstallService
{
	private static readonly JsonSerializerOptions SnapshotJson = new(JsonSerializerDefaults.Web);

	/// <inheritdoc />
	public async Task<PackageChangeset> PlanAsync(PackageManifest manifest, IReadOnlyDictionary<string, string>? configureAnswers = null,
		CancellationToken cancellationToken = default)
	{
		if (manifest.Kind != PackageKind.Theme)
		{
			return await inner.PlanAsync(manifest, configureAnswers, cancellationToken);
		}

		var installed = await registry.GetInstalledPackageAsync(manifest.Name) is InstalledPackageRecord found ? found : null;
		var issues = new List<PackageManifestIssue>();
		if (PackageThemeFiles.NeedsFiles(manifest))
		{
			issues.Add(PackageManifestIssue.Error("themes", "The package's theme files have not been read."));
		}
		else
		{
			issues.AddRange(StylesheetProblems(manifest).Select(problem => PackageManifestIssue.Error("themes", problem)));
		}

		if (installed is not null && installed.Kind != PackageKind.Theme)
		{
			issues.Add(PackageManifestIssue.Error("kind", "A package upgrade cannot change kind. Uninstall the old package first."));
		}

		var stored = await themes.GetStoredAsync();
		return new PackageChangeset(manifest.Name, installed?.Version, manifest.Version.ToString(),
			installed is null ? PackageRevisionKind.Install : PackageRevisionKind.Upgrade,
			[], [], [],
			PackagePlanService.CheckDependenciesAndConflicts(manifest, await registry.GetInstalledPackagesAsync(), new HashSet<string>()),
			[], [])
		{
			ManifestIssues = issues,
			Themes = Describe(manifest, PackageThemePlan.Compute(stored, manifest.Name, Identities(manifest))),
			Compatibility = PackageServerCompatibility.Check(manifest, engine.Current)
		};
	}

	/// <inheritdoc />
	public Task<Result<PackageApplyResult>> ApplyAsync(PackageManifest manifest, PackageApplyRequest request,
		CancellationToken cancellationToken = default, IPackageFileSource? fileSource = null)
		=> manifest.Kind != PackageKind.Theme
			? inner.ApplyAsync(manifest, request, cancellationToken, fileSource)
			: gate.RunAsync(() => ApplyThemesAsync(manifest, request, fileSource, cancellationToken), cancellationToken);

	/// <inheritdoc />
	public async Task<Result<Success>> UninstallAsync(string packageId, bool force = false, CancellationToken cancellationToken = default)
		=> await registry.GetInstalledPackageAsync(packageId) is InstalledPackageRecord { Kind: PackageKind.Theme }
			? await gate.RunAsync(() => UninstallThemesAsync(packageId, force), cancellationToken)
			: await inner.UninstallAsync(packageId, force, cancellationToken);

	/// <inheritdoc />
	public async Task<Result<PackageRollbackResult>> RollbackAsync(string packageId, int revision, CancellationToken cancellationToken = default)
		=> await registry.GetInstalledPackageAsync(packageId) is InstalledPackageRecord { Kind: PackageKind.Theme }
			? new Error<string>($"Cannot roll back '{packageId}': a theme package's revisions do not keep its stylesheets and pictures. "
				+ "Install the version you want instead.")
			: await inner.RollbackAsync(packageId, revision, cancellationToken);

	/// <inheritdoc />
	public Task<Found<string>> GetReadmeAsync(string packageId) => inner.GetReadmeAsync(packageId);

	private async Task<Result<PackageApplyResult>> ApplyThemesAsync(PackageManifest manifest, PackageApplyRequest request,
		IPackageFileSource? fileSource, CancellationToken cancellationToken)
	{
		var compatibility = PackageServerCompatibility.Check(manifest, engine.Current);
		if (compatibility.Fit == PackageServerFit.BelowMinimum)
		{
			return new Error<string>($"Plan is blocked: requires SharpMUSH {compatibility.Minimum}; this server is {compatibility.Server}.");
		}

		if (compatibility.Fit == PackageServerFit.AboveMaximum && !request.AllowPastMaximum)
		{
			return new Error<string>($"This package runs on SharpMUSH up to {compatibility.Maximum}; this server is {compatibility.Server}. "
				+ "Confirm that you want to apply it anyway.");
		}

		switch (await PackageThemeFiles.LoadAsync(manifest, fileSource, cancellationToken))
		{
			case PackageManifest loaded:
				manifest = loaded;
				break;
			case Error<string> error:
				return error;
		}

		if (StylesheetProblems(manifest) is [_, ..] problems)
		{
			return new Error<string>($"Theme files cannot be used: {string.Join("; ", problems)}");
		}

		var installed = await registry.GetInstalledPackageAsync(manifest.Name) is InstalledPackageRecord found ? found : null;
		if (installed is not null && installed.Kind != PackageKind.Theme)
		{
			return new Error<string>("A package upgrade cannot change kind. Uninstall the old package first.");
		}

		var dependencyIssues = PackagePlanService.CheckDependenciesAndConflicts(manifest, await registry.GetInstalledPackagesAsync(),
			new HashSet<string>());
		if (dependencyIssues.Count > 0)
		{
			return new Error<string>($"Plan is blocked: {string.Join("; ", dependencyIssues.Select(Describe))}");
		}

		var plan = PackageThemePlan.Compute(await themes.GetStoredAsync(), manifest.Name, Identities(manifest));
		if (plan.Where(change => change.Action == PackageThemeAction.Blocked).ToArray() is { Length: > 0 } blocked)
		{
			return new Error<string>($"Plan is blocked: {string.Join("; ", blocked.Select(b => $"theme {b.Id}: {b.Detail}"))}");
		}

		var previous = await themes.GetPackageThemesAsync(manifest.Name);
		var saved = new List<string>();
		var built = new List<StoredPortalTheme>();
		foreach (var theme in manifest.PortalThemes)
		{
			switch (await StoreAsync(manifest, theme, saved, cancellationToken))
			{
				case StoredPortalTheme stored:
					built.Add(stored);
					break;
				case Error<string> error:
					await DeleteAsync(saved);
					return error;
			}
		}

		return await themes.ReplacePackageThemesAsync(manifest.Name, built) switch
		{
			IReadOnlyList<string> released => await RecordOrRestoreAsync(manifest, request, installed, plan, previous, saved, released),
			Error<string> error => await DiscardedAsync(saved, new Error<string>($"Plan is blocked: {error.Value}")),
		};
	}

	private async Task<Result<PackageApplyResult>> DiscardedAsync(IReadOnlyList<string> saved, Error<string> error)
	{
		await DeleteAsync(saved);
		return error;
	}

	/// <summary>
	/// The registry rows for a version whose themes are written; if they cannot be written, the previous version's
	/// themes and rows go back, so the game and the registry agree. The pictures the new version no longer uses are
	/// deleted only once both are written.
	/// </summary>
	private async Task<Result<PackageApplyResult>> RecordOrRestoreAsync(PackageManifest manifest, PackageApplyRequest request,
		InstalledPackageRecord? installed, IReadOnlyList<PackageThemeChange> plan, IReadOnlyList<StoredPortalTheme> previous,
		IReadOnlyList<string> saved, IReadOnlyList<string> released)
	{
		var previousDependencies = await registry.GetPackageDependenciesAsync(manifest.Name);
		PackageApplyResult result;
		try
		{
			result = await RecordAsync(manifest, request, installed, plan);
		}
		catch
		{
			if (installed is null)
			{
				await registry.RemoveInstalledPackageAsync(manifest.Name);
			}
			else
			{
				await registry.UpsertInstalledPackageAsync(installed);
			}

			await registry.SetPackageDependenciesAsync(manifest.Name, previousDependencies);
			if (await themes.ReplacePackageThemesAsync(manifest.Name, previous, dropDefaults: true) is IReadOnlyList<string>)
			{
				// The new version's pictures are in use only while its themes are stored.
				await DeleteAsync(saved);
			}

			throw;
		}

		await DeleteAsync(released);
		return result;
	}

	/// <summary>
	/// <paramref name="theme"/> as the game stores it: its pictures saved with the game's media, under the Package
	/// Manager, and its stylesheet pointed at where they are served and checked once more as it will be stored.
	/// </summary>
	private async Task<Result<StoredPortalTheme>> StoreAsync(PackageManifest manifest, PackageThemeSpec theme, List<string> saved,
		CancellationToken cancellationToken)
	{
		var addresses = new Dictionary<string, string>(StringComparer.Ordinal);
		var owner = $"#{DatabaseOptions.PackageManagerOrSeeded(configuration.CurrentValue.Database.PackageManager)}";
		foreach (var image in theme.Images)
		{
			await using var content = new MemoryStream(image.Content!, writable: false);
			switch (await assets.SaveAsync(Path.GetFileName(image.File), image.ContentType, content, owner, isPrivate: true, cancellationToken))
			{
				case WikiAsset asset:
					saved.Add(asset.Id);
					addresses[image.File] = $"/api/wiki-assets/{asset.Id}/{asset.FileName}";
					break;
				case Error<string> error:
					return new Error<string>($"Theme {theme.Id}: {image.File} could not be stored: {error.Value}");
			}
		}

		var stylesheet = theme.Stylesheet is { } css && !string.IsNullOrWhiteSpace(css)
			? PackageThemeFiles.PointAt(css, theme, image => addresses[image.File])
			: null;
		if (stylesheet is not null && ThemeStylesheet.Validate(stylesheet) is [_, ..] problems)
		{
			return new Error<string>($"Theme files cannot be used: {theme.Id}: {string.Join(" ", problems)}");
		}

		return new StoredPortalTheme
		{
			Id = theme.Id,
			Name = theme.Name,
			Dark = theme.Dark,
			Published = true,
			Tokens = PortalThemeService.CompleteTokens(theme.Tokens),
			Stylesheet = stylesheet,
			Package = manifest.Name,
			PackageVersion = manifest.Version.ToString(),
			Assets = theme.Images.Select(image => addresses[image.File]).Select(AssetId).ToList()
		};
	}

	/// <summary>The install row, its dependencies and a revision, as a managed package records them.</summary>
	private async Task<PackageApplyResult> RecordAsync(PackageManifest manifest, PackageApplyRequest request,
		InstalledPackageRecord? installed, IReadOnlyList<PackageThemeChange> plan)
	{
		var revision = (installed?.CurrentRevision ?? 0) + 1;
		var notes = plan.Select(change => change.Action switch
		{
			PackageThemeAction.Add => $"Added theme {change.Name}. Players can choose it now.",
			PackageThemeAction.Update => $"Updated theme {change.Name}.",
			_ => $"Removed theme {change.Name}. {change.Detail}"
		}).ToList();
		var pastMaximum = manifest.MaximumServer is { } maximum && maximum.IsExceededBy(engine.Current)
			? new PackageMaximumOverride(engine.Current.ToString(), maximum.ToString(), DateTimeOffset.UtcNow)
			: null;
		if (pastMaximum is not null)
		{
			notes.Add($"Applied past maximum_server {pastMaximum.Maximum}: this server is {engine.Current}. The choice is recorded on the package.");
		}

		await registry.UpsertInstalledPackageAsync(new InstalledPackageRecord(
			manifest.Name, manifest.Version.ToString(), request.Source.Repo, request.Source.Path,
			request.Source.Commit, request.Source.Branch, DateTimeOffset.UtcNow, revision,
			Kind: PackageKind.Theme, PastMaximum: pastMaximum));
		await registry.SetPackageDependenciesAsync(manifest.Name, manifest.Dependencies
			.Select(d => new PackageDependencyRecord(manifest.Name, d.PackageId, d.Constraint.ToString()))
			.ToList());
		var snapshot = new PackageRevisionSnapshot(manifest.Version.ToString(), [], [], [], Readme: request.Readme);
		await registry.AddPackageRevisionAsync(new PackageRevisionRecord(
			manifest.Name, revision,
			installed is null ? PackageRevisionKind.Install : PackageRevisionKind.Upgrade,
			manifest.Version.ToString(), request.Source.Commit,
			JsonSerializer.Serialize(snapshot, SnapshotJson),
			JsonSerializer.Serialize(request.ConfigureAnswers, SnapshotJson),
			"[]",
			DateTimeOffset.UtcNow));
		await registry.PrunePackageRevisionsAsync(manifest.Name, request.KeepRevisions);
		return new PackageApplyResult(revision, new Dictionary<string, string>(StringComparer.Ordinal), notes);
	}

	private async Task<Result<Success>> UninstallThemesAsync(string packageId, bool force)
	{
		if (await registry.GetInstalledPackageAsync(packageId) is not InstalledPackageRecord { Kind: PackageKind.Theme })
		{
			return new Error<string>($"'{packageId}' is not an installed theme package.");
		}

		var dependents = await registry.GetPackageDependentsAsync(packageId);
		if (dependents.Count > 0 && !force)
		{
			return new Error<string>(
				$"Cannot uninstall '{packageId}': {string.Join(", ", dependents.Select(d => d.PackageId))} depend(s) on it. Uninstall them first or force-remove.");
		}

		var stored = await themes.GetStoredAsync();
		if (!force && stored.Themes.FirstOrDefault(t => t.Package == packageId && PortalThemeService.IsDefault(stored, t.Id)) is { } inUse)
		{
			return new Error<string>($"Cannot uninstall '{packageId}': its theme {inUse.Name} is the game's default for browsers that prefer "
				+ $"{(inUse.Dark ? "dark" : "light")}. Make another theme the default first, or force-remove it and "
				+ $"{(inUse.Dark ? Library.Models.Portal.BuiltInThemes.Phosphor.Name : Library.Models.Portal.BuiltInThemes.Daylight.Name)} takes its place.");
		}

		switch (await themes.ReplacePackageThemesAsync(packageId, [], dropDefaults: force))
		{
			case IReadOnlyList<string> released:
				try
				{
					await registry.RemoveInstalledPackageAsync(packageId);
				}
				catch
				{
					await themes.ReplacePackageThemesAsync(packageId, stored.Themes.Where(t => t.Package == packageId).ToList(), dropDefaults: true);
					throw;
				}

				await DeleteAsync(released);
				return new Success();
			case Error<string> error:
				return new Error<string>($"Cannot uninstall '{packageId}': {error.Value}");
			default:
				throw new InvalidOperationException("Unknown theme write result.");
		}
	}

	private async Task DeleteAsync(IEnumerable<string> ids)
	{
		// Best effort: a picture that cannot be deleted stays on disk, private and unused, rather than failing a write
		// that has already happened.
		foreach (var id in ids)
		{
			try
			{
				await assets.DeleteAsync(id);
			}
			catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
			{
				logger?.LogWarning(exception, "A theme package's picture {AssetId} could not be deleted.", id);
			}
		}
	}

	private static string AssetId(string address) => address.Split('/')[3];

	private static IReadOnlyList<(string Id, string Name, bool Dark)> Identities(PackageManifest manifest)
		=> manifest.PortalThemes.Select(theme => (theme.Id, theme.Name, theme.Dark)).ToList();

	private static IReadOnlyList<string> StylesheetProblems(PackageManifest manifest)
		=> manifest.PortalThemes
			.Where(theme => theme.Stylesheet is not null)
			.SelectMany(theme => PackageThemeFiles.CheckStylesheet(theme.Stylesheet!, theme).Select(problem => $"{theme.Id}: {problem}"))
			.ToList();

	/// <summary>The plan's lines with what the review screen shows of each new theme: its colours, pictures and stylesheet.</summary>
	private static IReadOnlyList<PackageThemeChange> Describe(PackageManifest manifest, IReadOnlyList<PackageThemeChange> plan)
		=> plan.Select(change => manifest.PortalThemes.FirstOrDefault(theme => theme.Id == change.Id) is { } theme
			? change with
			{
				Tokens = PortalThemeService.CompleteTokens(theme.Tokens),
				Images = theme.Images.Count,
				StylesheetLength = theme.Stylesheet?.Length ?? 0
			}
			: change).ToList();

	private static string Describe(PackageDependencyIssue issue) => issue switch
	{
		{ IsConflict: true } => $"conflicts with installed {issue.PackageId} {issue.InstalledVersion}",
		_ => $"requires {issue.PackageId} {issue.Constraint}{(issue.InstalledVersion is null ? " (not installed)" : $" (installed: {issue.InstalledVersion})")}"
	};
}
