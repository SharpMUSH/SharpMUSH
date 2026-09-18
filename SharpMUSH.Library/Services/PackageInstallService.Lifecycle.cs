using Mediator;
using SharpMUSH.Library.Commands.Database;
using System.Text.Json;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Models.Portal.Applications;
using SharpMUSH.Library.Models.Portal.Widgets;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Utilities;


namespace SharpMUSH.Library.Services;

/// <summary>Uninstall and rollback: removing a package and restoring what it displaced.</summary>
public partial class PackageInstallService
{
	public async Task<Result<Success>> UninstallAsync(
		string packageId, bool force = false, CancellationToken cancellationToken = default)
	{
		if (await registry.GetInstalledPackageAsync(packageId) is not InstalledPackageRecord installed)
		{
			return new Error<string>($"'{packageId}' is not installed.");
		}

		var dependents = await registry.GetPackageDependentsAsync(packageId);
		if (dependents.Count > 0 && !force)
		{
			return new Error<string>(
				$"Cannot uninstall '{packageId}': {string.Join(", ", dependents.Select(d => d.PackageId))} depend(s) on it. Uninstall them first or force-remove.");
		}

		// Managed packages (Phase 4): no game objects/attributes — remove the
		// deposited plugins/<id>/ directory (and unload the plugin if it is loaded
		// and unloadable), then drop the registry records.
		if (installed.DeployedFiles is { Count: > 0 })
		{
			var removed = await managedInstaller.RemoveAsync(
				packageId, installed.DeployedFiles, cancellationToken);
			if (removed is Error<string> removeError)
			{
				return removeError;
			}

			await registry.RemoveInstalledPackageAsync(packageId);
			return new Success();
		}

		var ownObjects = await registry.GetPackageObjectsAsync(packageId);
		var ownObjids = ownObjects.Select(o => o.Objid).ToHashSet(StringComparer.Ordinal);

		// Attachment guard (decision 20.3): another package may manage
		// attributes on one of THIS package's objects (cross-package attach).
		// Destroying the object would orphan those attributes, so block while
		// any attachment exists — unless forced.
		if (ownObjids.Count > 0 && !force)
		{
			var attachers = new SortedSet<string>(StringComparer.Ordinal);
			foreach (var objid in ownObjids)
			{
				foreach (var managed in (await registry.GetManagedAttributesForObjectAsync(objid))
					.Where(managed => managed.PackageId != packageId))
				{
					attachers.Add(managed.PackageId);
				}
			}

			if (attachers.Count > 0)
			{
				return new Error<string>(
					$"Cannot uninstall '{packageId}': {string.Join(", ", attachers)} {(attachers.Count == 1 ? "is" : "are")} attached to its object(s). Uninstall them first or force-remove.");
			}
		}

		await using var writes = BeginWrites(await GetPackageManagerWizardAsync(cancellationToken));
		await RetirePackageAsync(writes, packageId, ownObjects, cancellationToken);
		return new Success();
	}

	/// <summary>
	/// The writes of an uninstall that has passed its guards. Removing the package's rows is the
	/// last write and commits it: that also drops its revisions, so it cannot be reverted.
	/// </summary>
	private async Task RetirePackageAsync(
		PackageWriteTransaction writes,
		string packageId,
		IReadOnlyList<PackageObjectRecord> ownObjects,
		CancellationToken cancellationToken)
	{
		var notes = new List<string>();
		var ownObjids = ownObjects.Select(o => o.Objid).ToHashSet(StringComparer.Ordinal);

		// Managed attrs on objects this package does NOT own (cross-package): clear them.
		foreach (var group in (await registry.GetManagedAttributesAsync(packageId))
			.Where(m => !ownObjids.Contains(m.Objid)).GroupBy(m => m.Objid))
		{
			var otherRefs = (await registry.GetManagedAttributesForObjectAsync(group.Key))
				.Where(a => a.PackageId != packageId && PackageRefIndirection.IsRefAttribute(a.Attribute))
				.Select(a => a.Attribute).ToHashSet(StringComparer.OrdinalIgnoreCase);
			if (HelperFunctions.ParseDbRef(group.Key) is not DBRef dbref)
			{
				continue;
			}

			foreach (var managed in group.Where(a => !otherRefs.Contains(a.Attribute)))
			{
				await writes.ClearAttributeAsync(dbref, managed.Attribute.Split('`'), cancellationToken);
			}
		}

		foreach (var record in ownObjects)
		{
			await MarkGoingAsync(writes, record.Objid, notes, cancellationToken);
		}

		// Application packages own portal registrations rather than objects;
		// reclaim every application this package installed.
		foreach (var app in (await applications.GetApplicationsAsync())
			.Where(a => string.Equals(a.OwningPackage, packageId, StringComparison.Ordinal)))
		{
			await writes.RemoveApplicationAsync(app.Slug);
			notes.Add($"Removed application '{app.Slug}'.");
		}

		await registry.RemoveInstalledPackageAsync(packageId);
		writes.Commit();
	}

	// ── Rollback ─────────────────────────────────────────────────────────────

	public async Task<Result<PackageRollbackResult>> RollbackAsync(
		string packageId, int revision, CancellationToken cancellationToken = default)
	{
		if (await registry.GetInstalledPackageAsync(packageId) is not InstalledPackageRecord installed)
		{
			return new Error<string>($"'{packageId}' is not installed.");
		}

		// A revision snapshot carries version, objects, attributes and structure. A managed package's
		// deployed files and an application package's portal registration live outside it, so a
		// rollback would move the recorded version while the newer DLLs or registration stayed live.
		if (installed.DeployedFiles is { Count: > 0 })
		{
			return new Error<string>(
				$"Cannot roll back '{packageId}': it is a managed package, and its revisions do not keep the files "
				+ $"deployed under plugins/{packageId}/. Install the version you want instead.");
		}

		if ((await applications.GetApplicationsAsync())
			.Any(a => string.Equals(a.OwningPackage, packageId, StringComparison.Ordinal)))
		{
			return new Error<string>(
				$"Cannot roll back '{packageId}': it registers a portal application, and its revisions do not keep "
				+ "that registration. Install the version you want instead.");
		}

		if (await registry.GetPackageRevisionAsync(packageId, revision) is not PackageRevisionRecord record)
		{
			return new Error<string>($"'{packageId}' has no revision {revision}.");
		}

		var snapshot = JsonSerializer.Deserialize<PackageRevisionSnapshot>(record.ManifestSnapshotJson, SnapshotJson);
		if (snapshot is null)
		{
			return new Error<string>($"Revision {revision} has no usable snapshot.");
		}

		var managedAttributes = await registry.GetManagedAttributesAsync(packageId);
		var sharedRefs = new HashSet<(string Objid, string Attribute)>();
		foreach (var objid in snapshot.Attributes.Select(a => a.Objid)
			.Concat(managedAttributes.Select(a => a.Objid)).Distinct(StringComparer.Ordinal))
		{
			foreach (var shared in (await registry.GetManagedAttributesForObjectAsync(objid))
				.Where(a => a.PackageId != packageId && PackageRefIndirection.IsRefAttribute(a.Attribute)))
			{
				sharedRefs.Add((objid, shared.Attribute.ToUpperInvariant()));
			}
		}

		// Validate shared legacy restores before writing even the first snapshot attribute.
		// A rollback must not redirect another package's code to our historical resolution.
		foreach (var attribute in snapshot.Attributes.Where(a => sharedRefs.Contains((a.Objid, a.Attribute.ToUpperInvariant()))))
		{
			if (HelperFunctions.ParseDbRef(attribute.Objid) is not DBRef dbref
				|| (await database.GetObjectNodeAsync(dbref, cancellationToken)).IsNone)
			{
				continue;
			}
			var live = await attributeStore.GetAttributeAsync(dbref, attribute.Attribute.Split('`'), cancellationToken)
				.LastOrDefaultAsync(cancellationToken);
			if (live?.Value.ToPlainText() != attribute.Value)
			{
				return new Error<string>($"Cannot roll back '{packageId}': {attribute.Objid}/{attribute.Attribute} is shared with another package and differs from revision {revision}.");
			}
		}

		return await PlanObjectRollbackAsync(packageId, revision, snapshot, cancellationToken) switch
		{
			ObjectRollback objects => await RestoreRevisionAsync(
				installed, record, snapshot, managedAttributes, sharedRefs, objects, cancellationToken),
			Error<string> error => error
		};
	}

	/// <summary>
	/// How the package's object registry moves to the objects a revision owned.
	/// </summary>
	/// <param name="Wanted">The registry records the revision implies.</param>
	/// <param name="Stale">Current records the revision does not have (a later object, or a later ref for one).</param>
	/// <param name="Revive">Owned at the revision, not now: removed later and marked GOING then.</param>
	/// <param name="Release">Owned now, not at the revision: added later, so marked GOING now.</param>
	private sealed record ObjectRollback(
		IReadOnlyList<PackageObjectRecord> Wanted,
		IReadOnlyList<PackageObjectRecord> Stale,
		IReadOnlyList<PackageObjectRecord> Revive,
		IReadOnlyList<PackageObjectRecord> Release);

	/// <summary>
	/// Works out <see cref="ObjectRollback"/>, refusing before any write when it cannot be done: an
	/// owned object that no longer exists cannot be recreated, and a snapshot written before
	/// <see cref="PackageObjectRelation"/> was recorded cannot say whether an entry the registry does
	/// not hold was owned or only attached.
	/// </summary>
	private async Task<Result<ObjectRollback>> PlanObjectRollbackAsync(
		string packageId, int revision, PackageRevisionSnapshot snapshot, CancellationToken cancellationToken)
	{
		var current = await registry.GetPackageObjectsAsync(packageId);
		var currentObjids = current.Select(o => o.Objid).ToHashSet(StringComparer.Ordinal);

		var wanted = new List<PackageObjectRecord>();
		foreach (var obj in snapshot.Objects.Where(o => o.Relation != PackageObjectRelation.Attached))
		{
			// An unrecorded entry the registry still holds was owned: a package registers only objects it created.
			if (obj.Relation == PackageObjectRelation.Unrecorded && !currentObjids.Contains(obj.Objid))
			{
				return new Error<string>(
					$"Cannot roll back '{packageId}' to revision {revision}: that revision predates recording whether the "
					+ $"package owns or only attaches to each object, and {obj.Objid} ({{{{{obj.Ref}}}}}) is not registered "
					+ "to the package now, so there is no way to tell whether to restore it.");
			}

			wanted.Add(new PackageObjectRecord(packageId, obj.Ref, obj.Objid, obj.Type));
		}

		var revive = wanted.Where(o => !currentObjids.Contains(o.Objid)).ToList();
		foreach (var obj in revive)
		{
			if (await GetKnownAsync(obj.Objid, cancellationToken) is null)
			{
				return new Error<string>(
					$"Cannot roll back '{packageId}' to revision {revision}: {obj.Objid} ({{{{{obj.Ref}}}}}) was destroyed "
					+ "after that revision, and a rollback cannot recreate it.");
			}
		}

		var wantedObjids = wanted.Select(o => o.Objid).ToHashSet(StringComparer.Ordinal);
		return new ObjectRollback(
			wanted,
			current.Where(c => !wanted.Any(w => w.Ref == c.Ref && w.Objid == c.Objid)).ToList(),
			revive,
			current.Where(c => !wantedObjids.Contains(c.Objid)).ToList());
	}

	/// <summary>
	/// The writes of a rollback that has passed every check, in one transaction: objects back into the
	/// registry first, so the attribute and structure restores find them, objects the revision did not
	/// own released last, and the new revision record committing it.
	/// </summary>
	private async Task<PackageRollbackResult> RestoreRevisionAsync(
		InstalledPackageRecord installed,
		PackageRevisionRecord record,
		PackageRevisionSnapshot snapshot,
		IReadOnlyList<ManagedAttributeRecord> managedAttributes,
		HashSet<(string Objid, string Attribute)> sharedRefs,
		ObjectRollback objects,
		CancellationToken cancellationToken)
	{
		var packageId = installed.Id;
		var revision = record.Revision;
		var pmWizard = await GetPackageManagerWizardAsync(cancellationToken);
		await using var writes = BeginWrites(pmWizard);
		var notes = new List<string>();
		var restoredKeys = new HashSet<(string, string)>();

		foreach (var stale in objects.Stale)
		{
			await writes.RemovePackageObjectAsync(packageId, stale.Ref);
		}

		foreach (var owned in objects.Wanted)
		{
			await writes.UpsertPackageObjectAsync(owned);
		}

		foreach (var revived in objects.Revive)
		{
			await ClearGoingAsync(writes, revived.Objid, notes, cancellationToken);
		}

		foreach (var attribute in snapshot.Attributes)
		{
			if (HelperFunctions.ParseDbRef(attribute.Objid) is not DBRef dbref
				|| (await database.GetObjectNodeAsync(dbref, cancellationToken)).IsNone)
			{
				notes.Add($"Skipped {attribute.Objid}/{attribute.Attribute}: object no longer exists.");
				continue;
			}

			await writes.SetAttributeAsync(
				dbref, attribute.Attribute.Split('`'), MarkupText.Plain(attribute.Value), pmWizard, cancellationToken);
			await writes.UpsertManagedAttributeAsync(new ManagedAttributeRecord(
				packageId, attribute.Objid, attribute.Attribute.ToUpperInvariant(),
				attribute.Value, ContentHash.Sha256Hex(attribute.Value), snapshot.Version));
			restoredKeys.Add((attribute.Objid, attribute.Attribute.ToUpperInvariant()));
		}

		// Attributes managed now but absent from the snapshot: remove to match the old state.
		foreach (var managed in managedAttributes
			.Where(m => !restoredKeys.Contains((m.Objid, m.Attribute.ToUpperInvariant()))))
		{
			var shared = sharedRefs.Contains((managed.Objid, managed.Attribute.ToUpperInvariant()));
			if (HelperFunctions.ParseDbRef(managed.Objid) is DBRef dbref && !shared)
			{
				await writes.ClearAttributeAsync(dbref, managed.Attribute.Split('`'), cancellationToken);
			}

			await writes.RemoveManagedAttributeAsync(packageId, managed.Objid, managed.Attribute);
			notes.Add(shared
				? $"Released ownership of shared ref {managed.Objid}/{managed.Attribute}; its value was retained."
				: $"Removed {managed.Objid}/{managed.Attribute} (not present in revision {revision}).");
		}

		await RestoreStructureAsync(writes, packageId, snapshot, notes, cancellationToken);

		foreach (var released in objects.Release)
		{
			await MarkGoingAsync(writes, released.Objid, notes, cancellationToken);
		}

		var newRevision = installed.CurrentRevision + 1;
		await writes.UpsertInstalledPackageAsync(installed with
		{
			Version = snapshot.Version,
			CurrentRevision = newRevision,
			InstalledAt = DateTimeOffset.UtcNow
		});
		await registry.AddPackageRevisionAsync(record with
		{
			Revision = newRevision,
			Kind = PackageRevisionKind.Rollback,
			AppliedAt = DateTimeOffset.UtcNow
		});
		writes.Commit();

		return new PackageRollbackResult(newRevision, revision, notes);
	}

	/// <summary>
	/// Reconciles each object's package-managed structure to a revision snapshot
	/// (rollback): sets the snapshot's flags/powers/locks/attribute flags and
	/// unsets package-managed elements absent from it. Scoped to elements the
	/// package set, so admin-added extras are never disturbed.
	/// </summary>
	private async Task RestoreStructureAsync(
		PackageWriteTransaction writes,
		string packageId, PackageRevisionSnapshot snapshot, List<string> notes, CancellationToken cancellationToken)
	{
		var noDecisions = new Dictionary<string, PackageConflictDecision>(StringComparer.Ordinal);
		// Fold once, here, so the whole rollback speaks canonical: a revision written before lock
		// names were canonical spells a lock the way that world did. Left raw, its `Teleport` add and
		// the folded baseline's `tport` removal both canonicalise to Teleport in the provider, the
		// removal is applied last, and the rollback deletes the lock the revision asked it to keep.
		var target = (snapshot.Structure ?? [])
			.ToDictionary(s => s.Objid, s => s with { Locks = LockNames.Fold(s.Locks) }, StringComparer.Ordinal);
		var current = DeserializeStructureBaselines(await registry.GetManagedStructuresAsync(packageId));

		foreach (var objid in target.Keys.Union(current.Keys, StringComparer.Ordinal))
		{
			if (await GetKnownAsync(objid, cancellationToken) is null)
			{
				notes.Add($"Skipped structure for {objid}: object no longer exists.");
				continue;
			}

			var want = target.GetValueOrDefault(objid);
			var have = current.GetValueOrDefault(objid) ?? PackageStructureBaseline.Empty;

			foreach (var change in StructureRollbackChanges(objid, have, want))
			{
				var error = await ApplyStructureChangeAsync(writes, change, objid, noDecisions, notes, cancellationToken);
				if (error is not null)
				{
					notes.Add(error);
				}
			}

			if (want is null)
			{
				await writes.RemoveManagedStructureAsync(packageId, objid);
			}
			else
			{
				var baseline = new PackageStructureBaseline(want.Flags, want.Powers, want.Locks, want.AttributeFlags);
				await writes.UpsertManagedStructureAsync(new ManagedStructureRecord(
					packageId, objid, JsonSerializer.Serialize(baseline, SnapshotJson), snapshot.Version));
			}
		}
	}

	/// <summary>Synthesizes the add/remove structure changes that move <paramref name="have"/> to <paramref name="want"/>.</summary>
	private static IEnumerable<PackageStructureChange> StructureRollbackChanges(
		string objid, PackageStructureBaseline have, PackageRevisionSnapshotStructure? want)
	{
		var wantFlags = want?.Flags ?? Array.Empty<string>();
		var wantPowers = want?.Powers ?? Array.Empty<string>();
		var wantLocks = want?.Locks ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		var wantAttrFlags = want?.AttributeFlags ?? new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

		IEnumerable<PackageStructureChange> Set(PackageStructureKind kind, string? attribute,
			IReadOnlyList<string> haveSet, IReadOnlyList<string> wantSet)
		{
			var wantHash = new HashSet<string>(wantSet, StringComparer.OrdinalIgnoreCase);
			foreach (var element in wantSet)
			{
				yield return new PackageStructureChange(objid, objid, kind, element, PackageStructureAction.Add, attribute);
			}

			foreach (var element in haveSet.Where(e => !wantHash.Contains(e)))
			{
				yield return new PackageStructureChange(objid, objid, kind, element, PackageStructureAction.Remove, attribute);
			}
		}

		foreach (var change in Set(PackageStructureKind.ObjectFlag, null, have.Flags, wantFlags))
		{
			yield return change;
		}

		foreach (var change in Set(PackageStructureKind.ObjectPower, null, have.Powers, wantPowers))
		{
			yield return change;
		}

		var attrNames = new HashSet<string>(have.AttributeFlags.Keys, StringComparer.OrdinalIgnoreCase);
		attrNames.UnionWith(wantAttrFlags.Keys);
		foreach (var attr in attrNames)
		{
			var changes = Set(PackageStructureKind.AttributeFlag, attr,
				have.AttributeFlags.GetValueOrDefault(attr) ?? [], wantAttrFlags.GetValueOrDefault(attr) ?? []);
			foreach (var change in changes)
			{
				yield return change;
			}
		}

		foreach (var (lockType, value) in wantLocks)
		{
			yield return new PackageStructureChange(
				objid, objid, PackageStructureKind.Lock, lockType, PackageStructureAction.Add, NewValue: value);
		}

		foreach (var lockType in have.Locks.Keys.Where(k => !wantLocks.ContainsKey(k)))
		{
			yield return new PackageStructureChange(
				objid, objid, PackageStructureKind.Lock, lockType, PackageStructureAction.Remove);
		}
	}
}
