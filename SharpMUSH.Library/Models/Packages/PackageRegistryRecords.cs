namespace SharpMUSH.Library.Models.Packages;

/// <summary>
/// Registry record for an installed package (decision 20.3/20.18). Identity
/// is the flat package id bound to its source repo — installing the same id
/// from a different repo is rejected by the install flow.
/// </summary>
/// <param name="Id">Package id slug (e.g. <c>who-where</c>).</param>
/// <param name="Version">Installed semantic version, as written in the manifest.</param>
/// <param name="SourceRepo">Git repo URL the package was installed from.</param>
/// <param name="SourcePath">Package directory within the repo, or null for the repo root.</param>
/// <param name="InstalledCommit">Commit hash the install/upgrade was applied from (powers update + moved-tag detection).</param>
/// <param name="PinnedBranch">Branch the admin pinned for updates, or null for the remote default.</param>
/// <param name="InstalledAt">UTC time of the most recent apply.</param>
/// <param name="CurrentRevision">Monotonic revision number of the latest apply (see <see cref="PackageRevisionRecord"/>).</param>
/// <param name="DeployedFiles">
/// For a <see cref="PackageKind.Managed"/> package (Phase 4): the file names this
/// install deposited into <c>plugins/&lt;id&gt;/</c>, recorded so uninstall removes
/// exactly what it deposited. Empty for softcode/application packages. A simple
/// string list on the existing record — no separate collection.
/// </param>
public sealed record InstalledPackageRecord(
	string Id,
	string Version,
	string SourceRepo,
	string? SourcePath,
	string InstalledCommit,
	string? PinnedBranch,
	DateTimeOffset InstalledAt,
	int CurrentRevision,
	IReadOnlyList<string>? DeployedFiles = null);

/// <summary>An object created by a package (sys_package_objects).</summary>
/// <param name="PackageId">Owning package id.</param>
/// <param name="Ref">The manifest ref that produced the object (lowercase).</param>
/// <param name="Objid">Stable object id (<c>#dbref:created-secs</c>) of the created object.</param>
/// <param name="Type">Game object type name (thing/room/exit/player).</param>
public sealed record PackageObjectRecord(string PackageId, string Ref, string Objid, string Type);

/// <summary>
/// An attribute managed by a package (sys_managed_attributes). Per decision
/// 20.13 the full baseline value is stored — not just a hash — so the upgrade
/// review can render Base/Live/New panes and attempt merges offline.
/// </summary>
/// <param name="PackageId">Managing package (may differ from the object's creating package — cross-package attrs).</param>
/// <param name="Objid">Object the attribute lives on.</param>
/// <param name="Attribute">Attribute name (uppercase, may contain backtick branches).</param>
/// <param name="BaselineValue">Full attribute value as applied at the last install/upgrade (refs resolved).</param>
/// <param name="BaselineHash">SHA-256 of <paramref name="BaselineValue"/> — the cheap drift-detection index.</param>
/// <param name="BaselineVersion">Package version that set the baseline.</param>
public sealed record ManagedAttributeRecord(
	string PackageId,
	string Objid,
	string Attribute,
	string BaselineValue,
	string BaselineHash,
	string BaselineVersion);

/// <summary>
/// The object-structure baseline a package manages on one object
/// (sys_managed_structures): the flags, powers, locks, and per-attribute flags
/// it set at the last install/upgrade, serialized as one JSON document so the
/// full three-way structure merge and rollback can run offline (decision 20.13,
/// extended for full object structure diffs).
/// </summary>
/// <param name="PackageId">Managing package.</param>
/// <param name="Objid">Object the structure lives on.</param>
/// <param name="StructureJson">JSON of <see cref="PackageStructureBaseline"/> as applied at the last install/upgrade.</param>
/// <param name="BaselineVersion">Package version that set the baseline.</param>
public sealed record ManagedStructureRecord(
	string PackageId,
	string Objid,
	string StructureJson,
	string BaselineVersion);

/// <summary>A dependency edge between two installed packages (sys_package_depends).</summary>
/// <param name="PackageId">The depending package.</param>
/// <param name="DependsOnId">The package depended upon.</param>
/// <param name="Constraint">Version constraint text as declared (e.g. <c>&gt;=1.0 &lt;2.0</c>; empty = any).</param>
public sealed record PackageDependencyRecord(string PackageId, string DependsOnId, string Constraint);

/// <summary>Trust level of a configured package remote (decision 20.8) — server-side config, never self-declared by repos.</summary>
public enum PackageRemoteTrust
{
	Official,
	Community,
	Unknown
}

/// <summary>A configured package source repo (sys_remotes).</summary>
/// <param name="Name">Display name, unique.</param>
/// <param name="Url">Git repo URL.</param>
/// <param name="Trust">Trust level shown as a badge throughout the UI.</param>
/// <param name="Branch">Pinned branch, or null for the remote default.</param>
public sealed record PackageRemoteRecord(string Name, string Url, PackageRemoteTrust Trust, string? Branch);

/// <summary>What kind of apply produced a revision.</summary>
public enum PackageRevisionKind
{
	Install,
	Upgrade,
	Rollback
}

/// <summary>
/// One apply of a package (sys_package_revisions, decision 20.13 — the Helm
/// model). Stores everything needed to render history, re-answer nothing on
/// upgrade, and roll back: the fully resolved manifest, the admin's configure
/// answers, and the pre-apply values of whatever the apply overwrote.
/// Rolling back applies an old revision's snapshot as a NEW revision.
/// </summary>
/// <param name="PackageId">Package this revision belongs to.</param>
/// <param name="Revision">Monotonic revision number, starting at 1.</param>
/// <param name="Kind">Install, upgrade, or rollback.</param>
/// <param name="Version">Package version applied.</param>
/// <param name="Commit">Source commit applied from.</param>
/// <param name="ManifestSnapshotJson">JSON of the resolved manifest (refs → dbrefs, conflict resolutions).</param>
/// <param name="ConfigureAnswersJson">JSON map of configure key → admin-supplied value.</param>
/// <param name="PreApplyValuesJson">JSON of attribute/object state overwritten by this apply (undo data).</param>
/// <param name="AppliedAt">UTC time of the apply.</param>
public sealed record PackageRevisionRecord(
	string PackageId,
	int Revision,
	PackageRevisionKind Kind,
	string Version,
	string Commit,
	string ManifestSnapshotJson,
	string ConfigureAnswersJson,
	string PreApplyValuesJson,
	DateTimeOffset AppliedAt);
