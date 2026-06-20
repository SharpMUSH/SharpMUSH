using SharpMUSH.Library.Models.Packages;

namespace SharpMUSH.Library.API;

/// <summary>
/// API response for the curated community-repo directory: listings aggregated
/// from every official remote's <c>community/</c> folder.
/// </summary>
/// <param name="Listings">Accepted community repos with their providing official source.</param>
/// <param name="Errors">Per-file parse errors found while reading listing directories.</param>
public sealed record CommunityReposResponse(
	IReadOnlyList<CommunityRepoListingDto> Listings,
	IReadOnlyList<string> Errors);

/// <summary>One community repo listing plus where it was accepted.</summary>
/// <param name="Listing">The listing itself.</param>
/// <param name="AcceptedBy">Name of the official remote whose community/ folder lists it.</param>
/// <param name="AlreadyConfigured">True when a configured remote already points at this URL.</param>
public sealed record CommunityRepoListingDto(
	CommunityRepoListing Listing,
	string AcceptedBy,
	bool AlreadyConfigured);

/// <summary>A README fetched from a remote, raw and rendered.</summary>
/// <param name="Markdown">Raw markdown source.</param>
/// <param name="Html">Sanitized HTML (raw HTML in the source is stripped).</param>
public sealed record ReadmeResponse(string Markdown, string Html);

/// <summary>Request body for adding/updating a configured remote.</summary>
public sealed record RemoteRequest(string Name, string Url, string Trust, string? Branch);

/// <summary>One installed package with dashboard context.</summary>
/// <param name="Package">The registry record.</param>
/// <param name="ManagedAttributeCount">How many attributes the package manages.</param>
/// <param name="ObjectCount">How many objects it created.</param>
/// <param name="Dependents">Installed packages that depend on it (uninstall blocking).</param>
public sealed record InstalledPackageDto(
	InstalledPackageRecord Package,
	int ManagedAttributeCount,
	int ObjectCount,
	IReadOnlyList<string> Dependents);

/// <summary>One revision in a package's history (snapshot payloads omitted).</summary>
public sealed record RevisionDto(int Revision, string Kind, string Version, string Commit, DateTimeOffset AppliedAt);

/// <summary>Request body for planning an install/upgrade from a remote.</summary>
/// <param name="Remote">Configured remote name.</param>
/// <param name="Path">Package directory in the repo (empty = root).</param>
/// <param name="Version">Release version, or null for the branch tip (dev channel).</param>
/// <param name="ConfigureAnswers">Configure answers gathered so far (may be partial).</param>
public sealed record PlanRequest(
	string Remote,
	string Path,
	string? Version,
	Dictionary<string, string>? ConfigureAnswers);

/// <summary>Request body for applying a reviewed plan.</summary>
/// <param name="AllowManagedCode">
/// Phase-4 trust opt-in for <c>kind: managed</c> packages: the operator's explicit
/// confirmation that they trust this package to deposit and run arbitrary compiled
/// C# in full server trust. Required (alongside the server allow-list) for a
/// managed install; ignored for softcode/application packages.
/// </param>
public sealed record ApplyRequest(
	string Remote,
	string Path,
	string? Version,
	Dictionary<string, string>? ConfigureAnswers,
	List<PackageConflictDecision>? Decisions,
	int KeepRevisions = 10,
	bool AllowManagedCode = false);

/// <summary>A configure prompt for the review screen.</summary>
public sealed record ConfigurePromptDto(string Key, string Label, string Type, string? Default, bool Answered);

/// <summary>
/// Highlighted HTML for one attribute change's review panes, plus advisory
/// dangerous-pattern flags (decision 20.8 — visual alerts, never blockers).
/// </summary>
public sealed record AttributeRenderDto(
	string TargetRef,
	string Attribute,
	string? BaseHtml,
	string? LiveHtml,
	string? NewHtml,
	IReadOnlyList<string> DangerousPatterns);

/// <summary>The review screen payload: changeset plus rendering aids.</summary>
/// <param name="PackageId">Package being planned.</param>
/// <param name="Version">Resolved version (or manifest version at the tip).</param>
/// <param name="Commit">Commit the manifest was read from (becomes installed_commit on apply).</param>
/// <param name="Changeset">The plan engine's classification.</param>
/// <param name="Configure">Configure prompts with answered-state.</param>
/// <param name="Renders">Per-attribute highlighted Base/Live/New panes + danger flags.</param>
/// <param name="ManifestWarnings">Non-blocking manifest parse warnings.</param>
public sealed record PlanResponse(
	string PackageId,
	string Version,
	string Commit,
	PackageChangeset Changeset,
	IReadOnlyList<ConfigurePromptDto> Configure,
	IReadOnlyList<AttributeRenderDto> Renders,
	IReadOnlyList<string> ManifestWarnings);

/// <summary>Result of a successful apply, for the UI.</summary>
public sealed record ApplyResponse(
	int Revision,
	IReadOnlyDictionary<string, string> CreatedObjects,
	IReadOnlyList<string> Notes);
