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
	IReadOnlyList<PackageStructureChange> Structure,
	IReadOnlyList<PackageDependencyIssue> DependencyIssues,
	IReadOnlyList<PackageCommandCollision> CommandCollisions,
	IReadOnlyList<string> Notes)
{
	/// <summary>True when dependency/conflict issues prevent applying at all.</summary>
	public bool IsBlocked => DependencyIssues.Count > 0;

	/// <summary>True when at least one attribute or structure element needs an admin decision.</summary>
	public bool HasConflicts =>
		Attributes.Any(a => a.Action == PackageAttributeAction.Conflict)
		|| Structure.Any(s => s.Action == PackageStructureAction.Conflict);
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
	Delete,

	/// <summary>Attach mode (decision 20.3): manage attributes on an existing object the package does not create or own.</summary>
	Attach
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

/// <summary>The kind of object-structure element a <see cref="PackageStructureChange"/> describes.</summary>
public enum PackageStructureKind
{
	/// <summary>An object flag (set-valued, binary presence).</summary>
	ObjectFlag,

	/// <summary>An object power (set-valued, binary presence).</summary>
	ObjectPower,

	/// <summary>A flag on a specific attribute (set-valued, binary presence; carries <see cref="PackageStructureChange.Attribute"/>).</summary>
	AttributeFlag,

	/// <summary>A lock (value-typed: a lock-type name mapped to a lock string; uses the full attribute conflict model).</summary>
	Lock
}

/// <summary>
/// Classification of one object-structure element under the same dpkg/ucf
/// three-way merge as attributes (decision 20.7), extended for full object
/// structure diffs: object flags, object powers, attribute flags, and locks.
/// Binary elements (flags/powers/attribute flags) resolve deterministically and
/// never conflict; value-typed locks can.
/// </summary>
public enum PackageStructureAction
{
	/// <summary>Newly declared (or, for locks, a changed value the package owns): set it.</summary>
	Add,

	/// <summary>Already present live and now package-managed: record a baseline without writing.</summary>
	Adopt,

	/// <summary>Base, live, and new agree: nothing to do.</summary>
	NoChange,

	/// <summary>Admin diverged locally and the package did not (removed a binary element it still declares, or edited a lock the package left unchanged): keep the live state (dpkg keep-local).</summary>
	KeepLocal,

	/// <summary>Removed from the package and still present live (locally unmodified): unset it.</summary>
	Remove,

	/// <summary>Removed from the package and already gone live: just drop the baseline element.</summary>
	RemoveBaseline,

	/// <summary>Value-typed (lock) divergence needing an admin decision; see <see cref="PackageConflictKind"/>.</summary>
	Conflict
}

/// <summary>
/// One object-structure-level action (flag/power/attribute-flag/lock add or
/// remove on upgrade). Binary elements leave the value panes null; locks carry
/// Base/Live/New lock strings for the three-pane review.
/// </summary>
/// <param name="TargetRef">Manifest ref of the target object, or its objid for non-package targets.</param>
/// <param name="Objid">Resolved target objid when the object already exists.</param>
/// <param name="Kind">Which structure element this describes.</param>
/// <param name="Element">Flag name, power name, or lock-type name.</param>
/// <param name="Action">Classification.</param>
/// <param name="Attribute">For <see cref="PackageStructureKind.AttributeFlag"/>, the attribute the flag lives on.</param>
/// <param name="Conflict">Set when <paramref name="Action"/> is <see cref="PackageStructureAction.Conflict"/> (locks only).</param>
/// <param name="BaseValue">Lock baseline string, or null (binary/absent).</param>
/// <param name="LiveValue">Current live lock string, or null.</param>
/// <param name="NewValue">Incoming lock string with refs resolved as far as possible, or null for removals/binary.</param>
/// <param name="RequiresApplyResolution">True when a lock's new value references objects that only get dbrefs at apply time.</param>
public sealed record PackageStructureChange(
	string TargetRef,
	string? Objid,
	PackageStructureKind Kind,
	string Element,
	PackageStructureAction Action,
	string? Attribute = null,
	PackageConflictKind? Conflict = null,
	string? BaseValue = null,
	string? LiveValue = null,
	string? NewValue = null,
	bool RequiresApplyResolution = false);

/// <summary>
/// The package-managed object structure baseline for one object (decision
/// 20.13, extended): the flags, powers, locks, and per-attribute flags the
/// package set at the last install/upgrade. Persisted as one JSON document per
/// (package, objid) so the three-way merge and rollback can reason offline.
/// </summary>
/// <param name="Flags">Object flag names the package set.</param>
/// <param name="Powers">Object power names the package set.</param>
/// <param name="Locks">Lock-type name → resolved lock string the package set.</param>
/// <param name="AttributeFlags">Attribute name → flag names the package set on it.</param>
public sealed record PackageStructureBaseline(
	IReadOnlyList<string> Flags,
	IReadOnlyList<string> Powers,
	IReadOnlyDictionary<string, string> Locks,
	IReadOnlyDictionary<string, IReadOnlyList<string>> AttributeFlags)
{
	public static PackageStructureBaseline Empty { get; } = new(
		[], [], new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
		new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase));
}

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
/// <param name="Flags">Current object flag names, or null when not gathered (treated as empty).</param>
/// <param name="Powers">Current object power names, or null when not gathered (treated as empty).</param>
/// <param name="Locks">Current lock-type → lock string, or null when not gathered (treated as empty).</param>
/// <param name="AttributeFlags">Current attribute name → flag names, or null when not gathered (treated as empty).</param>
public sealed record LiveObjectState(
	string Objid,
	bool Exists,
	string Name,
	IReadOnlyDictionary<string, string> Attributes,
	bool HasContents = false,
	IReadOnlyList<string>? Flags = null,
	IReadOnlyList<string>? Powers = null,
	IReadOnlyDictionary<string, string>? Locks = null,
	IReadOnlyDictionary<string, IReadOnlyList<string>>? AttributeFlags = null);

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
/// <param name="StructureBaselines">This package's managed object-structure baselines, keyed by objid (decision 20.13, extended for full structure diffs).</param>
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
	IReadOnlyDictionary<string, string> CrossPackageObjids,
	IReadOnlyDictionary<string, PackageStructureBaseline>? StructureBaselines = null);
