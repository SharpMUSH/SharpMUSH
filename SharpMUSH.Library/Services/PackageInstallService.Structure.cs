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
		PackageWriteTransaction writes, PackageStructureChange change, string objid,
		Dictionary<string, PackageConflictDecision> decisions, List<string> notes, CancellationToken cancellationToken)
	{
		var node = await GetKnownAsync(objid, cancellationToken);
		if (node is null)
		{
			return $"Internal error: object {objid} for structure '{change.Element}' vanished during apply.";
		}

		if (change.Kind == PackageStructureKind.Lock)
		{
			return await ApplyLockChangeAsync(writes, node, change, decisions, cancellationToken);
		}

		if (change.Action is not (PackageStructureAction.Add or PackageStructureAction.Remove))
		{
			return null;
		}

		var add = change.Action == PackageStructureAction.Add;
		switch (change.Kind)
		{
			case PackageStructureKind.ObjectFlag:
				var flag = await flags.GetObjectFlagAsync(change.Element.ToUpperInvariant(), cancellationToken)
					?? await flags.GetObjectFlagAsync(change.Element, cancellationToken);
				if (flag is null)
				{
					notes.Add($"{change.TargetRef}: unknown flag '{change.Element}' skipped.");
				}
				else if (add)
				{
					await writes.SetFlagAsync(node, flag, cancellationToken);
				}
				else
				{
					await writes.UnsetFlagAsync(node, flag, cancellationToken);
				}

				return null;

			case PackageStructureKind.ObjectPower:
				var power = await flags.GetPowerAsync(change.Element.ToUpperInvariant(), cancellationToken)
					?? await flags.GetPowerAsync(change.Element, cancellationToken);
				if (power is null)
				{
					notes.Add($"{change.TargetRef}: unknown power '{change.Element}' skipped.");
				}
				else if (add)
				{
					await writes.SetPowerAsync(node, power, cancellationToken);
				}
				else
				{
					await writes.UnsetPowerAsync(node, power, cancellationToken);
				}

				return null;

			case PackageStructureKind.AttributeFlag:
				var attributeFlag = await attributeStore.GetAttributeFlagAsync(change.Element.ToUpperInvariant(), cancellationToken)
					?? await attributeStore.GetAttributeFlagAsync(change.Element, cancellationToken);
				var path = change.Attribute!.Split('`');
				if (attributeFlag is null)
				{
					notes.Add($"{change.TargetRef}/{change.Attribute}: unknown attribute flag '{change.Element}' skipped.");
				}
				// Only flag an attribute that exists after apply: a flag the package adds to an
				// attribute the admin deleted locally has nothing to land on.
				else if (add && !await writes.SetAttributeFlagAsync(node.Object().DBRef, path, attributeFlag, cancellationToken))
				{
					notes.Add($"{change.TargetRef}/{change.Attribute}: flag '{change.Element}' skipped (attribute not present).");
				}
				else if (!add)
				{
					await writes.UnsetAttributeFlagAsync(node.Object().DBRef, path, attributeFlag, cancellationToken);
				}

				return null;

			default:
				return null;
		}
	}

	private async Task<string?> ApplyLockChangeAsync(
		PackageWriteTransaction writes, AnySharpObject node, PackageStructureChange change,
		Dictionary<string, PackageConflictDecision> decisions, CancellationToken cancellationToken)
	{
		async Task<string?> SetAsync(string value)
		{
			var name = Enum.TryParse<LockType>(LockNames.Canonical(change.Element), true, out _) ? change.Element : $"user:{change.Element}";
			return await writes.SetLockAsync(node.Object(), name, value, cancellationToken) is Error<string> error ? error.Value : null;
		}
		async Task<string?> RemoveAsync() =>
			await writes.UnsetLockAsync(node.Object(), change.Element, cancellationToken) is Error<string> error ? error.Value : null;

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
