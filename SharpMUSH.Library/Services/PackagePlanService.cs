using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <summary>
/// Pure changeset computation for package installs and upgrades
/// (decisions 20.7, 20.15, 20.20). See <see cref="IPackagePlanService"/>.
/// </summary>
public class PackagePlanService : IPackagePlanService
{
	public PackageChangeset ComputeChangeset(PackagePlanInputs inputs)
	{
		var manifest = inputs.Manifest;
		var notes = new List<string>();

		var dependencyIssues = CheckDependenciesAndConflicts(inputs);
		var (objects, objidByRef, deletedObjids) = ClassifyObjects(inputs, notes);
		var attributes = ClassifyAttributes(inputs, objidByRef, deletedObjids, notes);
		var structure = ClassifyStructure(inputs, objidByRef, deletedObjids, notes);
		var collisions = DetectCommandCollisions(inputs);

		// Application packages (kind: application) carry no objects; surface the
		// portal registration the apply will perform so the review is not blank.
		if (manifest is { Kind: PackageKind.Application, Application: { } app })
		{
			var verb = inputs.Installed is null ? "Registers" : "Updates";
			notes.Add($"{verb} application '{app.Slug}' ({app.Kind}) at /apps/{app.Slug}, served from '{app.SchemaUrl}'.");
		}

		return new PackageChangeset(
			manifest.Name,
			inputs.Installed?.Version,
			manifest.Version.ToString(),
			inputs.Installed is null ? PackageRevisionKind.Install : PackageRevisionKind.Upgrade,
			objects,
			attributes,
			structure,
			dependencyIssues,
			collisions,
			notes);
	}

	// ── Dependencies & conflicts (decisions 20.6, 20.20) ───────────────────

	private static List<PackageDependencyIssue> CheckDependenciesAndConflicts(PackagePlanInputs inputs)
	{
		var issues = new List<PackageDependencyIssue>();
		var installedById = inputs.AllInstalledPackages.ToDictionary(p => p.Id, StringComparer.Ordinal);

		foreach (var dependency in inputs.Manifest.Dependencies)
		{
			if (!installedById.TryGetValue(dependency.PackageId, out var installed))
			{
				issues.Add(new PackageDependencyIssue(
					dependency.PackageId, dependency.Constraint.ToString(), null, dependency.Source, IsConflict: false));
				continue;
			}

			if (!PackageVersion.TryParse(installed.Version, out var installedVersion)
				|| !dependency.Constraint.IsSatisfiedBy(installedVersion, includePrereleases: true))
			{
				issues.Add(new PackageDependencyIssue(
					dependency.PackageId, dependency.Constraint.ToString(), installed.Version, dependency.Source,
					IsConflict: false));
			}
		}

		foreach (var conflict in inputs.Manifest.Conflicts)
		{
			if (installedById.TryGetValue(conflict.PackageId, out var installed)
				&& PackageVersion.TryParse(installed.Version, out var installedVersion)
				&& conflict.Constraint.IsSatisfiedBy(installedVersion, includePrereleases: true))
			{
				issues.Add(new PackageDependencyIssue(
					conflict.PackageId, conflict.Constraint.ToString(), installed.Version, null, IsConflict: true));
			}
		}

		return issues;
	}

	// ── Objects (decision 20.15: renames never become destroy+create) ──────

	private static string? ResolveTargetObjid(PackageRef target, PackagePlanInputs inputs) => target switch
	{
		{ Kind: PackageRefKind.WellKnown } => inputs.WellKnownObjids.GetValueOrDefault(target.Name),
		{ Kind: PackageRefKind.Configure } => inputs.ConfigureAnswers.GetValueOrDefault(target.Name),
		{ Kind: PackageRefKind.Internal, Package: not null } =>
			inputs.CrossPackageObjids.GetValueOrDefault($"{target.Package}/{target.Name}"),
		_ => null
	};

	private static (List<PackageObjectChange> Changes, Dictionary<string, string> ObjidByRef, HashSet<string> DeletedObjids)
		ClassifyObjects(PackagePlanInputs inputs, List<string> notes)
	{
		var changes = new List<PackageObjectChange>();
		var objidByRef = new Dictionary<string, string>(StringComparer.Ordinal);
		var consumedInstalledRefs = new HashSet<string>(StringComparer.Ordinal);
		var installedByRef = inputs.InstalledObjects.ToDictionary(o => o.Ref, StringComparer.Ordinal);

		foreach (var obj in inputs.Manifest.Objects)
		{
			// Attach mode (decision 20.3): the target object already exists; we
			// only manage its attributes — never create, restructure, or delete it.
			if (obj.Target is not null)
			{
				var targetObjid = ResolveTargetObjid(obj.Target, inputs);
				if (targetObjid is null)
				{
					notes.Add($"Attach target {obj.Target} for {{{{{obj.Ref}}}}} is not resolved yet (answer its configure prompt before applying).");
					changes.Add(new PackageObjectChange(obj.Ref, PackageObjectAction.Attach, obj.Type, obj.Target.ToString()));
					continue;
				}

				var targetLive = inputs.Live.Objects.GetValueOrDefault(targetObjid);
				if (targetLive is null || !targetLive.Exists)
				{
					notes.Add($"Attach target {obj.Target} ({targetObjid}) for {{{{{obj.Ref}}}}} does not exist on this game; it cannot be created.");
				}

				objidByRef[obj.Ref] = targetObjid;
				changes.Add(new PackageObjectChange(
					obj.Ref, PackageObjectAction.Attach, obj.Type, targetLive?.Name ?? obj.Target.ToString(), targetObjid));
				continue;
			}

			if (installedByRef.TryGetValue(obj.Ref, out var installed))
			{
				consumedInstalledRefs.Add(obj.Ref);
				if (!inputs.Live.Objects.TryGetValue(installed.Objid, out var live) || !live.Exists)
				{
					notes.Add($"Object {{{{{obj.Ref}}}}} ({installed.Objid}) was destroyed outside the package manager; it will be recreated.");
					changes.Add(new PackageObjectChange(obj.Ref, PackageObjectAction.RecreateMissing, obj.Type, obj.Name));
					continue;
				}

				objidByRef[obj.Ref] = installed.Objid;
				var diffs = new List<string>();
				if (!string.Equals(live.Name, obj.Name, StringComparison.Ordinal))
				{
					diffs.Add($"name: '{live.Name}' -> '{obj.Name}'");
				}

				changes.Add(new PackageObjectChange(
					obj.Ref,
					diffs.Count > 0 ? PackageObjectAction.UpdateMetadata : PackageObjectAction.NoChange,
					obj.Type, obj.Name, installed.Objid, MetadataDiffs: diffs));
				continue;
			}

			var renamedFrom = obj.PreviousRefs.FirstOrDefault(installedByRef.ContainsKey);
			if (renamedFrom is not null)
			{
				var old = installedByRef[renamedFrom];
				consumedInstalledRefs.Add(renamedFrom);
				objidByRef[obj.Ref] = old.Objid;
				changes.Add(new PackageObjectChange(
					obj.Ref, PackageObjectAction.Rename, obj.Type, obj.Name, old.Objid, RenamedFromRef: renamedFrom));
				continue;
			}

			changes.Add(new PackageObjectChange(obj.Ref, PackageObjectAction.Create, obj.Type, obj.Name));
		}

		var deletedObjids = new HashSet<string>(StringComparer.Ordinal);
		foreach (var orphan in inputs.InstalledObjects.Where(o => !consumedInstalledRefs.Contains(o.Ref)))
		{
			deletedObjids.Add(orphan.Objid);
			var live = inputs.Live.Objects.GetValueOrDefault(orphan.Objid);
			if (live is { Exists: true, HasContents: true })
			{
				notes.Add($"Object {{{{{orphan.Ref}}}}} ({orphan.Objid}) is slated for deletion but contains objects; review its contents before applying.");
			}

			changes.Add(new PackageObjectChange(
				orphan.Ref, PackageObjectAction.Delete,
				Enum.TryParse<PackageObjectType>(orphan.Type, ignoreCase: true, out var type) ? type : PackageObjectType.Thing,
				live?.Name ?? orphan.Ref, orphan.Objid));
		}

		return (changes, objidByRef, deletedObjids);
	}

	// ── Attributes: three-way merge truth table (decisions 20.7, 20.15) ────

	private static List<PackageAttributeChange> ClassifyAttributes(
		PackagePlanInputs inputs,
		Dictionary<string, string> objidByRef,
		HashSet<string> deletedObjids,
		List<string> notes)
	{
		var changes = new List<PackageAttributeChange>();
		var unresolvedCount = 0;

		string? Resolve(PackageRef reference) => reference switch
		{
			{ Kind: PackageRefKind.Internal, Package: not null } =>
				inputs.CrossPackageObjids.GetValueOrDefault($"{reference.Package}/{reference.Name}"),
			{ Kind: PackageRefKind.Internal } => objidByRef.GetValueOrDefault(reference.Name),
			{ Kind: PackageRefKind.WellKnown } => inputs.WellKnownObjids.GetValueOrDefault(reference.Name),
			{ Kind: PackageRefKind.Configure } => inputs.ConfigureAnswers.GetValueOrDefault(reference.Name),
			_ => null
		};

		var baselineByKey = inputs.Baselines.ToDictionary(
			b => (b.Objid, Attribute: b.Attribute.ToUpperInvariant()));
		var manifestKeys = new HashSet<(string Objid, string Attribute)>();
		var legacyRefKeys = new HashSet<(string Objid, string Attribute)>();
		var ownedObjids = inputs.InstalledObjects.Select(o => o.Objid).ToHashSet(StringComparer.Ordinal);
		var legacyRefsByObject = inputs.Baselines
			.Where(b => b.Attribute.StartsWith($"{PackageRefIndirection.RefsBranch}`", StringComparison.OrdinalIgnoreCase))
			.ToLookup(b => b.Objid);


		foreach (var obj in inputs.Manifest.Objects)
		{
			var objid = objidByRef.GetValueOrDefault(obj.Ref);
			var live = objid is not null ? inputs.Live.Objects.GetValueOrDefault(objid) : null;

			if (obj.IsAttach && objid is not null)
			{
				// On a package-owned target, only an old attached-code baseline can
				// establish that a legacy ref belonged to the attachment too.
				foreach (var legacy in legacyRefsByObject[objid].Where(legacy => !ownedObjids.Contains(objid)
					|| obj.Attributes.Keys.Any(name =>
						baselineByKey.GetValueOrDefault((objid, name.ToUpperInvariant()))?.BaselineValue
							.Contains($"[v({legacy.Attribute})]", StringComparison.OrdinalIgnoreCase) == true)))
				{
					legacyRefKeys.Add((objid, legacy.Attribute.ToUpperInvariant()));
				}
			}

			foreach (var (attrName, attr) in obj.Attributes)
			{
				// Code never carries dbrefs (decision 20.21): tokens become
				// [v(PM`REFS`...)] recalls — a total, game-portable transform.
				var resolved = PackageRefIndirection.TransformCode(attr.Value, obj.IsAttach ? inputs.Manifest.Name : null);

				if (objid is null || live is null)
				{
					// Target is created by this apply — every attribute is a create.
					changes.Add(new PackageAttributeChange(
						obj.Ref, null, attrName, PackageAttributeAction.Create, NewValue: resolved));
					continue;
				}

				manifestKeys.Add((objid, attrName.ToUpperInvariant()));
				var baseline = baselineByKey.GetValueOrDefault((objid, attrName.ToUpperInvariant()));
				var liveValue = live.Attributes.TryGetValue(attrName, out var lv) ? lv : null;

				changes.Add(ClassifyExisting(obj.Ref, objid, attrName, baseline?.BaselineValue, liveValue, resolved, requiresApply: false));
			}

			// Synthesize the engine-managed PM`REFS`<NAME> ref attributes this
			// object needs: value = the resolved objid/answer (decision 20.21).
			// They run through the same three-way table, so a user re-pointing
			// a ref locally is preserved as KeepLocal on upgrade.
			foreach (var reference in PackageRefIndirection.RefsUsedIn(obj, obj.IsAttach ? inputs.Manifest.Name : null))
			{
				var refAttr = PackageRefIndirection.AttributeNameFor(reference, obj.IsAttach ? inputs.Manifest.Name : null);
				var resolution = Resolve(reference);
				var newValue = resolution ?? reference.ToString();
				if (resolution is null)
				{
					unresolvedCount++;
				}

				if (objid is null || live is null)
				{
					changes.Add(new PackageAttributeChange(
						obj.Ref, null, refAttr, PackageAttributeAction.Create,
						NewValue: newValue, RequiresApplyResolution: resolution is null));
					continue;
				}

				manifestKeys.Add((objid, refAttr.ToUpperInvariant()));
				var baseline = baselineByKey.GetValueOrDefault((objid, refAttr.ToUpperInvariant()));
				var liveValue = live.Attributes.TryGetValue(refAttr, out var lv) ? lv : null;

				string? previousAttribute = null;
				if (obj.IsAttach && baseline is null && liveValue is null)
				{
					var legacy = PackageRefIndirection.AttributeNameFor(reference);
					var legacyBaseline = baselineByKey.GetValueOrDefault((objid, legacy));
					// A shared legacy leaf may hold another package's resolution. Never copy
					// that collision into the isolated namespace.
					var shared = inputs.OtherManagedAttributes.Any(a => a.Objid == objid
						&& a.PackageId != inputs.Manifest.Name && a.Attribute.Equals(legacy, StringComparison.OrdinalIgnoreCase));
					if (legacyBaseline is not null && !shared && legacyRefKeys.Contains((objid, legacy)))
					{
						previousAttribute = legacy;
						baseline = legacyBaseline;
						liveValue = live.Attributes.GetValueOrDefault(legacy);
					}
				}

				// Empty managed well-known refs from older installs are invalid objids,
				// not useful local overrides. Configure values may legitimately be empty.
				if (baseline is not null && liveValue == "" && reference.Kind == PackageRefKind.WellKnown
					&& !string.IsNullOrEmpty(resolution))
				{
					changes.Add(new PackageAttributeChange(obj.Ref, objid, refAttr, PackageAttributeAction.AutoUpgrade,
						BaseValue: baseline.BaselineValue, LiveValue: liveValue, NewValue: resolution,
						PreviousAttribute: previousAttribute));
					notes.Add($"{obj.Ref}/{refAttr}: repair empty well-known ref to {resolution}.");
					continue;
				}

				if (previousAttribute is not null && liveValue is not null && liveValue != baseline!.BaselineValue)
				{
					// Another package may already have retired its legacy baseline. A changed
					// value is ambiguous: intentional re-point or historical shared-ref collision.
					changes.Add(new PackageAttributeChange(obj.Ref, objid, refAttr, PackageAttributeAction.Conflict,
						PackageConflictKind.ModifyModify, baseline.BaselineValue, liveValue, newValue,
						resolution is null, previousAttribute));
					notes.Add($"{obj.Ref}/{refAttr}: legacy ref differs from its baseline; choose whether to retain it or use this package's resolution.");
					continue;
				}

				changes.Add(ClassifyExisting(
					obj.Ref, objid, refAttr, baseline?.BaselineValue, liveValue, newValue,
					requiresApply: resolution is null) with
				{ PreviousAttribute = previousAttribute });
			}
		}

		// Reverse of objidByRef; the first ref to claim an objid wins, as it did in the dictionary walk.
		var refByObjid = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (var (reference, objid) in objidByRef)
		{
			refByObjid.TryAdd(objid, reference);
		}

		// Baselines whose attribute vanished from the manifest: delete / conflict / cleanup.
		foreach (var baseline in inputs.Baselines)
		{
			if (manifestKeys.Contains((baseline.Objid, baseline.Attribute.ToUpperInvariant()))
				|| deletedObjids.Contains(baseline.Objid))
			{
				// Still in the manifest, or the whole object is being deleted (object action covers it).
				continue;
			}

			var live = inputs.Live.Objects.GetValueOrDefault(baseline.Objid);
			string? liveValue = null;
			if (live is not null && live.Attributes.TryGetValue(baseline.Attribute, out var lv))
			{
				liveValue = lv;
			}

			var targetRef = refByObjid.GetValueOrDefault(baseline.Objid) ?? baseline.Objid;

			var isLegacyRef = baseline.Attribute.StartsWith($"{PackageRefIndirection.RefsBranch}`", StringComparison.OrdinalIgnoreCase);
			// A removed attachment has no incoming spec to establish provenance. Its
			// locally edited code can still be retained through ModifyDelete/KeepMine.
			var recalledByLocalCode = isLegacyRef && liveValue == baseline.BaselineValue && live is not null
				&& inputs.Baselines.Any(code => code.Objid == baseline.Objid
					&& live.Attributes.TryGetValue(code.Attribute, out var codeValue)
					&& codeValue != code.BaselineValue
					&& codeValue.Contains($"[v({baseline.Attribute})]", StringComparison.OrdinalIgnoreCase));

			// Legacy attached refs may still be recalled by another package or locally
			// edited code. Retire our ownership without deleting the shared leaf.
			if ((isLegacyRef && (!ownedObjids.Contains(baseline.Objid)
					|| legacyRefKeys.Contains((baseline.Objid, baseline.Attribute.ToUpperInvariant())) || recalledByLocalCode))
				|| (PackageRefIndirection.IsRefAttribute(baseline.Attribute)
					&& inputs.OtherManagedAttributes.Any(a => a.PackageId != inputs.Manifest.Name && a.Objid == baseline.Objid
						&& a.Attribute.Equals(baseline.Attribute, StringComparison.OrdinalIgnoreCase))))
			{
				changes.Add(new PackageAttributeChange(targetRef, baseline.Objid, baseline.Attribute,
					PackageAttributeAction.RemoveBaseline, BaseValue: baseline.BaselineValue, LiveValue: liveValue));
				continue;
			}

			changes.Add(liveValue switch
			{
				null => new PackageAttributeChange(
					targetRef, baseline.Objid, baseline.Attribute, PackageAttributeAction.RemoveBaseline,
					BaseValue: baseline.BaselineValue),
				_ when liveValue == baseline.BaselineValue => new PackageAttributeChange(
					targetRef, baseline.Objid, baseline.Attribute, PackageAttributeAction.Delete,
					BaseValue: baseline.BaselineValue, LiveValue: liveValue),
				_ => new PackageAttributeChange(
					targetRef, baseline.Objid, baseline.Attribute, PackageAttributeAction.Conflict,
					PackageConflictKind.ModifyDelete, baseline.BaselineValue, liveValue)
			});
		}

		if (unresolvedCount > 0)
		{
			notes.Add($"{unresolvedCount} PM`REFS entr{(unresolvedCount == 1 ? "y" : "ies")} resolve at apply time (new objects or unanswered configure prompts).");
		}

		return changes;
	}

	/// <summary>The dpkg/ucf truth table, extended with local-deletion and add/add cases.</summary>
	private static PackageAttributeChange ClassifyExisting(
		string targetRef, string objid, string attribute,
		string? baseValue, string? liveValue, string newValue, bool requiresApply)
	{
		PackageAttributeChange Make(PackageAttributeAction action, PackageConflictKind? conflict = null) => new(
			targetRef, objid, attribute, action, conflict, baseValue, liveValue, newValue, requiresApply);

		if (baseValue is null)
		{
			// Not managed by this package yet.
			return liveValue switch
			{
				null => Make(PackageAttributeAction.Create),
				_ when liveValue == newValue => Make(PackageAttributeAction.Adopt),
				_ => Make(PackageAttributeAction.Conflict, PackageConflictKind.AddAdd)
			};
		}

		if (liveValue is null)
		{
			// User deleted the attribute locally.
			return baseValue == newValue
				? Make(PackageAttributeAction.KeepLocal)
				: Make(PackageAttributeAction.Conflict, PackageConflictKind.DeleteModify);
		}

		var userChanged = liveValue != baseValue;
		var packageChanged = newValue != baseValue;

		return (userChanged, packageChanged) switch
		{
			(false, false) => Make(PackageAttributeAction.NoChange),
			(false, true) => Make(PackageAttributeAction.AutoUpgrade),
			(true, false) => Make(PackageAttributeAction.KeepLocal),
			(true, true) when liveValue == newValue => Make(PackageAttributeAction.NoChange),
			(true, true) => Make(PackageAttributeAction.Conflict, PackageConflictKind.ModifyModify)
		};
	}

	// ── Object structure: flags, powers, locks, attribute flags ─────────────
	// Full three-way merge (decision 20.7, extended). Binary elements
	// (flags/powers/attribute flags) resolve deterministically and never
	// conflict; value-typed locks reuse the attribute conflict model. Only
	// elements the package manages now or managed at the last apply are
	// considered — admin-added extras are left untouched and unmentioned.

	private static List<PackageStructureChange> ClassifyStructure(
		PackagePlanInputs inputs,
		Dictionary<string, string> objidByRef,
		HashSet<string> deletedObjids,
		List<string> notes)
	{
		var changes = new List<PackageStructureChange>();
		var baselines = inputs.StructureBaselines ?? new Dictionary<string, PackageStructureBaseline>();
		var unresolvedLocks = 0;

		string? Resolve(PackageRef reference) => reference switch
		{
			{ Kind: PackageRefKind.Internal, Package: not null } =>
				inputs.CrossPackageObjids.GetValueOrDefault($"{reference.Package}/{reference.Name}"),
			{ Kind: PackageRefKind.Internal } => objidByRef.GetValueOrDefault(reference.Name),
			{ Kind: PackageRefKind.WellKnown } => inputs.WellKnownObjids.GetValueOrDefault(reference.Name),
			{ Kind: PackageRefKind.Configure } => inputs.ConfigureAnswers.GetValueOrDefault(reference.Name),
			_ => null
		};

		foreach (var obj in inputs.Manifest.Objects)
		{
			var objid = objidByRef.GetValueOrDefault(obj.Ref);
			if (objid is not null && deletedObjids.Contains(objid))
			{
				continue;
			}

			var live = objid is not null ? inputs.Live.Objects.GetValueOrDefault(objid) : null;
			var baseline = objid is not null ? baselines.GetValueOrDefault(objid) : null;
			var existing = live is { Exists: true };

			var liveFlags = (existing ? live!.Flags : null) ?? Array.Empty<string>();
			var livePowers = (existing ? live!.Powers : null) ?? Array.Empty<string>();

			// Object-level flags and powers (attach objects declare none).
			ClassifySet(obj.Ref, objid, PackageStructureKind.ObjectFlag, attribute: null,
				baseline?.Flags ?? Array.Empty<string>(), liveFlags, obj.Flags, changes);
			ClassifySet(obj.Ref, objid, PackageStructureKind.ObjectPower, attribute: null,
				baseline?.Powers ?? Array.Empty<string>(), livePowers, obj.Powers, changes);

			// Per-attribute flags (created objects, attach objects, and existing objects alike).
			var baseAttrFlags = baseline?.AttributeFlags
				?? new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
			var liveAttrFlags = existing ? live!.AttributeFlags : null;
			foreach (var (attrName, attrSpec) in obj.Attributes)
			{
				ClassifySet(obj.Ref, objid, PackageStructureKind.AttributeFlag, attrName,
					baseAttrFlags.GetValueOrDefault(attrName) ?? [],
					liveAttrFlags?.GetValueOrDefault(attrName) ?? [],
					attrSpec.Flags, changes);
			}

			// Locks (value-typed; refs resolved directly, not via PM`REFS).
			var baseLocks = baseline?.Locks ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			var liveLocks = (existing ? live!.Locks : null)
				?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			// The manifest may spell a standard lock any way PennMUSH does; the live object's keys are
			// LockType's spelling, so both sides are canonicalised or "teleport" reads as drift
			// against the TPort it just wrote.
			var specLocks = LockNames.Fold(obj.Locks);
			var lockTypes = new HashSet<string>(baseLocks.Keys, StringComparer.OrdinalIgnoreCase);
			lockTypes.UnionWith(specLocks.Keys);
			foreach (var lockType in lockTypes.OrderBy(t => t, StringComparer.Ordinal))
			{
				string? newValue = null;
				var requiresApply = false;
				if (specLocks.TryGetValue(lockType, out var rawNew))
				{
					newValue = PackageRefSubstitution.Substitute(rawNew, Resolve, out var unresolved);
					if (unresolved.Count > 0)
					{
						requiresApply = true;
						unresolvedLocks++;
					}
				}

				var baseValue = baseLocks.GetValueOrDefault(lockType);
				var liveValue = liveLocks.GetValueOrDefault(lockType);
				var (action, conflict) = ClassifyLock(baseValue, liveValue, newValue, requiresApply);
				changes.Add(new PackageStructureChange(
					obj.Ref, objid, PackageStructureKind.Lock, lockType, action,
					Attribute: null, Conflict: conflict,
					BaseValue: baseValue, LiveValue: liveValue, NewValue: newValue,
					RequiresApplyResolution: requiresApply));
			}
		}

		if (unresolvedLocks > 0)
		{
			notes.Add($"{unresolvedLocks} lock value{(unresolvedLocks == 1 ? "" : "s")} resolve at apply time (new objects or unanswered configure prompts).");
		}

		return changes;
	}

	/// <summary>
	/// Three-way merge of a set-valued structure element (flags/powers/attribute
	/// flags). Iterates only elements the package manages now or managed before;
	/// binary presence resolves deterministically (never a conflict).
	/// </summary>
	private static void ClassifySet(
		string targetRef, string? objid, PackageStructureKind kind, string? attribute,
		IReadOnlyList<string> baseline, IReadOnlyList<string> live, IReadOnlyList<string> @new,
		List<PackageStructureChange> changes)
	{
		var baseSet = new HashSet<string>(baseline, StringComparer.OrdinalIgnoreCase);
		var liveSet = new HashSet<string>(live, StringComparer.OrdinalIgnoreCase);
		var newSet = new HashSet<string>(@new, StringComparer.OrdinalIgnoreCase);

		// Canonical element name → display spelling; prefer the manifest spelling.
		var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (var n in @new)
		{
			names[n] = n;
		}

		foreach (var n in baseline)
		{
			names.TryAdd(n, n);
		}

		foreach (var element in names.Values.OrderBy(n => n, StringComparer.Ordinal))
		{
			var action = ClassifyPresence(baseSet.Contains(element), liveSet.Contains(element), newSet.Contains(element));
			changes.Add(new PackageStructureChange(targetRef, objid, kind, element, action, attribute));
		}
	}

	/// <summary>Binary-presence three-way table (no conflicts possible).</summary>
	private static PackageStructureAction ClassifyPresence(bool inBaseline, bool inLive, bool inNew)
	{
		if (!inBaseline)
		{
			// Only iterated for elements in baseline ∪ new, so here inNew is true.
			return inLive ? PackageStructureAction.Adopt : PackageStructureAction.Add;
		}

		return (inNew, inLive) switch
		{
			(true, true) => PackageStructureAction.NoChange,
			(true, false) => PackageStructureAction.KeepLocal,    // admin removed it; package still declares it
			(false, true) => PackageStructureAction.Remove,       // package dropped it; admin still has it
			(false, false) => PackageStructureAction.RemoveBaseline
		};
	}

	/// <summary>Value-typed (lock) three-way table — the attribute truth table, mapped to structure actions.</summary>
	private static (PackageStructureAction Action, PackageConflictKind? Conflict) ClassifyLock(
		string? baseValue, string? liveValue, string? newValue, bool requiresApply)
	{
		// Lock dropped from the manifest (only reached for a baseline key absent from new).
		if (newValue is null)
		{
			return liveValue switch
			{
				null => (PackageStructureAction.RemoveBaseline, null),
				_ when liveValue == baseValue => (PackageStructureAction.Remove, null),
				_ => (PackageStructureAction.Conflict, PackageConflictKind.ModifyDelete)
			};
		}

		// A still-unresolved ref means we cannot compare yet — set it at apply.
		if (requiresApply)
		{
			return (PackageStructureAction.Add, null);
		}

		if (baseValue is null)
		{
			return liveValue switch
			{
				null => (PackageStructureAction.Add, null),
				_ when liveValue == newValue => (PackageStructureAction.Adopt, null),
				_ => (PackageStructureAction.Conflict, PackageConflictKind.AddAdd)
			};
		}

		if (liveValue is null)
		{
			return baseValue == newValue
				? (PackageStructureAction.KeepLocal, null)
				: (PackageStructureAction.Conflict, PackageConflictKind.DeleteModify);
		}

		var userChanged = liveValue != baseValue;
		var packageChanged = newValue != baseValue;
		return (userChanged, packageChanged) switch
		{
			(false, false) => (PackageStructureAction.NoChange, null),
			(false, true) => (PackageStructureAction.Add, null),        // package changed the lock; admin didn't
			(true, false) => (PackageStructureAction.KeepLocal, null),  // admin changed the lock; package didn't
			(true, true) when liveValue == newValue => (PackageStructureAction.NoChange, null),
			(true, true) => (PackageStructureAction.Conflict, PackageConflictKind.ModifyModify)
		};
	}

	// ── $command collisions (decision 20.20) ────────────────────────────────

	private static List<PackageCommandCollision> DetectCommandCollisions(PackagePlanInputs inputs)
	{
		var collisions = new List<PackageCommandCollision>();
		var existing = new Dictionary<string, (string PackageId, string Attribute, string Objid)>(StringComparer.Ordinal);

		foreach (var managed in inputs.OtherManagedAttributes.Where(m => m.PackageId != inputs.Manifest.Name))
		{
			var pattern = ExtractCommandPattern(managed.BaselineValue);
			if (pattern is not null)
			{
				existing.TryAdd(pattern, (managed.PackageId, managed.Attribute, managed.Objid));
			}
		}

		foreach (var obj in inputs.Manifest.Objects)
		{
			foreach (var (attrName, attr) in obj.Attributes)
			{
				var pattern = ExtractCommandPattern(attr.Value);
				if (pattern is not null && existing.TryGetValue(pattern, out var other))
				{
					collisions.Add(new PackageCommandCollision(
						pattern, obj.Ref, attrName, other.PackageId, other.Attribute, other.Objid));
				}
			}
		}

		return collisions;
	}

	/// <summary>
	/// Extracts the normalized command pattern from a <c>$pattern:action</c>
	/// attribute value (lowercased, whitespace collapsed), or null when the
	/// value does not define a command.
	/// <para>
	/// Uses the dispatcher's own split and separator unescaping rather than a local
	/// <c>[^:]+</c>: two packages collide when the commands they install match the same input, and
	/// that is decided by the pattern <c>CommandAttributeScanner</c> compiles, not by the raw text.
	/// <c>$foo\:bar:…</c> is one command named <c>foo:bar</c> — cutting it at the escaped colon
	/// compared <c>foo\</c> instead and missed the collision.
	/// </para>
	/// </summary>
	public static string? ExtractCommandPattern(string value)
	{
		var match = CommandDiscoveryService.CommandPatternRegex().Match(value);
		if (!match.Success)
		{
			return null;
		}

		var pattern = CommandDiscoveryService.UnescapePatternSeparator(match.Groups["pattern"].Value);
		var words = pattern.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		return string.Join(' ', words).ToLowerInvariant();
	}
}
