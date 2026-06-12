namespace SharpMUSH.Library.Models.Packages;

/// <summary>
/// The result of the plan phase (decisions 20.7, 20.15): a pure-data
/// description of everything an install/upgrade would do, computed read-only
/// and shown on the review screen. Never materialized in the game DB.
/// </summary>
/// <param name="PackageId">The package being planned.</param>
/// <param name="FromVersion">Installed version, or null for a fresh install.</param>
/// <param name="ToVersion">Version being applied.</param>
/// <param name="Kind">Install or upgrade (rollback plans are built from revision snapshots).</param>
/// <param name="Objects">Object-level actions, in manifest order then deletions.</param>
/// <param name="Attributes">Attribute-level actions with Base/Live/New values for the three-pane review.</param>
/// <param name="DependencyIssues">Unmet dependencies and conflict violations; any entry blocks apply.</param>
/// <param name="CommandCollisions">$command patterns that collide with other installed packages (warnings, decision 20.20).</param>
/// <param name="Notes">Free-form reviewer notes (recreated objects, contents warnings, unresolved-at-apply values).</param>
public sealed record PackageChangeset(
	string PackageId,
	string? FromVersion,
	string ToVersion,
	PackageRevisionKind Kind,
	IReadOnlyList<PackageObjectChange> Objects,
	IReadOnlyList<PackageAttributeChange> Attributes,
	IReadOnlyList<PackageDependencyIssue> DependencyIssues,
	IReadOnlyList<PackageCommandCollision> CommandCollisions,
	IReadOnlyList<string> Notes)
{
	/// <summary>True when dependency/conflict issues prevent applying at all.</summary>
	public bool IsBlocked => DependencyIssues.Count > 0;

	/// <summary>True when at least one attribute needs an admin decision.</summary>
	public bool HasConflicts => Attributes.Any(a => a.Action == PackageAttributeAction.Conflict);
}

/// <summary>Object-level classification.</summary>
public enum PackageObjectAction
{
	/// <summary>Object does not exist; create it.</summary>
	Create,

	/// <summary>An installed object's ref matches this object's <c>previous_refs</c>; keep the dbref, update the registry (decision 20.15).</summary>
	Rename,

	/// <summary>Object exists; only metadata (e.g. name) differs.</summary>
	UpdateMetadata,

	/// <summary>Object exists and matches.</summary>
	NoChange,

	/// <summary>Registry says installed but the object is gone from the live DB (destroyed out-of-band); recreate.</summary>
	RecreateMissing,

	/// <summary>Object was removed from the package; propose destroying it.</summary>
	Delete
}

/// <summary>One object-level action.</summary>
/// <param name="Ref">Manifest ref (current name; for deletes, the retired ref).</param>
/// <param name="Action">What the apply phase would do.</param>
/// <param name="Type">Object type from the manifest (or registry record for deletes).</param>
/// <param name="Name">In-game name from the manifest (or registry for deletes).</param>
/// <param name="Objid">Existing object identity, when the object already exists.</param>
/// <param name="RenamedFromRef">For renames, the installed ref being retired.</param>
/// <param name="MetadataDiffs">Human-readable metadata differences (e.g. <c>name: 'Old' -> 'New'</c>).</param>
public sealed record PackageObjectChange(
	string Ref,
	PackageObjectAction Action,
	PackageObjectType Type,
	string Name,
	string? Objid = null,
	string? RenamedFromRef = null,
	IReadOnlyList<string>? MetadataDiffs = null);

/// <summary>Attribute-level classification (the dpkg/ucf truth table plus delete and add/add cases, decisions 20.7/20.15).</summary>
public enum PackageAttributeAction
{
	/// <summary>Attribute does not exist on the target; set it.</summary>
	Create,

	/// <summary>User never touched it and the package changed it; take the new value silently.</summary>
	AutoUpgrade,

	/// <summary>User changed it and the package did not; keep the local value (also covers a preserved local deletion).</summary>
	KeepLocal,

	/// <summary>Base, live, and new all agree.</summary>
	NoChange,

	/// <summary>Unmanaged attribute already equals the incoming value; record a baseline without writing.</summary>
	Adopt,

	/// <summary>Removed from the package and locally unmodified; delete it.</summary>
	Delete,

	/// <summary>Removed from the package and already gone locally; just drop the baseline record.</summary>
	RemoveBaseline,

	/// <summary>Needs an admin decision; see <see cref="PackageConflictKind"/>.</summary>
	Conflict
}

/// <summary>Why an attribute needs an admin decision.</summary>
public enum PackageConflictKind
{
	/// <summary>Modified locally AND changed in the new version.</summary>
	ModifyModify,

	/// <summary>Modified locally but removed from the new version.</summary>
	ModifyDelete,

	/// <summary>Deleted locally but changed in the new version.</summary>
	DeleteModify,

	/// <summary>New to the package but already exists locally with different content.</summary>
	AddAdd
}

/// <summary>
/// One attribute-level action, carrying the three values the review UI's
/// Base/Live/New panes render (nulls = absent).
/// </summary>
/// <param name="TargetRef">Manifest ref of the target object, or its objid for non-package targets.</param>
/// <param name="Objid">Resolved target objid when the object already exists.</param>
/// <param name="Attribute">Attribute name.</param>
/// <param name="Action">Classification.</param>
/// <param name="Conflict">Set when <paramref name="Action"/> is <see cref="PackageAttributeAction.Conflict"/>.</param>
/// <param name="BaseValue">Baseline (as applied at last install/upgrade), or null.</param>
/// <param name="LiveValue">Current live value, or null when absent.</param>
/// <param name="NewValue">Incoming value with refs resolved as far as currently possible, or null for deletes.</param>
/// <param name="RequiresApplyResolution">True when the new value references objects that only get dbrefs at apply time.</param>
public sealed record PackageAttributeChange(
	string TargetRef,
	string? Objid,
	string Attribute,
	PackageAttributeAction Action,
	PackageConflictKind? Conflict = null,
	string? BaseValue = null,
	string? LiveValue = null,
	string? NewValue = null,
	bool RequiresApplyResolution = false);

/// <summary>A dependency or conflict violation that blocks apply (decisions 20.6, 20.20).</summary>
/// <param name="PackageId">The other package involved.</param>
/// <param name="Constraint">Declared constraint text (empty = any).</param>
/// <param name="InstalledVersion">Its installed version, or null when not installed.</param>
/// <param name="Source">The manifest's source hint, for "fetch it from here?" offers.</param>
/// <param name="IsConflict">True for a <c>conflicts:</c> violation, false for an unmet dependency.</param>
public sealed record PackageDependencyIssue(
	string PackageId,
	string Constraint,
	string? InstalledVersion,
	PackageSourceHint? Source,
	bool IsConflict);

/// <summary>A $command pattern collision with another installed package (warning, decision 20.20).</summary>
/// <param name="Pattern">The normalized command pattern (e.g. <c>+bbread *</c>).</param>
/// <param name="TargetRef">Manifest ref of the object carrying the new definition.</param>
/// <param name="Attribute">Attribute holding the new definition.</param>
/// <param name="OtherPackageId">Installed package already defining the pattern.</param>
/// <param name="OtherAttribute">Its attribute (on <paramref name="OtherObjid"/>).</param>
/// <param name="OtherObjid">Object carrying the existing definition.</param>
public sealed record PackageCommandCollision(
	string Pattern,
	string TargetRef,
	string Attribute,
	string OtherPackageId,
	string OtherAttribute,
	string OtherObjid);

/// <summary>
/// Read-only snapshot of the live game state the plan engine compares
/// against, gathered by the caller before planning. Keyed by objid.
/// </summary>
/// <param name="Objects">State per object of interest (installed objects + resolved external targets).</param>
public sealed record LivePackageState(IReadOnlyDictionary<string, LiveObjectState> Objects)
{
	public static LivePackageState Empty { get; } = new(new Dictionary<string, LiveObjectState>());
}

/// <summary>Live state of one object.</summary>
/// <param name="Objid">Stable object id.</param>
/// <param name="Exists">False when the object was destroyed out-of-band.</param>
/// <param name="Name">Current in-game name.</param>
/// <param name="Attributes">Current attribute values by name (case-insensitive keys expected).</param>
/// <param name="HasContents">True when the object contains things/players (delete-review warning).</param>
public sealed record LiveObjectState(
	string Objid,
	bool Exists,
	string Name,
	IReadOnlyDictionary<string, string> Attributes,
	bool HasContents = false);

/// <summary>
/// Everything the pure plan computation needs (decision 20.7). The caller —
/// the install orchestration — gathers registry records and the live
/// snapshot; the engine itself touches no services.
/// </summary>
/// <param name="Manifest">The parsed manifest being installed/upgraded.</param>
/// <param name="Installed">Registry record when already installed (upgrade), or null (install).</param>
/// <param name="InstalledObjects">This package's created-object records.</param>
/// <param name="Baselines">This package's managed-attribute baselines.</param>
/// <param name="AllInstalledPackages">Every installed package (dependency/conflict checks).</param>
/// <param name="OtherManagedAttributes">Other packages' managed attributes ($command collision scan).</param>
/// <param name="Live">Live game state snapshot.</param>
/// <param name="WellKnownObjids">Resolution map: well-known ref name → objid.</param>
/// <param name="ConfigureAnswers">Resolution map: configure key → value (objid for dbref-typed); may be partial before review.</param>
/// <param name="CrossPackageObjids">Resolution map: <c>pkg/ref</c> → objid for dependency-owned objects.</param>
public sealed record PackagePlanInputs(
	PackageManifest Manifest,
	InstalledPackageRecord? Installed,
	IReadOnlyList<PackageObjectRecord> InstalledObjects,
	IReadOnlyList<ManagedAttributeRecord> Baselines,
	IReadOnlyList<InstalledPackageRecord> AllInstalledPackages,
	IReadOnlyList<ManagedAttributeRecord> OtherManagedAttributes,
	LivePackageState Live,
	IReadOnlyDictionary<string, string> WellKnownObjids,
	IReadOnlyDictionary<string, string> ConfigureAnswers,
	IReadOnlyDictionary<string, string> CrossPackageObjids);
