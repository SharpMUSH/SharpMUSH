using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Models.Portal.Applications;
using SharpMUSH.Library.Utilities;

namespace SharpMUSH.Library.Services;

/// <summary>
/// Preflight: what an apply can see coming it refuses before its first write, so the common
/// failures never reach <see cref="PackageWriteTransaction"/> at all. The passes build their errors
/// from the same helpers, so the two cannot drift apart.
/// </summary>
public partial class PackageInstallService
{
	/// <summary>Stand-in objid for an object this apply will create, while checking refs before it exists.</summary>
	private const string PendingObjid = "#-1";

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
}
