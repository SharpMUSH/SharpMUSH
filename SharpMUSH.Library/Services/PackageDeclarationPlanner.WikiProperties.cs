using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Models.Wiki.Semantics;
using SharpMUSH.Library.Wiki.Semantics;

namespace SharpMUSH.Library.Services;

/// <summary>The wiki fact properties a package declares (<c>wiki_properties:</c>, format 1.17).</summary>
public static partial class PackageDeclarationPlanner
{
	/// <summary>A property as the manifest and the registry hold it.</summary>
	public static PackageWikiPropertySpec WikiPropertySpec(PropertyDefinition property)
		=> new(property.Id, WikiFactProperties.TypeName(property.Type), property.Cardinality == Cardinality.Multiple,
			[.. (property.AllowedKeywords.IsDefault ? [] : property.AllowedKeywords).Select(k => k.Value)],
			property.Label, property.Description);

	/// <summary>A property as <c>@wiki/property</c> defines it.</summary>
	public static PropertyDefinition WikiPropertyDefinition(PackageWikiPropertySpec spec)
		=> new(spec.Id, WikiFactProperties.SchemaVersion, WikiFactProperties.ParseType(spec.Type) ?? SemanticType.Keyword,
			spec.Multiple ? Cardinality.Multiple : Cardinality.Single, [.. spec.Values.Select(v => new Keyword(v))],
			spec.Label, spec.Description);

	/// <summary>
	/// Diffs the declared wiki properties against what the package owned and the world's properties. A property the
	/// world lacks is defined and owned; one the world defines and the package does not own is the world's, used as it
	/// is when it takes what the package's pages state; one another package owns blocks. An owned property merges field
	/// by field, and one the package drops is removed. Pages keep the facts they state either way.
	/// </summary>
	private static (List<PropertyDefinition> Writes, List<string> Removals, List<PackageWikiPropertySpec> Owned) PlanWikiProperties(
		PackageDeclarations declared, PackageDeclarations owned, PackageDeclarationLiveState live, List<PackageDeclarationChange> changes)
	{
		var writes = new List<PropertyDefinition>();
		var removals = new List<string>();
		var keep = new List<PackageWikiPropertySpec>();
		var world = (live.WikiProperties ?? []).ToDictionary(p => p.Id, WikiPropertySpec, StringComparer.Ordinal);

		foreach (var spec in declared.WikiFactProperties)
		{
			var current = world.GetValueOrDefault(spec.Id);
			var baseline = owned.WikiFactProperties.FirstOrDefault(p => p.Id == spec.Id);
			if (live.OtherOwners.TryGetValue((PackageDeclarationKind.WikiProperty, spec.Id), out var other))
			{
				changes.Add(new(PackageDeclarationKind.WikiProperty, spec.Id, PackageDeclarationAction.Blocked, $"It belongs to the {other} package."));
				continue;
			}

			if (current is not null && baseline is null)
			{
				changes.Add(TheWorldsProperty(spec, current));
				continue;
			}

			if (current is null)
			{
				keep.Add(spec);
				writes.Add(WikiPropertyDefinition(spec));
				changes.Add(new(PackageDeclarationKind.WikiProperty, spec.Id, PackageDeclarationAction.Create,
					baseline is null ? Describe(spec) : $"It had been removed, so it is defined again. {Describe(spec)}"));
				continue;
			}

			// Fields merge one by one, so a type staff changed and values the package added can make a property the wiki refuses.
			var merged = MergeWikiProperty(spec, baseline!, current);
			if (WikiFactProperties.Checked(WikiPropertyDefinition(merged)) is Error<string> refused)
			{
				changes.Add(new(PackageDeclarationKind.WikiProperty, spec.Id, PackageDeclarationAction.Blocked,
					$"The world's property is {Describe(current)} The package's changes to it would make one the wiki refuses: {refused.Value} Change it with @wiki/property to install."));
				continue;
			}

			keep.Add(spec);
			if (merged.SameAs(current))
			{
				changes.Add(new(PackageDeclarationKind.WikiProperty, spec.Id, PackageDeclarationAction.NoChange));
				continue;
			}

			writes.Add(WikiPropertyDefinition(merged));
			changes.Add(new(PackageDeclarationKind.WikiProperty, spec.Id, PackageDeclarationAction.Update,
				Describe(current) == Describe(merged)
					? "New label or description."
					: $"{Describe(current)} becomes {Describe(merged)} Pages that state it keep their facts."));
		}

		foreach (var baseline in owned.WikiFactProperties.Where(b => declared.WikiFactProperties.All(d => d.Id != b.Id) && world.ContainsKey(b.Id)))
		{
			removals.Add(baseline.Id);
			changes.Add(new(PackageDeclarationKind.WikiProperty, baseline.Id, PackageDeclarationAction.Remove,
				"Pages that state it keep their facts."));
		}

		return (writes, removals, keep);
	}

	/// <summary>
	/// A property the world already defines: used as it is when it takes the values the package's pages state, and
	/// blocking the install when its type, its number of values, or its keywords do not.
	/// </summary>
	private static PackageDeclarationChange TheWorldsProperty(PackageWikiPropertySpec spec, PackageWikiPropertySpec current)
	{
		var narrower = current.Values.Count > 0 && spec.Values.Except(current.Values, StringComparer.Ordinal).Any();
		if (spec.Type != current.Type || spec.Multiple != current.Multiple || narrower)
		{
			return new(PackageDeclarationKind.WikiProperty, spec.Id, PackageDeclarationAction.Blocked,
				$"The world's property is {Describe(current)} The package needs {Describe(spec)} Change it with @wiki/property, or remove it, to install.");
		}

		return new(PackageDeclarationKind.WikiProperty, spec.Id, PackageDeclarationAction.Existing,
			"The world already defines this property; it is left as it is.");
	}

	/// <summary>An owned property as the package declares it now, merged against what it declared before and the world's.</summary>
	private static PackageWikiPropertySpec MergeWikiProperty(PackageWikiPropertySpec spec, PackageWikiPropertySpec baseline,
		PackageWikiPropertySpec current)
		=> new(spec.Id,
			Merge(spec.Type, baseline.Type, current.Type),
			Merge(spec.Multiple, baseline.Multiple, current.Multiple),
			spec.Values.SequenceEqual(baseline.Values) ? current.Values : spec.Values,
			Merge(spec.Label, baseline.Label, current.Label),
			Merge(spec.Description, baseline.Description, current.Description));

	/// <summary>A property's type, values and keywords, as a sentence.</summary>
	private static string Describe(PackageWikiPropertySpec spec)
		=> $"{spec.Type}, {(spec.Multiple ? "any number of values" : "one value")}"
			+ (spec.Values.Count > 0 ? $": {string.Join(", ", spec.Values)}." : ".");
}
