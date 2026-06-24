using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SharpMUSH.Library;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

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
	ISharpDatabase database,
	IPackageSourceService source,
	IPackageManifestService manifests,
	IPackageInstallService installer,
	IPackageAuthoringService authoring) : ControllerBase
{
	/// <summary>The canonical official repo, used when no official remote is configured yet.</summary>
	public const string DefaultOfficialRepoUrl = "https://github.com/SharpMUSH/SharpMUSH-Packages";

	private static readonly WikiMarkdigPipeline Markdown = new();

	private IPackageRegistryService Registry => (IPackageRegistryService)database;

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
		var remotes = await Registry.GetPackageRemotesAsync();
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
			result.Switch(
				directory =>
				{
					errors.AddRange(directory.Errors.Select(e => $"{official.Name}: {e}"));
					foreach (var listing in directory.Listings.Where(l => seenUrls.Add(l.Url)))
					{
						listings.Add(new CommunityRepoListingDto(
							listing, official.Name, configuredUrls.Contains(listing.Url)));
					}
				},
				error => errors.Add($"{official.Name}: {error.Value}"));
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

		var remotes = await Registry.GetPackageRemotesAsync();
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

		var readme = await source.GetReadmeAsync(remote, "", null, cancellationToken);
		return readme.Match<ActionResult<ReadmeResponse>>(
			markdown => Ok(new ReadmeResponse(markdown, Markdown.RenderToHtml(markdown))),
			error => NotFound(error.Value));
	}

	/// <summary>Scans selected live objects: attrs, flags, parents, and external dbrefs needing classification.</summary>
	[HttpPost("author/scan")]
	[Authorize]
	public async Task<ActionResult<PackageAuthoringScan>> AuthorScan(
		[FromBody] List<string> objids, CancellationToken cancellationToken)
	{
		var result = await authoring.ScanAsync(objids, cancellationToken);
		return result.Match<ActionResult<PackageAuthoringScan>>(
			scan => Ok(scan),
			error => BadRequest(error.Value));
	}

	/// <summary>Exports a classified selection as a validated package.yaml document.</summary>
	[HttpPost("author/export")]
	[Authorize]
	public async Task<IActionResult> AuthorExport(
		[FromBody] PackageAuthoringRequest request, CancellationToken cancellationToken)
	{
		var result = await authoring.ExportAsync(request, cancellationToken);
		return result.Match<IActionResult>(
			yaml => File(System.Text.Encoding.UTF8.GetBytes(yaml), "application/yaml", "package.yaml"),
			error => BadRequest(error.Value));
	}

	/// <summary>Lists installed packages with dashboard context.</summary>
	[HttpGet]
	[Authorize]
	public async Task<ActionResult<IReadOnlyList<InstalledPackageDto>>> GetInstalled()
	{
		var installed = await Registry.GetInstalledPackagesAsync();
		var result = new List<InstalledPackageDto>();
		foreach (var package in installed)
		{
			result.Add(new InstalledPackageDto(
				package,
				(await Registry.GetManagedAttributesAsync(package.Id)).Count,
				(await Registry.GetPackageObjectsAsync(package.Id)).Count,
				(await Registry.GetPackageDependentsAsync(package.Id)).Select(d => d.PackageId).ToList()));
		}

		return Ok(result);
	}

	/// <summary>Revision history for an installed package (snapshot payloads omitted).</summary>
	[HttpGet("{id}/revisions")]
	[Authorize]
	public async Task<ActionResult<IReadOnlyList<RevisionDto>>> GetRevisions(string id)
	{
		var revisions = await Registry.GetPackageRevisionsAsync(id);
		return Ok(revisions
			.Select(r => new RevisionDto(r.Revision, r.Kind.ToString().ToLowerInvariant(), r.Version, r.Commit, r.AppliedAt))
			.ToList());
	}

	/// <summary>Rolls back to a prior revision (recorded as a NEW revision, decision 20.13).</summary>
	[HttpPost("{id}/rollback/{revision:int}")]
	[Authorize]
	public async Task<ActionResult<PackageRollbackResult>> Rollback(string id, int revision, CancellationToken cancellationToken)
	{
		var result = await installer.RollbackAsync(id, revision, cancellationToken);
		return result.Match<ActionResult<PackageRollbackResult>>(
			ok => Ok(ok),
			error => BadRequest(error.Value));
	}

	/// <summary>Uninstalls a package; 409 when dependents exist and force is not set.</summary>
	[HttpDelete("{id}")]
	[Authorize]
	public async Task<IActionResult> Uninstall(string id, [FromQuery] bool force, CancellationToken cancellationToken)
	{
		var result = await installer.UninstallAsync(id, force, cancellationToken);
		return result.Match<IActionResult>(
			_ => NoContent(),
			error => Conflict(error.Value));
	}

	/// <summary>
	/// Update check for an installed package: newest release tag, dev-channel
	/// path changes, and the moved-tag trust warning (decision 20.14).
	/// </summary>
	[HttpGet("{id}/update")]
	[Authorize]
	public async Task<ActionResult<PackageUpdateInfo>> CheckForUpdate(string id, CancellationToken cancellationToken)
	{
		var installed = await Registry.GetInstalledPackageAsync(id);
		if (installed.IsT1)
		{
			return NotFound($"'{id}' is not installed.");
		}

		var remotes = await Registry.GetPackageRemotesAsync();
		var remote = remotes.FirstOrDefault(r =>
				string.Equals(r.Url, installed.AsT0.SourceRepo, StringComparison.OrdinalIgnoreCase))
			?? new PackageRemoteRecord(
				installed.AsT0.SourceRepo, installed.AsT0.SourceRepo,
				PackageRemoteTrust.Unknown, installed.AsT0.PinnedBranch);

		var result = await source.CheckForUpdateAsync(remote, installed.AsT0, cancellationToken);
		return result.Match<ActionResult<PackageUpdateInfo>>(
			info => Ok(info),
			error => StatusCode(StatusCodes.Status502BadGateway, error.Value));
	}

	/// <summary>Lists configured remotes.</summary>
	[HttpGet("remotes")]
	[Authorize]
	public async Task<ActionResult<IReadOnlyList<PackageRemoteRecord>>> GetRemotes() =>
		Ok(await Registry.GetPackageRemotesAsync());

	/// <summary>Adds or updates a configured remote.</summary>
	[HttpPost("remotes")]
	[Authorize]
	public async Task<IActionResult> UpsertRemote([FromBody] RemoteRequest request)
	{
		if (string.IsNullOrWhiteSpace(request.Name) || !Uri.TryCreate(request.Url, UriKind.Absolute, out _))
		{
			return BadRequest("A remote requires a name and a valid URL.");
		}

		if (!Enum.TryParse<PackageRemoteTrust>(request.Trust, ignoreCase: true, out var trust))
		{
			return BadRequest("Trust must be official, community, or unknown.");
		}

		await Registry.UpsertPackageRemoteAsync(new PackageRemoteRecord(
			request.Name.Trim(), request.Url.Trim(), trust,
			string.IsNullOrWhiteSpace(request.Branch) ? null : request.Branch.Trim()));
		return NoContent();
	}

	/// <summary>Removes a configured remote.</summary>
	[HttpDelete("remotes/{name}")]
	[Authorize]
	public async Task<IActionResult> DeleteRemote(string name)
	{
		await Registry.RemovePackageRemoteAsync(name);
		return NoContent();
	}

	/// <summary>Refreshes a remote's cache and returns its discovered packages with version tags.</summary>
	[HttpGet("remotes/{name}/browse")]
	[Authorize]
	public async Task<ActionResult<PackageRepoSnapshot>> Browse(string name, CancellationToken cancellationToken)
	{
		var remote = await Registry.GetPackageRemoteAsync(name);
		if (remote.IsT1)
		{
			return NotFound($"No configured remote named '{name}'.");
		}

		var snapshot = await source.RefreshAsync(remote.AsT0, cancellationToken);
		return snapshot.Match<ActionResult<PackageRepoSnapshot>>(
			ok => Ok(ok),
			error => StatusCode(StatusCodes.Status502BadGateway, error.Value));
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
	{
		var fetched = await FetchManifestAsync(request.Remote, request.Path, request.Version, cancellationToken);
		if (fetched.IsT1)
		{
			return fetched.AsT1;
		}

		var (manifest, warnings, manifestSource) = fetched.AsT0;
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
	{
		var fetched = await FetchManifestAsync(request.Remote, request.Path, request.Version, cancellationToken);
		if (fetched.IsT1)
		{
			return fetched.AsT1;
		}

		var (manifest, _, manifestSource) = fetched.AsT0;
		var remote = (await Registry.GetPackageRemoteAsync(request.Remote)).AsT0;

		// Managed packages (Phase 4) carry a compiled DLL alongside package.yaml;
		// resolve a binary reader over the same commit so the installer can verify
		// and deposit the bytes. Softcode/application packages need none.
		IManagedPackageBinarySource? binarySource = null;
		if (manifest.Kind == PackageKind.Managed)
		{
			var binary = await source.GetBinarySourceAsync(remote, request.Path, manifestSource.Commit, cancellationToken);
			if (binary.IsT1)
			{
				return BadRequest(binary.AsT1.Value);
			}

			binarySource = binary.AsT0;
		}

		var result = await installer.ApplyAsync(manifest, new PackageApplyRequest(
			new PackageApplySource(remote.Url, request.Path, manifestSource.Commit, remote.Branch),
			request.ConfigureAnswers ?? new Dictionary<string, string>(),
			request.Decisions ?? [],
			request.KeepRevisions,
			request.AllowManagedCode), cancellationToken, binarySource);

		return result.Match<ActionResult<ApplyResponse>>(
			ok => Ok(new ApplyResponse(ok.Revision, ok.CreatedObjects, ok.Notes)),
			error => BadRequest(error.Value));
	}

	private async Task<OneOf.OneOf<(PackageManifest Manifest, IReadOnlyList<string> Warnings, PackageManifestSource Source), ActionResult>>
		FetchManifestAsync(string remoteName, string path, string? version, CancellationToken cancellationToken)
	{
		var remote = await Registry.GetPackageRemoteAsync(remoteName);
		if (remote.IsT1)
		{
			return NotFound($"No configured remote named '{remoteName}'.");
		}

		var fetched = await source.GetManifestAsync(remote.AsT0, path, version, cancellationToken);
		if (fetched.IsT1)
		{
			return NotFound(fetched.AsT1.Value);
		}

		var parsed = manifests.ParseManifest(fetched.AsT0.ManifestYaml);
		if (parsed.IsT1)
		{
			return UnprocessableEntity(new
			{
				Message = "The manifest is invalid.",
				Issues = parsed.AsT1.Issues.Select(i => i.ToString()).ToList()
			});
		}

		return (parsed.AsT0.Manifest,
			parsed.AsT0.Warnings.Select(w => w.ToString()).ToList(),
			fetched.AsT0);
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
		var remote = await Registry.GetPackageRemoteAsync(name);
		if (remote.IsT1)
		{
			return NotFound($"No configured remote named '{name}'.");
		}

		var readme = await source.GetReadmeAsync(remote.AsT0, path ?? "", version, cancellationToken);
		return readme.Match<ActionResult<ReadmeResponse>>(
			markdown => Ok(new ReadmeResponse(markdown, Markdown.RenderToHtml(markdown))),
			error => NotFound(error.Value));
	}
}
