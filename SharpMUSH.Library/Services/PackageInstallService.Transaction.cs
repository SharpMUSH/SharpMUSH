using System.Collections.Immutable;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Models.Portal.Applications;
using SharpMUSH.Library.Utilities;

namespace SharpMUSH.Library.Services;

/// <summary>
/// Apply atomicity. What an apply can see coming it refuses before its first write; everything it
/// may change after that is captured first and put back if it fails before its commit point, the
/// revision record. Nothing spans the stores, so this is compensation rather than a transaction:
/// a process that dies part way through an apply still leaves what it had written.
/// </summary>
public partial class PackageInstallService
{
	/// <summary>Stand-in objid for an object this apply will create, while checking refs before it exists.</summary>
	private const string PendingObjid = "#-1";

	/// <summary>Everything an apply may change, as it was before the first write.</summary>
	/// <param name="PackageId">The package being applied.</param>
	/// <param name="Installed">Its installed-package row; null on a first install.</param>
	/// <param name="Objects">Its object registry records.</param>
	/// <param name="Attributes">Its managed-attribute baselines.</param>
	/// <param name="Structures">Its managed-structure baselines.</param>
	/// <param name="Dependencies">Its dependency rows.</param>
	/// <param name="ApplicationSlug">The slug an application package registers; null for other kinds.</param>
	/// <param name="Application">What was registered at that slug; null when nothing was.</param>
	/// <param name="Live">The existing objects the changeset touches.</param>
	private sealed record ApplyBeforeImage(
		string PackageId,
		InstalledPackageRecord? Installed,
		IReadOnlyList<PackageObjectRecord> Objects,
		IReadOnlyList<ManagedAttributeRecord> Attributes,
		IReadOnlyList<ManagedStructureRecord> Structures,
		IReadOnlyList<PackageDependencyRecord> Dependencies,
		string? ApplicationSlug,
		RegisteredApplication? Application,
		IReadOnlyList<ObjectImage> Live);

	/// <summary>One existing object, as far as an apply can change it.</summary>
	private sealed record ObjectImage(
		string Objid,
		string Name,
		string? Parent,
		IReadOnlyList<string> Flags,
		IReadOnlyList<string> Powers,
		IImmutableDictionary<string, SharpLockData> Locks,
		IReadOnlyList<AttributeImage> Attributes);

	/// <summary>One attribute the changeset touches; <paramref name="Value"/> is null when it did not exist.</summary>
	private sealed record AttributeImage(string[] Path, MString? Value, IReadOnlyList<string> Flags, SharpPlayer? Owner);

	private static string UnresolvedRef(string targetRef, string element, PackageRef reference) =>
		$"{targetRef}/{element}: ref {reference} is unresolved — answer its configure prompt before applying.";

	private static string ExitSourceUnresolved(string reference) => $"Exit {{{{{reference}}}}}: source room is not resolvable.";

	private static string ExitDestinationUnresolved(string reference) => $"Exit {{{{{reference}}}}}: destination is not resolvable.";

	private static string ParentUnresolved(string reference, PackageRef parent) =>
		$"Object {{{{{reference}}}}}: parent {parent} is not resolvable.";

	private static string CustomValueMissing(string what) => $"{what}: UseCustom requires a value.";

	/// <summary>
	/// The first failure the apply can see coming, before it writes anything: a ref that no answer or
	/// object resolves (in engine-managed ref attributes, locks, and where objects are placed or
	/// parented), or a UseCustom decision with no value. An object this apply creates counts as
	/// resolved; whether it lands is the apply's own business.
	/// </summary>
	private async Task<string?> FindForeseeableFailureAsync(
		PackageManifest manifest,
		PackageChangeset changeset,
		IReadOnlyDictionary<string, PackageConflictDecision> decisions,
		Func<PackageRef, string?> resolve,
		CancellationToken cancellationToken)
	{
		var creating = changeset.Objects
			.Where(c => c.Action is PackageObjectAction.Create or PackageObjectAction.RecreateMissing)
			.Select(c => c.Ref)
			.ToHashSet(StringComparer.Ordinal);
		bool Created(PackageRef reference) =>
			reference is { Kind: PackageRefKind.Internal, Package: null } && creating.Contains(reference.Name);
		string? Planned(PackageRef reference) => Created(reference) ? PendingObjid : resolve(reference);

		var manifestByRef = manifest.Objects.ToDictionary(o => o.Ref, StringComparer.Ordinal);

		foreach (var change in changeset.Attributes)
		{
			if (change.Action == PackageAttributeAction.Conflict
				&& decisions.GetValueOrDefault(DecisionKey(change.TargetRef, change.Attribute))
					is { Resolution: PackageConflictResolution.UseCustom, CustomValue: null })
			{
				return CustomValueMissing($"Conflict {change.TargetRef}/{change.Attribute}");
			}

			if (manifestByRef.GetValueOrDefault(change.TargetRef)?.Attributes.GetValueOrDefault(change.Attribute) is null
				&& change.NewValue is not null && PackageRefIndirection.IsRefAttribute(change.Attribute))
			{
				PackageRefSubstitution.Substitute(change.NewValue, Planned, out var unresolved);
				if (unresolved.Count > 0)
				{
					return UnresolvedRef(change.TargetRef, change.Attribute, unresolved[0]);
				}
			}
		}

		foreach (var change in changeset.Structure.Where(s => s.Kind == PackageStructureKind.Lock))
		{
			if (change.Action == PackageStructureAction.Conflict
				&& decisions.GetValueOrDefault(DecisionKey(change.TargetRef, LockDecisionAttribute(change.Element)))
					is { Resolution: PackageConflictResolution.UseCustom, CustomValue: null })
			{
				return CustomValueMissing($"Lock conflict {change.TargetRef}/{change.Element}");
			}

			if (change.Action is PackageStructureAction.Add or PackageStructureAction.Conflict
				&& manifestByRef.TryGetValue(change.TargetRef, out var lockSpec)
				&& LockNames.Fold(lockSpec.Locks).TryGetValue(change.Element, out var rawLock))
			{
				PackageRefSubstitution.Substitute(rawLock, Planned, out var unresolved);
				if (unresolved.Count > 0)
				{
					return UnresolvedRef(change.TargetRef, $"lock:{change.Element}", unresolved[0]);
				}
			}
		}

		async Task<bool> PlacesAsync(PackageRef? reference) =>
			reference is not null
			&& (Created(reference) || await ResolveContainerAsync(reference, resolve, cancellationToken) is not null);

		foreach (var spec in manifest.Objects)
		{
			if (spec.Type == PackageObjectType.Exit && creating.Contains(spec.Ref))
			{
				if (!await PlacesAsync(spec.Location))
				{
					return ExitSourceUnresolved(spec.Ref);
				}

				if (!await PlacesAsync(spec.Destination))
				{
					return ExitDestinationUnresolved(spec.Ref);
				}
			}

			if (spec.Parent is not null && !Created(spec.Parent)
				&& (resolve(spec.Parent) is not string parentObjid || await GetKnownAsync(parentObjid, cancellationToken) is null))
			{
				return ParentUnresolved(spec.Ref, spec.Parent);
			}
		}

		return null;
	}

	/// <summary>
	/// Captures what an apply may change: the package's registry rows, the application slot it
	/// registers, and every existing object the changeset touches, with the attributes it touches on
	/// them. Objects the apply creates are not captured; undoing them is marking them GOING.
	/// </summary>
	private async Task<ApplyBeforeImage> CaptureBeforeImageAsync(
		PackageManifest manifest,
		PackagePlanInputs inputs,
		PackageChangeset changeset,
		IReadOnlyDictionary<string, string> existingObjids,
		CancellationToken cancellationToken)
	{
		var touched = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
		void Touch(string? objid, string? attribute)
		{
			if (objid is null)
			{
				return;
			}

			if (!touched.TryGetValue(objid, out var attributes))
			{
				touched[objid] = attributes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			}

			if (attribute is not null)
			{
				attributes.Add(attribute);
			}
		}

		foreach (var change in changeset.Objects.Where(c => c.Action
			is not (PackageObjectAction.Create or PackageObjectAction.RecreateMissing)))
		{
			Touch(change.Objid, null);
		}

		foreach (var change in changeset.Attributes)
		{
			Touch(change.Objid ?? existingObjids.GetValueOrDefault(change.TargetRef), change.Attribute);
		}

		foreach (var change in changeset.Structure)
		{
			Touch(change.Objid ?? existingObjids.GetValueOrDefault(change.TargetRef),
				change.Kind == PackageStructureKind.AttributeFlag ? change.Attribute : null);
		}

		var live = new List<ObjectImage>();
		foreach (var (objid, attributes) in touched)
		{
			if (await GetKnownAsync(objid, cancellationToken) is not AnySharpObject node)
			{
				continue;
			}

			var obj = node.Object();
			var images = new List<AttributeImage>();
			foreach (var attribute in attributes)
			{
				var path = attribute.Split('`');
				var leaf = await ResolveAttributeLeafAsync(objid, path, cancellationToken);
				images.Add(leaf is null
					? new AttributeImage(path, null, [], null)
					: new AttributeImage(path, leaf.Value, leaf.Flags.Select(f => f.Name).ToList(),
						leaf.Owner is null ? null : await leaf.Owner.WithCancellation(cancellationToken)));
			}

			live.Add(new ObjectImage(
				objid,
				obj.Name,
				await obj.Parent.WithCancellation(cancellationToken) is AnySharpObject parent ? parent.Object().DBRef.ToString() : null,
				await obj.Flags.Value.Select(f => f.Name).ToListAsync(cancellationToken),
				await obj.Powers.Value.Select(p => p.Name).ToListAsync(cancellationToken),
				obj.Locks,
				images));
		}

		var slug = manifest.Application?.Slug;
		return new ApplyBeforeImage(
			manifest.Name,
			inputs.Installed,
			inputs.InstalledObjects,
			inputs.Baselines,
			await registry.GetManagedStructuresAsync(manifest.Name),
			await registry.GetPackageDependenciesAsync(manifest.Name),
			slug,
			slug is not null && await applications.GetApplicationAsync(slug) is RegisteredApplication application ? application : null,
			live);
	}

	/// <summary>
	/// Undoes a failed apply: marks what it created GOING and puts back <paramref name="image"/>. Each
	/// object and the registry are restored independently, so one that cannot be restored does not
	/// strand the rest; what could not be restored is named in the returned error.
	/// </summary>
	private async Task<Error<string>> AbandonAsync(ApplyBeforeImage image, IEnumerable<string> created, string reason)
	{
		var unrestored = new List<string>();
		async Task AttemptAsync(string what, Func<Task> restore)
		{
			try
			{
				await restore();
			}
			catch (Exception ex)
			{
				unrestored.Add($"{what} ({ex.Message})");
			}
		}

		var discarded = new List<string>();
		foreach (var objid in created)
		{
			await AttemptAsync(objid, () => MarkGoingAsync(objid, discarded, CancellationToken.None));
		}

		foreach (var obj in image.Live)
		{
			await AttemptAsync(obj.Objid, () => RestoreObjectAsync(obj));
		}

		await AttemptAsync("the package registry", () => RestoreRegistryAsync(image));

		return new Error<string>(unrestored.Count == 0
			? $"{reason} The apply was undone."
			: $"{reason} The apply was undone except for: {string.Join("; ", unrestored)}.");
	}

	private async Task RestoreObjectAsync(ObjectImage image)
	{
		var cancellationToken = CancellationToken.None;
		if (await GetKnownAsync(image.Objid, cancellationToken) is not AnySharpObject node)
		{
			return;
		}

		var obj = node.Object();
		if (obj.Name != image.Name)
		{
			await mediator.Send(new SetNameCommand(node, MarkupText.Plain(image.Name)), cancellationToken);
		}

		var parent = await obj.Parent.WithCancellation(cancellationToken) is AnySharpObject current ? current.Object().DBRef.ToString() : null;
		if (parent != image.Parent)
		{
			if (image.Parent is not null && await GetKnownAsync(image.Parent, cancellationToken) is AnySharpObject wanted)
			{
				await mediator.Send(new SetObjectParentCommand(node, wanted), cancellationToken);
			}
			else
			{
				await mediator.Send(new UnsetObjectParentCommand(node), cancellationToken);
			}
		}

		var liveFlags = await obj.Flags.Value.Select(f => f.Name).ToListAsync(cancellationToken);
		foreach (var name in image.Flags.Except(liveFlags, StringComparer.OrdinalIgnoreCase))
		{
			if (await flags.GetObjectFlagAsync(name, cancellationToken) is SharpObjectFlag flag)
			{
				await mediator.Send(new SetObjectFlagCommand(node, flag), cancellationToken);
			}
		}

		foreach (var name in liveFlags.Except(image.Flags, StringComparer.OrdinalIgnoreCase))
		{
			if (await flags.GetObjectFlagAsync(name, cancellationToken) is SharpObjectFlag flag)
			{
				await mediator.Send(new UnsetObjectFlagCommand(node, flag), cancellationToken);
			}
		}

		var livePowers = await obj.Powers.Value.Select(p => p.Name).ToListAsync(cancellationToken);
		foreach (var name in image.Powers.Except(livePowers, StringComparer.OrdinalIgnoreCase))
		{
			if (await flags.GetPowerAsync(name, cancellationToken) is SharpPower power)
			{
				await mediator.Send(new SetObjectPowerCommand(node, power), cancellationToken);
			}
		}

		foreach (var name in livePowers.Except(image.Powers, StringComparer.OrdinalIgnoreCase))
		{
			if (await flags.GetPowerAsync(name, cancellationToken) is SharpPower power)
			{
				await mediator.Send(new UnsetObjectPowerCommand(node, power), cancellationToken);
			}
		}

		var executor = new AnySharpObject(await GetPackageManagerWizardAsync(cancellationToken));
		foreach (var (name, data) in image.Locks.Where(l => !obj.Locks.TryGetValue(l.Key, out var now) || now != l.Value))
		{
			await mediator.Send(new SetLockCommand(obj, name, data.LockString, executor)
			{
				Flags = data.Flags,
				Creator = data.Creator,
				PreserveCreator = true
			}, cancellationToken);
		}

		foreach (var name in obj.Locks.Keys.Where(k => !image.Locks.ContainsKey(k)))
		{
			await mediator.Send(new UnsetLockCommand(obj, name, executor), cancellationToken);
		}

		var dbref = obj.DBRef;
		var pmWizard = await GetPackageManagerWizardAsync(cancellationToken);
		foreach (var attribute in image.Attributes)
		{
			if (attribute.Value is null)
			{
				if (await ResolveAttributeLeafAsync(image.Objid, attribute.Path, cancellationToken) is not null)
				{
					await mediator.Send(new ClearAttributeCommand(dbref, attribute.Path), cancellationToken);
				}

				continue;
			}

			await mediator.Send(new SetAttributeCommand(dbref, attribute.Path, attribute.Value, attribute.Owner ?? pmWizard), cancellationToken);
			if (await ResolveAttributeLeafAsync(image.Objid, attribute.Path, cancellationToken) is not SharpAttribute leaf)
			{
				continue;
			}

			var liveAttributeFlags = leaf.Flags.Select(f => f.Name).ToList();
			foreach (var name in attribute.Flags.Except(liveAttributeFlags, StringComparer.OrdinalIgnoreCase))
			{
				if (await attributeStore.GetAttributeFlagAsync(name, cancellationToken) is SharpAttributeFlag flag)
				{
					await mediator.Send(new SetAttributeFlagCommand(dbref, leaf, flag), cancellationToken);
				}
			}

			foreach (var name in liveAttributeFlags.Except(attribute.Flags, StringComparer.OrdinalIgnoreCase))
			{
				if (await attributeStore.GetAttributeFlagAsync(name, cancellationToken) is SharpAttributeFlag flag)
				{
					await mediator.Send(new UnsetAttributeFlagCommand(dbref, leaf, flag), cancellationToken);
				}
			}
		}
	}

	private async Task RestoreRegistryAsync(ApplyBeforeImage image)
	{
		var packageId = image.PackageId;
		if (image.ApplicationSlug is { } slug)
		{
			if (image.Application is not null)
			{
				await applications.UpsertApplicationAsync(image.Application);
			}
			else
			{
				await applications.RemoveApplicationAsync(slug);
			}
		}

		// A first install had no rows at all, and removing the package clears every kind of row it
		// could have written, whether or not the installed-package row itself was reached.
		if (image.Installed is null)
		{
			await registry.RemoveInstalledPackageAsync(packageId);
			return;
		}

		// The package row first: the dependency edges hang off it.
		await registry.UpsertInstalledPackageAsync(image.Installed);

		var objectRefs = image.Objects.Select(o => o.Ref).ToHashSet(StringComparer.Ordinal);
		foreach (var stale in (await registry.GetPackageObjectsAsync(packageId)).Where(o => !objectRefs.Contains(o.Ref)))
		{
			await registry.RemovePackageObjectAsync(packageId, stale.Ref);
		}

		foreach (var record in image.Objects)
		{
			await registry.UpsertPackageObjectAsync(record);
		}

		var attributeKeys = image.Attributes
			.Select(a => (a.Objid, Attribute: a.Attribute.ToUpperInvariant()))
			.ToHashSet();
		foreach (var stale in (await registry.GetManagedAttributesAsync(packageId))
			.Where(a => !attributeKeys.Contains((a.Objid, a.Attribute.ToUpperInvariant()))))
		{
			await registry.RemoveManagedAttributeAsync(packageId, stale.Objid, stale.Attribute);
		}

		foreach (var record in image.Attributes)
		{
			await registry.UpsertManagedAttributeAsync(record);
		}

		var structureObjids = image.Structures.Select(s => s.Objid).ToHashSet(StringComparer.Ordinal);
		foreach (var stale in (await registry.GetManagedStructuresAsync(packageId)).Where(s => !structureObjids.Contains(s.Objid)))
		{
			await registry.RemoveManagedStructureAsync(packageId, stale.Objid);
		}

		foreach (var record in image.Structures)
		{
			await registry.UpsertManagedStructureAsync(record);
		}

		await registry.SetPackageDependenciesAsync(packageId, image.Dependencies);
	}
}
