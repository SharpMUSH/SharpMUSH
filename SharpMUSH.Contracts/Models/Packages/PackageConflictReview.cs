namespace SharpMUSH.Library.Models.Packages;

/// <summary>The conflict decision keys understood by both ordinary applies and reviewed batches.</summary>
public static class PackageConflictReview
{
	public static IReadOnlyList<string> Validate(PackageChangeset changeset,
		IReadOnlyList<PackageConflictDecision> decisions, bool rejectUnused = false)
	{
		var required = changeset.Attributes.Where(a => a.Action == PackageAttributeAction.Conflict)
			.Select(a => Key(a.TargetRef, a.Attribute))
			.Concat(changeset.Structure.Where(s => s.Action == PackageStructureAction.Conflict).Select(s => Key(s.TargetRef, $"@LOCK`{s.Element}")))
			.Concat((changeset.Wiki ?? []).Where(w => w.Action == PackageWikiAction.Conflict).Select(w => Key(w.TargetRef, w.Page)))
			.ToHashSet(StringComparer.Ordinal);
		var errors = new List<string>();
		var seen = new HashSet<string>(StringComparer.Ordinal);
		foreach (var decision in decisions)
		{
			if (decision is null || string.IsNullOrWhiteSpace(decision.TargetRef) || string.IsNullOrWhiteSpace(decision.Attribute))
			{ errors.Add("A conflict decision requires a target and attribute."); continue; }
			var key = Key(decision.TargetRef, decision.Attribute);
			if (!seen.Add(key)) errors.Add($"Duplicate decision for {decision.TargetRef}/{decision.Attribute}.");
			if (rejectUnused && !required.Contains(key)) errors.Add($"No reviewed conflict for {decision.TargetRef}/{decision.Attribute}.");
			if (!Enum.IsDefined(decision.Resolution)) errors.Add("Unsupported conflict resolution.");
			if (decision.Resolution == PackageConflictResolution.UseCustom && decision.CustomValue is null)
				errors.Add($"UseCustom requires a value for {decision.TargetRef}/{decision.Attribute}.");
		}
		foreach (var key in required.Where(key => !seen.Contains(key))) errors.Add($"Missing decision for {key.Replace('\0', '/')}.");
		return errors;
	}

	private static string Key(string target, string attribute) => $"{target}\0{attribute.ToUpperInvariant()}";
}
