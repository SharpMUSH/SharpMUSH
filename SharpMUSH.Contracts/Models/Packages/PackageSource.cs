namespace SharpMUSH.Library.Models.Packages;

/// <summary>A released version of a package, derived from a git tag (decision 20.14).</summary>
/// <param name="Version">Semantic version parsed from the tag name.</param>
/// <param name="TagName">The full tag name (e.g. <c>who-where/v1.2.0</c>, or <c>v1.2.0</c> for root packages).</param>
/// <param name="Commit">The commit the tag points at.</param>
public sealed record PackageVersionTag(string Version, string TagName, string Commit);

/// <summary>One package discovered in a remote repo.</summary>
/// <param name="Path">Package directory relative to the repo root (empty string = repo root).</param>
/// <param name="PackageId">Package id from the manifest at HEAD, or null when unparsable.</param>
/// <param name="Version">Manifest version at HEAD (the dev channel), or null.</param>
/// <param name="Description">Manifest description at HEAD, or null.</param>
/// <param name="Versions">Released versions (tags), newest first.</param>
/// <param name="Title">Manifest <c>title:</c> at HEAD, or null.</param>
/// <param name="Category">Manifest <c>category:</c> at HEAD, or null.</param>
/// <param name="Subcategory">Manifest <c>subcategory:</c> at HEAD, or null.</param>
/// <param name="Contents">What the package ships at HEAD.</param>
/// <param name="Bridge">Whether the manifest at HEAD marks the package a bridge.</param>
/// <param name="DependsOn">The ids the manifest at HEAD depends on, in manifest order; empty when unparsable.</param>
public sealed record PackageRepoEntry(
	string Path,
	string? PackageId,
	string? Version,
	string? Description,
	IReadOnlyList<PackageVersionTag> Versions,
	string? Title = null,
	string? Category = null,
	string? Subcategory = null,
	PackageContents Contents = PackageContents.None,
	bool Bridge = false,
	IReadOnlyList<string>? DependsOn = null)
{
	/// <summary>The entry for a package whose manifest parsed.</summary>
	public static PackageRepoEntry From(string path, PackageManifest manifest, IReadOnlyList<PackageVersionTag> versions) =>
		new(path, manifest.Name, manifest.Version.ToString(), manifest.Description, versions,
			manifest.Title, manifest.Category, manifest.Subcategory, manifest.Contents, manifest.Bridge,
			[.. manifest.Dependencies.Select(d => d.PackageId)]);
}

/// <summary>The state of a remote repo after a refresh (clone or fetch).</summary>
/// <param name="RemoteName">The configured remote's name.</param>
/// <param name="Url">The repo URL.</param>
/// <param name="HeadCommit">Tip commit of the pinned (or default) branch.</param>
/// <param name="Packages">Discovered packages, ordered by path.</param>
public sealed record PackageRepoSnapshot(
	string RemoteName,
	string Url,
	string HeadCommit,
	IReadOnlyList<PackageRepoEntry> Packages);

/// <summary>A manifest read from a remote at a specific version or at the branch tip.</summary>
/// <param name="ManifestYaml">Raw package.yaml text (parse with <c>IPackageManifestService</c>).</param>
/// <param name="Commit">The commit the manifest was read from (recorded as installed_commit on apply).</param>
/// <param name="Version">The resolved version when read from a release tag, or null for the branch tip.</param>
public sealed record PackageManifestSource(string ManifestYaml, string Commit, string? Version);

/// <summary>Update status of an installed package against its remote (decision 20.14).</summary>
/// <param name="InstalledVersion">Currently installed version.</param>
/// <param name="LatestVersion">Newest released version on the remote, or null when untagged.</param>
/// <param name="LatestCommit">Commit of the newest release tag, or null.</param>
/// <param name="UpdateAvailable">True when a newer release tag exists.</param>
/// <param name="PathChangedAtHead">True when files under the package path changed between installed_commit and the branch tip (the dev-channel signal).</param>
/// <param name="InstalledTagMoved">TRUST WARNING: the release tag for the installed version no longer points at installed_commit.</param>
public sealed record PackageUpdateInfo(
	string InstalledVersion,
	string? LatestVersion,
	string? LatestCommit,
	bool UpdateAvailable,
	bool PathChangedAtHead,
	bool InstalledTagMoved);
