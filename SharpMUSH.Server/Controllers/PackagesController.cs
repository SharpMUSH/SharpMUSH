using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SharpMUSH.Library;
using SharpMUSH.Library.API;
using SharpMUSH.Library.DiscriminatedUnions;
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
public class PackagesController(
	IPackageRegistryService registry,
	IPackageSourceService source,
	IPackageManifestService manifests,
	IPackageInstallService installer,
	IPackageAuthoringService authoring) : ControllerBase
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
	[Authorize]
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
	[Authorize]
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
			string markdown => Ok(new ReadmeResponse(markdown, Markdown.RenderToHtml(markdown))),
			Error<string> error => NotFound(error.Value)
		};
	}

	/// <summary>Scans selected live objects: attrs, flags, parents, and external dbrefs needing classification.</summary>
	[HttpPost("author/scan")]
	[Authorize]
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
	[Authorize]
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
	[Authorize]
	public async Task<ActionResult<IReadOnlyList<InstalledPackageDto>>> GetInstalled()
	{
		var installed = await registry.GetInstalledPackagesAsync();
		var result = new List<InstalledPackageDto>();
		foreach (var package in installed)
		{
			result.Add(new InstalledPackageDto(
				package,
				(await registry.GetManagedAttributesAsync(package.Id)).Count,
				(await registry.GetPackageObjectsAsync(package.Id)).Count,
				(await registry.GetPackageDependentsAsync(package.Id)).Select(d => d.PackageId).ToList()));
		}

		return Ok(result);
	}

	/// <summary>Revision history for an installed package (snapshot payloads omitted).</summary>
	[HttpGet("{id}/revisions")]
	[Authorize]
	public async Task<ActionResult<IReadOnlyList<RevisionDto>>> GetRevisions(string id)
	{
		var revisions = await registry.GetPackageRevisionsAsync(id);
		return Ok(revisions
			.Select(r => new RevisionDto(r.Revision, r.Kind.ToString().ToLowerInvariant(), r.Version, r.Commit, r.AppliedAt))
			.ToList());
	}

	/// <summary>Rolls back to a prior revision (recorded as a NEW revision, decision 20.13).</summary>
	[HttpPost("{id}/rollback/{revision:int}")]
	[Authorize]
	public async Task<ActionResult<PackageRollbackResult>> Rollback(string id, int revision, CancellationToken cancellationToken)
	{
		return await installer.RollbackAsync(id, revision, cancellationToken) switch
		{
			PackageRollbackResult ok => Ok(ok),
			Error<string> error => BadRequest(error.Value)
		};
	}

	/// <summary>Uninstalls a package; 409 when dependents exist and force is not set.</summary>
	[HttpDelete("{id}")]
	[Authorize]
	public async Task<IActionResult> Uninstall(string id, [FromQuery] bool force, CancellationToken cancellationToken)
	{
		return await installer.UninstallAsync(id, force, cancellationToken) switch
		{
			Success => NoContent(),
			Error<string> error => Conflict(error.Value)
		};
	}

	/// <summary>
	/// Update check for an installed package: newest release tag, dev-channel
	/// path changes, and the moved-tag trust warning (decision 20.14).
	/// </summary>
	[HttpGet("{id}/update")]
	[Authorize]
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
	[Authorize]
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
	[Authorize]
	public async Task<IActionResult> UpsertRemote([FromBody] RemoteRequest request)
	{
		if (BundledPackages.IsCatalogueRemote(request.Name))
		{
			return Conflict($"'{BundledPackages.RemoteName}' is reserved for the packages shipped with this server.");
		}

		if (string.IsNullOrWhiteSpace(request.Name) || !Uri.TryCreate(request.Url, UriKind.Absolute, out _))
		{
			return BadRequest("A remote requires a name and a valid URL.");
		}

		if (!Enum.TryParse<PackageRemoteTrust>(request.Trust, ignoreCase: true, out var trust))
		{
			return BadRequest("Trust must be official, community, or unknown.");
		}

		await registry.UpsertPackageRemoteAsync(new PackageRemoteRecord(
			request.Name.Trim(), request.Url.Trim(), trust,
			string.IsNullOrWhiteSpace(request.Branch) ? null : request.Branch.Trim()));
		return NoContent();
	}

	/// <summary>Removes a configured remote.</summary>
	[HttpDelete("remotes/{name}")]
	[Authorize]
	public async Task<IActionResult> DeleteRemote(string name)
	{
		if (BundledPackages.IsCatalogueRemote(name))
		{
			return Conflict(
				$"'{BundledPackages.RemoteName}' is the catalogue shipped with this server and cannot be removed.");
		}

		await registry.RemovePackageRemoteAsync(name);
		return NoContent();
	}

	/// <summary>Refreshes a remote's cache and returns its discovered packages with version tags.</summary>
	[HttpGet("remotes/{name}/browse")]
	[Authorize]
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
	[Authorize]
	public async Task<ActionResult<PlanResponse>> Plan([FromBody] PlanRequest request, CancellationToken cancellationToken)
		=> await FetchManifestAsync(request.Remote, request.Path, request.Version, cancellationToken) switch
		{
			FetchedManifest fetched => await PlanManifestAsync(request, fetched, cancellationToken),
			ActionResult response => response,
		};

	/// <summary>Plans a fetched manifest against the live game and renders the review panes.</summary>
	private async Task<ActionResult<PlanResponse>> PlanManifestAsync(
		PlanRequest request, FetchedManifest fetched, CancellationToken cancellationToken)
	{
		var (manifest, warnings, manifestSource) = fetched;

		var answers = request.ConfigureAnswers ?? new Dictionary<string, string>();
		var changeset = await installer.PlanAsync(manifest, answers, cancellationToken);

		var configure = manifest.Configure.Values
			.Select(c => new ConfigurePromptDto(
				c.Key, c.Label, c.Type.ToString().ToLowerInvariant(), c.Default,
				answers.ContainsKey(c.Key) || c.Default is not null))
			.ToList();

		var renders = changeset.Attributes
			.Select(a => new AttributeRenderDto(
				a.TargetRef, a.Attribute,
				a.BaseValue is null ? null : MushcodeHighlighter.ToHtml(a.BaseValue),
				a.LiveValue is null ? null : MushcodeHighlighter.ToHtml(a.LiveValue),
				a.NewValue is null ? null : MushcodeHighlighter.ToHtml(a.NewValue),
				MushcodeHighlighter.FindDangerousPatterns(a.NewValue ?? "")))
			.ToList();

		return Ok(new PlanResponse(
			manifest.Name, manifest.Version.ToString(), manifestSource.Commit,
			changeset, configure, renders, warnings));
	}

	/// <summary>Applies a reviewed plan (decision 20.8: never automatic — this is the explicit confirmation).</summary>
	[HttpPost("apply")]
	[Authorize]
	public async Task<ActionResult<ApplyResponse>> Apply([FromBody] ApplyRequest request, CancellationToken cancellationToken)
		=> await FetchManifestAsync(request.Remote, request.Path, request.Version, cancellationToken) switch
		{
			FetchedManifest fetched => await ApplyManifestAsync(request, fetched, cancellationToken),
			ActionResult response => response,
		};

	/// <summary>Installs a fetched manifest from the remote it was fetched from.</summary>
	private async Task<ActionResult<ApplyResponse>> ApplyManifestAsync(
		ApplyRequest request, FetchedManifest fetched, CancellationToken cancellationToken)
	{
		var (manifest, _, manifestSource) = fetched;

		// FetchManifestAsync looked the remote up already, but it can be removed in between.
		var isCatalogue = BundledPackages.IsCatalogueRemote(request.Remote);
		PackageRemoteRecord remote;
		if (isCatalogue)
		{
			remote = CatalogueRemote;
		}
		else if (await registry.GetPackageRemoteAsync(request.Remote) is PackageRemoteRecord configured)
		{
			remote = configured;
		}
		else
		{
			return NotFound($"No configured remote named '{request.Remote}'.");
		}

		// Managed packages (Phase 4) carry a compiled DLL alongside package.yaml;
		// resolve a binary reader over the same commit so the installer can verify
		// and deposit the bytes. Softcode/application packages need none.
		IManagedPackageBinarySource? binarySource = null;
		if (manifest.Kind == PackageKind.Managed && isCatalogue)
		{
			// The catalogue embeds manifests, not DLLs: there is nothing to read the binaries out
			// of. No bundled package is managed today, and this refuses rather than handing the
			// installer a git source that would try to clone "bundled:sharpmush".
			return BadRequest("A managed package cannot be installed from the bundled catalogue.");
		}

		if (manifest.Kind == PackageKind.Managed)
		{
			switch (await source.GetBinarySourceAsync(remote, request.Path, manifestSource.Commit, cancellationToken))
			{
				case Error<string> error:
					return BadRequest(error.Value);
				case IManagedPackageBinarySource binary:
					binarySource = binary;
					break;
			}
		}

		// A catalogue install records the package id as its path, which is what first-boot bootstrap
		// writes. Normalizing here (rather than trusting whatever path the client echoed back) keeps
		// the two routes producing one identity, so update checks and uninstall cannot tell them apart.
		var applyPath = isCatalogue ? manifest.Name : request.Path;

		var result = await installer.ApplyAsync(manifest, new PackageApplyRequest(
			new PackageApplySource(remote.Url, applyPath, manifestSource.Commit, remote.Branch),
			request.ConfigureAnswers ?? new Dictionary<string, string>(),
			request.Decisions ?? [],
			request.KeepRevisions,
			request.AllowManagedCode), cancellationToken, binarySource);

		return result switch
		{
			PackageApplyResult ok => Ok(new ApplyResponse(ok.Revision, ok.CreatedObjects, ok.Notes)),
			Error<string> error => BadRequest(error.Value)
		};
	}

	/// <summary>A parsed manifest, the warnings its parse raised, and where it was read from.</summary>
	private readonly record struct FetchedManifest(
		PackageManifest Manifest, IReadOnlyList<string> Warnings, PackageManifestSource Source);

	private async Task<ValueOrResponse<FetchedManifest>> FetchManifestAsync(
		string remoteName, string path, string? version, CancellationToken cancellationToken)
	{
		var isCatalogue = BundledPackages.IsCatalogueRemote(remoteName);

		ValueOrResponse<PackageManifestSource> fetched;
		if (isCatalogue)
		{
			fetched = FetchCatalogueManifest(path);
		}
		else
		{
			if (await registry.GetPackageRemoteAsync(remoteName) is not PackageRemoteRecord remote)
			{
				return NotFound($"No configured remote named '{remoteName}'.");
			}

			fetched = await source.GetManifestAsync(remote, path, version, cancellationToken) switch
			{
				PackageManifestSource fromRemote => fromRemote,
				Error<string> error => NotFound(error.Value)
			};
		}

		return fetched switch
		{
			PackageManifestSource manifestSource => ParseFetchedManifest(manifestSource, isCatalogue, version),
			ActionResult response => response,
		};
	}

	/// <summary>Parses a fetched manifest, answering with its issues when it does not parse.</summary>
	private ValueOrResponse<FetchedManifest> ParseFetchedManifest(
		PackageManifestSource manifestSource, bool isCatalogue, string? version)
		=> manifests.ParseManifest(manifestSource.ManifestYaml) switch
		{
			ParsedPackageManifest parsed => HonourRequestedVersion(parsed, manifestSource, isCatalogue, version),
			PackageManifestFailure failure => UnprocessableEntity(new
			{
				Message = "The manifest is invalid.",
				Issues = failure.Issues.Select(i => i.ToString()).ToList()
			}),
		};

	/// <summary>The parsed manifest, unless the catalogue cannot serve the version that was asked for.</summary>
	private ValueOrResponse<FetchedManifest> HonourRequestedVersion(
		ParsedPackageManifest parsed, PackageManifestSource manifestSource, bool isCatalogue, string? version)
	{
		// The image ships exactly one version of each catalogue package, so an explicit version
		// request can only be honoured when it names that one. Serving the shipped manifest anyway
		// would install something other than what was asked for.
		var shipped = parsed.Manifest.Version.ToString();
		if (isCatalogue && !string.IsNullOrWhiteSpace(version) &&
			!string.Equals(version, shipped, StringComparison.OrdinalIgnoreCase))
		{
			return NotFound(
				$"This server ships {parsed.Manifest.Name} v{shipped}; the catalogue has no other versions.");
		}

		return new FetchedManifest(
			parsed.Manifest,
			parsed.Warnings.Select(w => w.ToString()).ToList(),
			manifestSource);
	}

	/// <summary>
	/// Reads a catalogue manifest out of the server assembly. <paramref name="path"/> is the
	/// package id (the catalogue has no directories); a trailing slash is tolerated because the
	/// browse entries of a git remote carry one and the UI passes back whatever it was given.
	/// </summary>
	private ValueOrResponse<PackageManifestSource> FetchCatalogueManifest(string path)
	{
		var packageId = (path ?? "").Trim().Trim('/');
		if (!BundledPackages.Contains(packageId))
		{
			return NotFound($"'{packageId}' is not shipped with this server.");
		}

		// Version stays null: the catalogue is a dev channel of one, not a release tag.
		return new PackageManifestSource(
			BundledPackages.ManifestYaml(packageId), BundledPackages.SourceCommit, null);
	}

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
	/// A README synthesized from a catalogue manifest: the package's own description, what it
	/// creates, and whether a new game gets it. Null when <paramref name="path"/> names something
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

		if (!BundledPackages.Contains(packageId))
		{
			return null;
		}

		if (manifests.ParseManifest(BundledPackages.ManifestYaml(packageId)) is not ParsedPackageManifest { Manifest: var manifest })
		{
			return $"# {packageId}\n\nThis build's manifest for `{packageId}` could not be read.";
		}

		var descriptor = BundledPackages.All.Single(d =>
			string.Equals(d.PackageId, packageId, StringComparison.OrdinalIgnoreCase));

		var lines = new List<string>
		{
			$"# {manifest.Name} {manifest.Version}",
			"",
			manifest.Description,
			"",
			descriptor.InstallAtFirstBoot
				? "Installed at first boot — every new game has this."
				: "Shipped with this server, not installed. Installing it is your call."
		};

		if (manifest.Dependencies.Count > 0)
		{
			lines.Add("");
			lines.Add("## Requires");
			lines.AddRange(manifest.Dependencies.Select(d => $"- `{d.PackageId}` {d.Constraint}"));
		}

		if (manifest.Objects.Count > 0)
		{
			lines.Add("");
			lines.Add("## Creates");
			lines.AddRange(manifest.Objects.Select(o => $"- {o.Name ?? o.Ref} (`{o.Type}`)"));
		}

		return string.Join('\n', lines);
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
	[Authorize]
	public async Task<ActionResult<ReadmeResponse>> GetRemoteReadme(
		string name, [FromQuery] string? path, [FromQuery] string? version, CancellationToken cancellationToken)
	{
		// The image embeds manifests, not READMEs. Rather than 404 the browse screen's description
		// pane for every bundled package, render what the manifest itself says.
		if (BundledPackages.IsCatalogueRemote(name))
		{
			var markdown = CatalogueReadme(path);
			return markdown is null
				? NotFound($"'{path}' is not shipped with this server.")
				: Ok(new ReadmeResponse(markdown, Markdown.RenderToHtml(markdown)));
		}

		if (await registry.GetPackageRemoteAsync(name) is not PackageRemoteRecord remote)
		{
			return NotFound($"No configured remote named '{name}'.");
		}

		return await source.GetReadmeAsync(remote, path ?? "", version, cancellationToken) switch
		{
			string markdown => Ok(new ReadmeResponse(markdown, Markdown.RenderToHtml(markdown))),
			Error<string> error => NotFound(error.Value)
		};
	}
}
