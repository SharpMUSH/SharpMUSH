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

/// <summary>Object structure application: flags, powers, locks, and attribute flags.</summary>
public partial class PackageInstallService
{
	/// <summary>Synthetic decision-attribute namespacing a lock conflict so it cannot collide with a real attribute name.</summary>
	private static string LockDecisionAttribute(string lockType) => $"@LOCK`{lockType}";

	/// <summary>The resolved structure a package declares on one object — the baseline it writes on apply.</summary>
	private static PackageStructureBaseline? BuildResolvedStructure(PackageObjectSpec spec, Func<PackageRef, string?> resolve)
	{
		// Canonical keys, matching the plan and the live object — see PackagePlanService.
		var locks = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (var (lockType, raw) in LockNames.Fold(spec.Locks))
		{
			locks[lockType] = PackageRefSubstitution.Substitute(raw, resolve, out _);
		}

		var attributeFlags = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
		foreach (var (attrName, attrSpec) in spec.Attributes)
		{
			if (attrSpec.Flags.Count > 0)
			{
				attributeFlags[attrName] = attrSpec.Flags;
			}
		}

		if (spec.Flags.Count == 0 && spec.Powers.Count == 0 && locks.Count == 0 && attributeFlags.Count == 0)
		{
			return null;
		}

		return new PackageStructureBaseline(spec.Flags, spec.Powers, locks, attributeFlags);
	}

	private async Task<string?> ApplyStructureChangeAsync(
		PackageStructureChange change, string objid,
		Dictionary<string, PackageConflictDecision> decisions, List<string> notes, CancellationToken cancellationToken)
	{
		var node = await GetKnownAsync(objid, cancellationToken);
		if (node is null)
		{
			return $"Internal error: object {objid} for structure '{change.Element}' vanished during apply.";
		}

		switch (change.Kind)
		{
			case PackageStructureKind.ObjectFlag:
				if (change.Action is PackageStructureAction.Add or PackageStructureAction.Remove)
				{
					var flag = await flags.GetObjectFlagAsync(change.Element.ToUpperInvariant(), cancellationToken)
						?? await flags.GetObjectFlagAsync(change.Element, cancellationToken);
					if (flag is null)
					{
						notes.Add($"{change.TargetRef}: unknown flag '{change.Element}' skipped.");
					}
					// Through the commands, not the database: the flag set is cached per object, and the
					// commands are what invalidate it.
					else if (change.Action == PackageStructureAction.Add)
					{
						await mediator.Send(new SetObjectFlagCommand(node, flag), cancellationToken);
					}
					else
					{
						await mediator.Send(new UnsetObjectFlagCommand(node, flag), cancellationToken);
					}
				}

				return null;

			case PackageStructureKind.ObjectPower:
				if (change.Action is PackageStructureAction.Add or PackageStructureAction.Remove)
				{
					var power = await flags.GetPowerAsync(change.Element.ToUpperInvariant(), cancellationToken)
						?? await flags.GetPowerAsync(change.Element, cancellationToken);
					if (power is null)
					{
						notes.Add($"{change.TargetRef}: unknown power '{change.Element}' skipped.");
					}
					else if (change.Action == PackageStructureAction.Add)
					{
						await mediator.Send(new SetObjectPowerCommand(node, power), cancellationToken);
					}
					else
					{
						await mediator.Send(new UnsetObjectPowerCommand(node, power), cancellationToken);
					}
				}

				return null;

			case PackageStructureKind.AttributeFlag:
				if (change.Action is PackageStructureAction.Add or PackageStructureAction.Remove)
				{
					var flag = await attributeStore.GetAttributeFlagAsync(change.Element.ToUpperInvariant(), cancellationToken)
						?? await attributeStore.GetAttributeFlagAsync(change.Element, cancellationToken);
					var path = change.Attribute!.Split('`');
					if (flag is null)
					{
						notes.Add($"{change.TargetRef}/{change.Attribute}: unknown attribute flag '{change.Element}' skipped.");
					}
					else if (change.Action == PackageStructureAction.Add)
					{
						// Only flag an attribute that actually exists after apply — a
						// flag the package adds to an attribute the admin deleted locally
						// has nothing to land on.
						var leaf = await ResolveAttributeLeafAsync(objid, path, cancellationToken);
						if (leaf is not null)
						{
							// The flag commands carry the attribute cache keys and the
							// inheritance tag; the store call carried neither.
							await mediator.Send(new SetAttributeFlagCommand(DBRef.Parse(objid), leaf, flag), cancellationToken);
						}
						else
						{
							notes.Add($"{change.TargetRef}/{change.Attribute}: flag '{change.Element}' skipped (attribute not present).");
						}
					}
					else
					{
						// An attribute that no longer resolves has no flag to remove; the store
						// call was a no-op in that case too.
						var leaf = await ResolveAttributeLeafAsync(objid, path, cancellationToken);
						if (leaf is not null)
						{
							await mediator.Send(new UnsetAttributeFlagCommand(DBRef.Parse(objid), leaf, flag), cancellationToken);
						}
					}
				}

				return null;

			case PackageStructureKind.Lock:
				return await ApplyLockChangeAsync(node, change, decisions, cancellationToken);

			default:
				return null;
		}
	}

	private async Task<string?> ApplyLockChangeAsync(
		AnySharpObject node, PackageStructureChange change,
		Dictionary<string, PackageConflictDecision> decisions, CancellationToken cancellationToken)
	{
		var executor = new AnySharpObject(await GetPackageManagerWizardAsync(cancellationToken));
		async Task<string?> SetAsync(string value)
		{
			var name = Enum.TryParse<LockType>(LockNames.Canonical(change.Element), true, out _) ? change.Element : $"user:{change.Element}";
			var result = await mediator.Send(new SetLockCommand(node.Object(), name, value, executor), cancellationToken);
			return result is Error<string> error ? error.Value : null;
		}
		async Task<string?> RemoveAsync()
		{
			var result = await mediator.Send(new UnsetLockCommand(node.Object(), change.Element, executor), cancellationToken);
			return result is Error<string> error ? error.Value : null;
		}

		switch (change.Action)
		{
			case PackageStructureAction.Add:
				return await SetAsync(change.NewValue ?? "");

			case PackageStructureAction.Remove:
				return await RemoveAsync();

			case PackageStructureAction.Conflict:
				{
					var decision = decisions[DecisionKey(change.TargetRef, LockDecisionAttribute(change.Element))];
					switch (decision.Resolution)
					{
						case PackageConflictResolution.TakeTheirs when change.Conflict == PackageConflictKind.ModifyDelete:
							return await RemoveAsync();
						case PackageConflictResolution.TakeTheirs:
							return await SetAsync(change.NewValue ?? "");
						case PackageConflictResolution.UseCustom when decision.CustomValue is not null:
							return await SetAsync(decision.CustomValue);
						case PackageConflictResolution.UseCustom:
							return CustomValueMissing($"Lock conflict {change.TargetRef}/{change.Element}");
						default: // KeepMine — leave the live lock untouched.
							return null;
					}
				}

			default: // Adopt / NoChange / KeepLocal / RemoveBaseline — no write.
				return null;
		}
	}
}
