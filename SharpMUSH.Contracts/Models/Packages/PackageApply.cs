namespace SharpMUSH.Library.Models.Packages;

/// <summary>How the admin resolved one attribute conflict on the review screen.</summary>
public enum PackageConflictResolution
{
	/// <summary>Keep the live (locally modified) value; the baseline still advances to the package's value (dpkg semantics).</summary>
	KeepMine,

	/// <summary>Write the package's new value.</summary>
	TakeTheirs,

	/// <summary>Write an admin-edited merged value.</summary>
	UseCustom
}

/// <summary>One conflict resolution from the review screen.</summary>
/// <param name="TargetRef">The changeset's target ref (manifest ref, or objid for external targets).</param>
/// <param name="Attribute">Attribute name.</param>
/// <param name="Resolution">The choice.</param>
/// <param name="CustomValue">The merged value when <see cref="PackageConflictResolution.UseCustom"/>.</param>
public sealed record PackageConflictDecision(
	string TargetRef,
	string Attribute,
	PackageConflictResolution Resolution,
	string? CustomValue = null);

/// <summary>Where an apply came from — recorded in sys_packages for update/moved-tag detection (decision 20.14).</summary>
/// <param name="Repo">Git repo URL.</param>
/// <param name="Path">Package directory within the repo, or null for the root.</param>
/// <param name="Commit">Commit hash being applied.</param>
/// <param name="Branch">Pinned branch, or null.</param>
public sealed record PackageApplySource(string Repo, string? Path, string Commit, string? Branch);

/// <summary>Everything the admin supplies to turn a reviewed changeset into an apply.</summary>
/// <param name="Source">Provenance of the manifest being applied.</param>
/// <param name="ConfigureAnswers">Configure key → value (objid for dbref-typed params).</param>
/// <param name="ConflictDecisions">One decision per conflict in the changeset.</param>
/// <param name="KeepRevisions">Revision retention after this apply (decision 20.13; default 10).</param>
/// <param name="AllowManagedCode">
/// The Phase-4 trust gate for <see cref="PackageKind.Managed"/> packages: the
/// operator's explicit, per-apply opt-in to deposit and load arbitrary compiled
/// C# (which runs in <b>full server trust</b>, exactly like a manually-dropped
/// plugin — see docs/design/custom-widgets.md's trust model). A managed apply is
/// refused unless this is true <i>and</i> the server's managed-package allow-list
/// permits the package id. Ignored for softcode/application packages.
/// </param>
public sealed record PackageApplyRequest(
	PackageApplySource Source,
	IReadOnlyDictionary<string, string> ConfigureAnswers,
	IReadOnlyList<PackageConflictDecision> ConflictDecisions,
	int KeepRevisions = 10,
	bool AllowManagedCode = false);

/// <summary>Result of a successful apply.</summary>
/// <param name="Revision">The revision number this apply recorded.</param>
/// <param name="CreatedObjects">Manifest ref → objid for objects created by this apply.</param>
/// <param name="Notes">Reviewer-facing notes accumulated during apply (skipped flags, GC reminders).</param>
public sealed record PackageApplyResult(
	int Revision,
	IReadOnlyDictionary<string, string> CreatedObjects,
	IReadOnlyList<string> Notes);

/// <summary>Result of a rollback (decision 20.13: rollback applies an old snapshot as a NEW revision).</summary>
/// <param name="Revision">The new revision number recorded by the rollback.</param>
/// <param name="RestoredFromRevision">The snapshot that was restored.</param>
/// <param name="Notes">Anything that could not be restored exactly (e.g. destroyed objects).</param>
public sealed record PackageRollbackResult(int Revision, int RestoredFromRevision, IReadOnlyList<string> Notes);

/// <summary>
/// The JSON payload stored in <see cref="PackageRevisionRecord.ManifestSnapshotJson"/>:
/// the fully resolved state this apply left behind, sufficient to render
/// history and to roll back without the original repo.
/// </summary>
/// <param name="Version">Package version applied.</param>
/// <param name="Objects">Package objects after this apply (ref, objid, type).</param>
/// <param name="Attributes">Final effective value of every managed attribute after this apply.</param>
/// <param name="Structure">Final package-managed object structure (flags/powers/locks/attribute flags) per object after this apply.</param>
public sealed record PackageRevisionSnapshot(
	string Version,
	IReadOnlyList<PackageRevisionSnapshotObject> Objects,
	IReadOnlyList<PackageRevisionSnapshotAttribute> Attributes,
	IReadOnlyList<PackageRevisionSnapshotStructure>? Structure = null);

/// <summary>One object in a revision snapshot.</summary>
public sealed record PackageRevisionSnapshotObject(string Ref, string Objid, string Type);

/// <summary>One managed attribute's final effective value in a revision snapshot.</summary>
public sealed record PackageRevisionSnapshotAttribute(string Objid, string Attribute, string Value);

/// <summary>One object's final package-managed structure in a revision snapshot.</summary>
/// <param name="Objid">Object the structure lives on.</param>
/// <param name="Flags">Object flag names the package set.</param>
/// <param name="Powers">Object power names the package set.</param>
/// <param name="Locks">Lock-type name → resolved lock string the package set.</param>
/// <param name="AttributeFlags">Attribute name → flag names the package set on it.</param>
public sealed record PackageRevisionSnapshotStructure(
	string Objid,
	IReadOnlyList<string> Flags,
	IReadOnlyList<string> Powers,
	IReadOnlyDictionary<string, string> Locks,
	IReadOnlyDictionary<string, IReadOnlyList<string>> AttributeFlags);
