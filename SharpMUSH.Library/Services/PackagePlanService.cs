using System.Text.RegularExpressions;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <summary>
/// Pure changeset computation for package installs and upgrades
/// (decisions 20.7, 20.15, 20.20). See <see cref="IPackagePlanService"/>.
/// </summary>
public partial class PackagePlanService : IPackagePlanService
{
	[GeneratedRegex(@"^\$(?<pattern>[^:]+):", RegexOptions.Singleline)]
	private static partial Regex CommandPatternRegex();

	public PackageChangeset ComputeChangeset(PackagePlanInputs inputs)
	{
		var manifest = inputs.Manifest;
		var notes = new List<string>();

		var dependencyIssues = CheckDependenciesAndConflicts(inputs);
		var (objects, objidByRef, deletedObjids) = ClassifyObjects(inputs, notes);
		var attributes = ClassifyAttributes(inputs, objidByRef, deletedObjids, notes);
		var collisions = DetectCommandCollisions(inputs);

		return new PackageChangeset(
			manifest.Name,
			inputs.Installed?.Version,
			manifest.Version.ToString(),
			inputs.Installed is null ? PackageRevisionKind.Install : PackageRevisionKind.Upgrade,
			objects,
			attributes,
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

	private static (List<PackageObjectChange> Changes, Dictionary<string, string> ObjidByRef, HashSet<string> DeletedObjids)
		ClassifyObjects(PackagePlanInputs inputs, List<string> notes)
	{
		var changes = new List<PackageObjectChange>();
		var objidByRef = new Dictionary<string, string>(StringComparer.Ordinal);
		var consumedInstalledRefs = new HashSet<string>(StringComparer.Ordinal);
		var installedByRef = inputs.InstalledObjects.ToDictionary(o => o.Ref, StringComparer.Ordinal);

		foreach (var obj in inputs.Manifest.Objects)
		{
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

		foreach (var obj in inputs.Manifest.Objects)
		{
			var objid = objidByRef.GetValueOrDefault(obj.Ref);
			var live = objid is not null ? inputs.Live.Objects.GetValueOrDefault(objid) : null;

			foreach (var (attrName, attr) in obj.Attributes)
			{
				var resolved = PackageRefSubstitution.Substitute(attr.Value, Resolve, out var unresolved);
				var requiresApply = unresolved.Count > 0;
				unresolvedCount += unresolved.Count;

				if (objid is null || live is null)
				{
					// Target is created by this apply — every attribute is a create.
					changes.Add(new PackageAttributeChange(
						obj.Ref, null, attrName, PackageAttributeAction.Create,
						NewValue: resolved, RequiresApplyResolution: requiresApply));
					continue;
				}

				manifestKeys.Add((objid, attrName.ToUpperInvariant()));
				var baseline = baselineByKey.GetValueOrDefault((objid, attrName.ToUpperInvariant()));
				var liveValue = live.Attributes.TryGetValue(attrName, out var lv) ? lv : null;

				changes.Add(ClassifyExisting(obj.Ref, objid, attrName, baseline?.BaselineValue, liveValue, resolved, requiresApply));
			}
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

			var targetRef = objidByRef.FirstOrDefault(kv => kv.Value == baseline.Objid).Key ?? baseline.Objid;

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
			notes.Add($"{unresolvedCount} ref(s) resolve at apply time (new objects or unanswered configure prompts); affected values are compared as written.");
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
	/// </summary>
	public static string? ExtractCommandPattern(string value)
	{
		var match = CommandPatternRegex().Match(value);
		if (!match.Success)
		{
			return null;
		}

		var collapsed = string.Join(' ',
			match.Groups["pattern"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
		return collapsed.ToLowerInvariant();
	}
}
