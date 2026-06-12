namespace SharpMUSH.Library.Models.Packages;

/// <summary>
/// A parsed repo index (index.yaml) — the fast-discovery listing at the root
/// of a multi-package repo. When absent, repos are scanned for package.yaml
/// files instead.
/// </summary>
/// <param name="Name">Repo display name, if declared.</param>
/// <param name="Description">Repo description, if declared.</param>
/// <param name="Packages">Packages the repo declares, in index order.</param>
public sealed record PackageIndex(
	string? Name,
	string? Description,
	IReadOnlyList<PackageIndexEntry> Packages);

/// <summary>One package listed in a repo index.</summary>
/// <param name="Path">Directory path of the package relative to the repo root (e.g. <c>bbs/</c>).</param>
/// <param name="Name">Optional display name override; the package.yaml remains authoritative.</param>
/// <param name="PackageId">Optional package id, enabling browse and duplicate-id checks without parsing every manifest (decision 20.17).</param>
/// <param name="Version">Optional latest version, for cheap browse display.</param>
/// <param name="Description">Optional description, for cheap browse display.</param>
public sealed record PackageIndexEntry(
	string Path,
	string? Name,
	string? PackageId = null,
	PackageVersion? Version = null,
	string? Description = null);
