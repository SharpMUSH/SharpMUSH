using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SharpMUSH.Library;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Server.Controllers;

/// <summary>
/// Admin API for the softcode package manager (decision 20.9 — all package
/// operations are web-only). This first slice covers the community-repo
/// directory ("ping the official repo for accepted community repos") and
/// README rendering; browse/plan/apply endpoints follow with the admin UI.
/// </summary>
[ApiController]
[Route("api/packages")]
[Authorize(Policy = PortalPermission.PackagesAdmin)]
public class PackagesController(
	IPackageRegistryService registry,
	IPackageSourceService source,
	IPackageManifestService manifests,
	IPackageInstallService installer,
	IPackageAuthoringService authoring,
	IPackageOperationRunner operations,
	PluginUploadStore uploads,
	PackageManifestResolver manifestResolver,
	IAuditLog audit,
	EngineVersion engine) : ControllerBase
{
	/// <summary>The canonical official repo, used when no official remote is configured yet.</summary>
	public const string DefaultOfficialRepoUrl = "https://github.com/SharpMUSH/SharpMUSH-Packages";

	private static readonly WikiMarkdigPipeline Markdown = new();

	/// <summary>
	/// The catalogue of packages embedded in this build, presented as a remote that is always
	/// present and cannot be edited or removed. Everything else here treats it like any other
	/// remote — browse, plan, apply, review, roll back — except that its manifests come from
	/// embedded resources instead of a clone, so a game with no remotes configured and no network
	/// can still install what its own image ships.
	/// </summary>
	private static readonly PackageRemoteRecord CatalogueRemote = new(
		BundledPackages.RemoteName, BundledPackages.SourceRepo, PackageRemoteTrust.Official, null);

	/// <summary>
	/// Lists accepted community repos: aggregates the <c>community/</c>
	/// listing folders of every configured official remote (falling back to
	/// the canonical SharpMUSH-Packages repo when none is configured).
	/// </summary>
	[HttpGet("community")]
	public async Task<ActionResult<CommunityReposResponse>> GetCommunityRepos(CancellationToken cancellationToken) =>
		Ok(await BuildCommunityDirectoryAsync(cancellationToken));

	private async Task<CommunityReposResponse> BuildCommunityDirectoryAsync(CancellationToken cancellationToken)
	{
		var remotes = await registry.GetPackageRemotesAsync();
		var officials = remotes.Where(r => r.Trust == PackageRemoteTrust.Official).ToList();
		if (officials.Count == 0)
		{
			officials.Add(new PackageRemoteRecord(
				"SharpMUSH Official", DefaultOfficialRepoUrl, PackageRemoteTrust.Official, null));
		}

		var configuredUrls = remotes.Select(r => r.Url).ToHashSet(StringComparer.OrdinalIgnoreCase);
		var listings = new List<CommunityRepoListingDto>();
		var errors = new List<string>();
		var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		foreach (var official in officials)
		{
			var result = await source.GetCommunityListingsAsync(official, cancellationToken);
			switch (result)
			{
				case CommunityRepoDirectory directory:
					errors.AddRange(directory.Errors.Select(e => $"{official.Name}: {e}"));
					foreach (var listing in directory.Listings.Where(l => seenUrls.Add(l.Url)))
					{
						listings.Add(new CommunityRepoListingDto(
							listing, official.Name, configuredUrls.Contains(listing.Url)));
					}

					break;
				case Error<string> error:
					errors.Add($"{official.Name}: {error.Value}");
					break;
			}
		}

		return new CommunityReposResponse(listings, errors);
	}

	/// <summary>
	/// Renders the root README of an ACCEPTED community repo. The URL must
	/// appear in the community directory (or be a configured remote) — this
	/// endpoint never clones arbitrary URLs.
	/// </summary>
	[HttpGet("community/readme")]
	public async Task<ActionResult<ReadmeResponse>> GetCommunityRepoReadme(
		[FromQuery] string url, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(url))
		{
			return BadRequest("'url' is required.");
		}

		var remotes = await registry.GetPackageRemotesAsync();
		var configured = remotes.FirstOrDefault(r => string.Equals(r.Url, url, StringComparison.OrdinalIgnoreCase));

		CommunityRepoListing? accepted = null;
		if (configured is null)
		{
			var directory = await BuildCommunityDirectoryAsync(cancellationToken);
			accepted = directory.Listings
				.FirstOrDefault(l => string.Equals(l.Listing.Url, url, StringComparison.OrdinalIgnoreCase))
				?.Listing;

			if (accepted is null)
			{
				return NotFound("That repo is neither a configured remote nor an accepted community listing.");
			}
		}

		var remote = configured ?? new PackageRemoteRecord(
			accepted!.Name, accepted.Url, PackageRemoteTrust.Community, accepted.Branch);

		return await source.GetReadmeAsync(remote, "", null, cancellationToken) switch
		{
			string markdown => Ok(new ReadmeResponse(markdown, Markdown.RenderReadmeToHtml(markdown))),
			Error<string> error => NotFound(error.Value)
		};
	}

	/// <summary>Scans selected live objects: attrs, flags, parents, and external dbrefs needing classification.</summary>
	[HttpPost("author/scan")]
	public async Task<ActionResult<PackageAuthoringScan>> AuthorScan(
		[FromBody] List<string> objids, CancellationToken cancellationToken)
	{
		return await authoring.ScanAsync(objids, cancellationToken) switch
		{
			PackageAuthoringScan scan => Ok(scan),
			Error<string> error => BadRequest(error.Value)
		};
	}

	/// <summary>Exports a classified selection as a validated package.yaml document.</summary>
	[HttpPost("author/export")]
	public async Task<IActionResult> AuthorExport(
		[FromBody] PackageAuthoringRequest request, CancellationToken cancellationToken)
	{
		return await authoring.ExportAsync(request, cancellationToken) switch
		{
			string yaml => File(System.Text.Encoding.UTF8.GetBytes(yaml), "application/yaml", "package.yaml"),
			Error<string> error => BadRequest(error.Value)
		};
	}

	/// <summary>Lists installed packages with dashboard context.</summary>
	[HttpGet]
	public async Task<ActionResult<IReadOnlyList<InstalledPackageDto>>> GetInstalled()
	{
		var installed = await registry.GetInstalledPackagesAsync();
		// One read of every edge, grouped once, rather than a whole-table scan per package.
		var dependents = (await registry.GetAllPackageDependenciesAsync())
			.OrderBy(d => d.PackageId, StringComparer.Ordinal)
			.ToLookup(d => d.DependsOnId, d => d.PackageId);
		var result = new List<InstalledPackageDto>();
		foreach (var package in installed)
		{
			result.Add(new InstalledPackageDto(
				package,
				await registry.CountManagedAttributesAsync(package.Id),
				await registry.CountPackageObjectsAsync(package.Id),
				dependents[package.Id].ToList()));
		}

		return Ok(result);
	}

	/// <summary>Revision history for an installed package (snapshot payloads omitted).</summary>
	[HttpGet("{id}/revisions")]
	public async Task<ActionResult<IReadOnlyList<RevisionDto>>> GetRevisions(string id)
	{
		var revisions = await registry.GetPackageRevisionsAsync(id);
		return Ok(revisions
			.Select(r => new RevisionDto(r.Revision, r.Kind.ToString().ToLowerInvariant(), r.Version, r.Commit, r.AppliedAt))
			.ToList());
	}

	/// <summary>
	/// Renders the README an installed package was applied with: what its current revision kept, so it
	/// matches the installed version and needs no remote.
	/// </summary>
	[HttpGet("{id}/readme")]
	public async Task<ActionResult<ReadmeResponse>> GetInstalledReadme(string id)
		=> await installer.GetReadmeAsync(id) switch
		{
			string markdown => Ok(new ReadmeResponse(markdown, Markdown.RenderReadmeToHtml(markdown))),
			Library.DiscriminatedUnions.NotFound => NotFound($"'{id}' is not installed with a README.")
		};

	/// <summary>
	/// Rolls back to a prior revision (recorded as a NEW revision, decision 20.13), as a queue entry
	/// after a pre-operation backup; see <see cref="IPackageOperationRunner"/>.
	/// </summary>
	[HttpPost("{id}/rollback/{revision:int}")]
	public async Task<ActionResult<PackageRollbackResult>> Rollback(string id, int revision, CancellationToken cancellationToken)
		=> await operations.RunAsync("rollback", token => installer.RollbackAsync(id, revision, token), cancellationToken,
			_ => Task.FromResult(new BackupProvenance("pre-package", User?.FindFirstValue(ClaimTypes.NameIdentifier), "rollback", $"{id} revision {revision}"))) switch
		{
			PackageOperationRan<Result<PackageRollbackResult>> ran => RolledBack(ran.Result, ran.Backup),
			PackageOperationRefused refused => Unavailable(refused)
		};

	private ActionResult<PackageRollbackResult> RolledBack(Result<PackageRollbackResult> result, WorldBackup? backup) => result switch
	{
		PackageRollbackResult ok => Ok(ok with { Notes = WithBackupNote(ok.Notes, backup) }),
		Error<string> error => BadRequest(error.Value)
	};

	/// <summary>A package operation that never ran: the queue refused it, or its backup failed.</summary>
	private ObjectResult Unavailable(PackageOperationRefused refused) =>
		StatusCode(StatusCodes.Status503ServiceUnavailable, refused.Reason);

	/// <summary>Puts the pre-operation backup first among an operation's notes, so the admin knows where it is.</summary>
	private static IReadOnlyList<string> WithBackupNote(IReadOnlyList<string> notes, WorldBackup? backup) =>
		backup is null ? notes : [$"The world was backed up to {backup.Path} first.", .. notes];

	/// <summary>
	/// Uninstalls a package; 409 when dependents exist and force is not set. A bundled package that
	/// installs at first boot is recorded as turned off, so the next restart does not put it back.
	/// </summary>
	[HttpDelete("{id}")]
	public async Task<IActionResult> Uninstall(string id, [FromQuery] bool force, [FromServices] GameFeatureService features,
		CancellationToken cancellationToken)
	{
		return await operations.RunAsync("uninstall", token => installer.UninstallAsync(id, force, token), cancellationToken,
			_ => Task.FromResult(new BackupProvenance("pre-package", User?.FindFirstValue(ClaimTypes.NameIdentifier), "uninstall", id))) switch
		{
			PackageOperationRan<Result<Success>> { Result: var result } => result switch
			{
				Success => await UninstalledAsync(),
				Error<string> error => Conflict(error.Value)
			},
			PackageOperationRefused refused => Unavailable(refused)
		};

		async Task<IActionResult> UninstalledAsync()
		{
			await features.SetDeclinedAsync(id, declined: true);
			return NoContent();
		}
	}

	/// <summary>
	/// Update check for an installed package: newest release tag, dev-channel
	/// path changes, and the moved-tag trust warning (decision 20.14).
	/// </summary>
	[HttpGet("{id}/update")]
	public async Task<ActionResult<PackageUpdateInfo>> CheckForUpdate(string id, CancellationToken cancellationToken)
	{
		if (await registry.GetInstalledPackageAsync(id) is not InstalledPackageRecord installed)
		{
			return NotFound($"'{id}' is not installed.");
		}

		// A package that came out of the image has no git remote to ask: the fallback below would
		// synthesize a remote whose URL is "bundled:sharpmush", and the source service would try to
		// clone that as a repo and answer with a 502.
		if (BundledPackages.IsCatalogueSource(installed.SourceRepo))
		{
			return Ok(CatalogueUpdateInfo(installed));
		}

		// An uploaded package has nowhere to look for a newer version; the next one is another upload.
		if (string.Equals(installed.SourceRepo, PluginUploadStore.SourceRepo, StringComparison.Ordinal))
		{
			return Ok(new PackageUpdateInfo(installed.Version, null, null, false, false, false));
		}

		var remotes = await registry.GetPackageRemotesAsync();
		var remote = remotes.FirstOrDefault(r =>
				string.Equals(r.Url, installed.SourceRepo, StringComparison.OrdinalIgnoreCase))
			?? new PackageRemoteRecord(
				installed.SourceRepo, installed.SourceRepo,
				PackageRemoteTrust.Unknown, installed.PinnedBranch);

		return await source.CheckForUpdateAsync(remote, installed, cancellationToken) switch
		{
			PackageUpdateInfo info => Ok(info),
			Error<string> error => StatusCode(StatusCodes.Status502BadGateway, error.Value)
		};
	}

	/// <summary>
	/// Lists remotes: the built-in catalogue first, then the configured ones. A stored remote that
	/// shadows the reserved name is dropped rather than listed twice — it can only predate the
	/// reservation, since <see cref="UpsertRemote"/> now refuses that name.
	/// </summary>
	[HttpGet("remotes")]
	public async Task<ActionResult<IReadOnlyList<PackageRemoteRecord>>> GetRemotes()
	{
		var configured = await registry.GetPackageRemotesAsync();
		return Ok(configured
			.Where(r => !BundledPackages.IsCatalogueRemote(r.Name))
			.Prepend(CatalogueRemote)
			.ToList());
	}

	/// <summary>Adds or updates a configured remote.</summary>
	[HttpPost("remotes")]
	public async Task<IActionResult> UpsertRemote([FromBody] RemoteRequest request, [FromServices] IPackageOperationGate packageGate)
	{
		if (BundledPackages.IsCatalogueRemote(request.Name))
		{
			return Conflict($"'{BundledPackages.RemoteName}' is reserved for the packages shipped with this server.");
		}

		if (PluginUploadStore.IsUploadRemote(request.Name))
		{
			return Conflict($"'{PluginUploadStore.RemoteName}' is reserved for uploaded plugin packages.");
		}

		if (string.IsNullOrWhiteSpace(request.Name) || !Uri.TryCreate(request.Url, UriKind.Absolute, out _))
		{
			return BadRequest("A remote requires a name and a valid URL.");
		}

		if (!Enum.TryParse<PackageRemoteTrust>(request.Trust, ignoreCase: true, out var trust))
		{
			return BadRequest("Trust must be official, community, or unknown.");
		}

		await packageGate.RunAsync(async () =>
		{
			await registry.UpsertPackageRemoteAsync(new PackageRemoteRecord(request.Name.Trim(), request.Url.Trim(), trust,
				string.IsNullOrWhiteSpace(request.Branch) ? null : request.Branch.Trim()));
			return true;
		});
		return NoContent();
	}

	/// <summary>Removes a configured remote.</summary>
	[HttpDelete("remotes/{name}")]
	public async Task<IActionResult> DeleteRemote(string name, [FromServices] IPackageOperationGate packageGate)
	{
		if (BundledPackages.IsCatalogueRemote(name))
		{
			return Conflict(
				$"'{BundledPackages.RemoteName}' is the catalogue shipped with this server and cannot be removed.");
		}

		await packageGate.RunAsync(async () => { await registry.RemovePackageRemoteAsync(name); return true; });
		return NoContent();
	}

	/// <summary>Refreshes a remote's cache and returns its discovered packages with version tags.</summary>
	[HttpGet("remotes/{name}/browse")]
	public async Task<ActionResult<PackageRepoSnapshot>> Browse(string name, CancellationToken cancellationToken)
	{
		if (BundledPackages.IsCatalogueRemote(name))
		{
			return Ok(BrowseCatalogue());
		}

		if (await registry.GetPackageRemoteAsync(name) is not PackageRemoteRecord remote)
		{
			return NotFound($"No configured remote named '{name}'.");
		}

		return await source.RefreshAsync(remote, cancellationToken) switch
		{
			PackageRepoSnapshot ok => Ok(ok),
			Error<string> error => StatusCode(StatusCodes.Status502BadGateway, error.Value)
		};
	}

	/// <summary>
	/// Computes the review-screen payload for an install/upgrade: fetches the
	/// manifest at the requested version (or branch tip), plans against the
	/// live game, and renders highlighted Base/Live/New panes with dangerous-
	/// pattern flags. Read-only; re-run as configure answers arrive.
	/// </summary>
	[HttpPost("plan")]
	public async Task<ActionResult<PlanResponse>> Plan([FromBody] PlanRequest request,
		CancellationToken cancellationToken)
		=> await manifestResolver.ResolveAsync(new(request.Remote, request.Path, request.Version), false, cancellationToken) switch
		{
			ResolvedPackageManifest fetched => Ok(PackageReviewPresenter.For(fetched.Manifest, fetched.Source.Commit,
				await installer.PlanAsync(fetched.Manifest, request.ConfigureAnswers, cancellationToken),
				request.ConfigureAnswers ?? new Dictionary<string, string>(), fetched.Warnings)),
			PackageManifestResolutionFailure failure => ResolutionFailed(failure)
		};

	/// <summary>Applies a reviewed plan (decision 20.8: never automatic — this is the explicit confirmation).</summary>
	[HttpPost("apply")]
	public async Task<ActionResult<ApplyResponse>> Apply([FromBody] ApplyRequest request,
		CancellationToken cancellationToken)
		=> await manifestResolver.ResolveAsync(new(request.Remote, request.Path, request.Version), false, cancellationToken) switch
		{
			ResolvedPackageManifest fetched => await ApplyManifestAsync(request, fetched, cancellationToken),
			PackageManifestResolutionFailure failure => ResolutionFailed(failure)
		};

	private async Task<ActionResult<ApplyResponse>> ApplyManifestAsync(ApplyRequest request,
		ResolvedPackageManifest fetched, CancellationToken cancellationToken)
	{
		var manifest = fetched.Manifest;
		var remote = fetched.Remote;
		var applyRequest = new PackageApplyRequest(new(remote.Url, fetched.ApplyPath, fetched.Source.Commit, remote.Branch),
			request.ConfigureAnswers ?? new Dictionary<string, string>(), request.Decisions ?? [], request.KeepRevisions,
			request.AllowPluginCode, request.AllowPastMaximum, fetched.Readme);
		var outcome = await operations.RunAsync("apply", token => installer.ApplyAsync(manifest, applyRequest, token, fetched.Files),
			cancellationToken, async _ => new BackupProvenance("pre-package", User?.FindFirstValue(ClaimTypes.NameIdentifier),
				await registry.GetInstalledPackageAsync(manifest.Name) is InstalledPackageRecord ? "update" : "install",
				$"{manifest.Name} v{manifest.Version}"));
		if (outcome is PackageOperationRan<Result<PackageApplyResult>> { Result: PackageApplyResult }
			&& manifest.Kind is PackageKind.Plugin or PackageKind.Ui)
		{
			if (PluginUploadStore.IsUploadRemote(request.Remote)) uploads.Discard(request.Path);
			if (User?.FindFirstValue(ClaimTypes.NameIdentifier) is { } accountId)
				await audit.RecordPortalAsync(accountId, AuditActions.PluginInstall,
					new AuditTarget(AuditTargetKinds.Plugin, manifest.Name, manifest.Name),
					$"v{manifest.Version} from {(PluginUploadStore.IsUploadRemote(request.Remote) ? "an upload" : remote.Url)}", cancellationToken);
		}
		if (outcome is PackageOperationRan<Result<PackageApplyResult>> { Result: PackageApplyResult }
			&& PackageServerCompatibility.Check(manifest, engine.Current) is { Fit: PackageServerFit.AboveMaximum } past
			&& User?.FindFirstValue(ClaimTypes.NameIdentifier) is { } confirmedBy)
			await audit.RecordPortalAsync(confirmedBy, AuditActions.PackagePastMaximum,
				new AuditTarget(AuditTargetKinds.Package, manifest.Name, manifest.Name),
				$"v{manifest.Version} applied on SharpMUSH {past.Server}, past maximum_server {past.Maximum}", cancellationToken);
		return outcome switch
		{
			PackageOperationRan<Result<PackageApplyResult>> ran => Applied(ran.Result, ran.Backup),
			PackageOperationRefused refused => Unavailable(refused)
		};
	}

	private ObjectResult ResolutionFailed(PackageManifestResolutionFailure failure)
		=> StatusCode(failure.StatusCode, failure.Issues is null ? failure.Message : new { failure.Message, failure.Issues });

	private ActionResult<ApplyResponse> Applied(Result<PackageApplyResult> result, WorldBackup? backup) => result switch
	{
		PackageApplyResult ok => Ok(new ApplyResponse(ok.Revision, ok.CreatedObjects, WithBackupNote(ok.Notes, backup))),
		Error<string> error => BadRequest(error.Value)
	};

	/// <summary>
	/// The catalogue as a browse snapshot. An entry whose manifest does not parse is still listed,
	/// with null metadata, exactly as an unparsable package in a git remote would be — the browse
	/// screen should show that something is wrong rather than silently drop it.
	/// </summary>
	private PackageRepoSnapshot BrowseCatalogue()
	{
		var entries = BundledPackages.All
			.Select(descriptor => manifests.ParseManifest(BundledPackages.ManifestYaml(descriptor.PackageId)) switch
			{
				ParsedPackageManifest { Manifest: var manifest } => new PackageRepoEntry(descriptor.PackageId,
					manifest.Name, manifest.Version.ToString(), manifest.Description, []),
				PackageManifestFailure => new PackageRepoEntry(descriptor.PackageId, null, null, null, [])
			})
			.ToList();

		return new PackageRepoSnapshot(
			BundledPackages.RemoteName, BundledPackages.SourceRepo, BundledPackages.SourceCommit, entries);
	}

	/// <summary>
	/// A catalogue package's README: the one it ships, or one written from its manifest
	/// (<see cref="BundledPackages.Readme"/>). Null when <paramref name="path"/> names something
	/// this build does not ship. An empty path asks for the "repo root" README — the catalogue's
	/// own index.
	/// </summary>
	private string? CatalogueReadme(string? path)
	{
		var packageId = (path ?? "").Trim().Trim('/');
		if (packageId.Length == 0)
		{
			return string.Join('\n',
				"# Bundled with this server",
				"",
				"Packages embedded in this build. They install without a network connection or a",
				"configured remote. Those marked *installed at first boot* are already in every new",
				"game; the rest are available for you to install when you want them.",
				"",
				string.Join('\n', BundledPackages.All.Select(d =>
					$"- **{d.PackageId}**{(d.InstallAtFirstBoot ? " — installed at first boot" : "")}")));
		}

		if (BundledPackages.Find(packageId) is not BundledPackages.Descriptor descriptor)
		{
			return null;
		}

		return manifests.ParseManifest(BundledPackages.ManifestYaml(descriptor.PackageId)) switch
		{
			ParsedPackageManifest { Manifest: var manifest } => BundledPackages.Readme(descriptor, manifest),
			PackageManifestFailure => $"# {packageId}\n\nThis build's manifest for `{packageId}` could not be read."
		};
	}

	/// <summary>
	/// Update status for a package installed from the image. "An update is available" means what
	/// bootstrap means by it — this build ships a strictly newer version — so the answer here and
	/// what the next restart does cannot disagree. There are no tags to move and no branch to
	/// diverge, so the dev-channel and moved-tag signals are always false.
	/// </summary>
	private PackageUpdateInfo CatalogueUpdateInfo(InstalledPackageRecord installed)
	{
		// Installed from an older build's catalogue and no longer shipped: nothing to compare against.
		if (!BundledPackages.Contains(installed.Id))
		{
			return new PackageUpdateInfo(installed.Version, null, null, false, false, false);
		}

		// An unparsable embedded manifest is a build defect that BundledPackagesTests catches; here
		// it must not turn an update check into a 500.
		if (manifests.ParseManifest(BundledPackages.ManifestYaml(installed.Id)) is not ParsedPackageManifest parsed)
		{
			return new PackageUpdateInfo(installed.Version, null, null, false, false, false);
		}

		var shipped = parsed.Manifest.Version;
		return new PackageUpdateInfo(
			installed.Version,
			shipped.ToString(),
			BundledPackages.SourceCommit,
			DefaultPackagesBootstrapService.IsNewer(shipped, installed.Version),
			PathChangedAtHead: false,
			InstalledTagMoved: false);
	}

	/// <summary>
	/// Renders a README from a configured remote: the repo root, or a package
	/// directory (optionally at a release version).
	/// </summary>
	[HttpGet("remotes/{name}/readme")]
	public async Task<ActionResult<ReadmeResponse>> GetRemoteReadme(
		string name, [FromQuery] string? path, [FromQuery] string? version, CancellationToken cancellationToken)
	{
		// A bundled package without a README of its own gets one written from its manifest, rather
		// than a 404 in the browse screen's description pane.
		if (BundledPackages.IsCatalogueRemote(name))
		{
			var markdown = CatalogueReadme(path);
			return markdown is null
				? NotFound($"'{path}' is not shipped with this server.")
				: Ok(new ReadmeResponse(markdown, Markdown.RenderReadmeToHtml(markdown)));
		}

		if (await registry.GetPackageRemoteAsync(name) is not PackageRemoteRecord remote)
		{
			return NotFound($"No configured remote named '{name}'.");
		}

		return await source.GetReadmeAsync(remote, path ?? "", version, cancellationToken) switch
		{
			string markdown => Ok(new ReadmeResponse(markdown, Markdown.RenderReadmeToHtml(markdown))),
			Error<string> error => NotFound(error.Value)
		};
	}
}
