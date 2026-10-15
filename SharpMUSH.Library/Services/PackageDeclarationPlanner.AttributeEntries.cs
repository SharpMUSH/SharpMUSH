using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;

namespace SharpMUSH.Library.Services;

/// <summary>The standard attribute entries a package declares (<c>attributes:</c>, format 1.5).</summary>
public static partial class PackageDeclarationPlanner
{
	/// <summary>
	/// An entry's limit or enum, as one value: <c>@attribute/limit</c> and <c>@attribute/enum</c> replace each other,
	/// so an upgrade merges them together.
	/// </summary>
	private readonly record struct Restriction(string? Limit, string? Choices, char Delimiter)
	{
		public bool IsNone => Limit is null && Choices is null;

		public static Restriction Of(PackageAttributeEntrySpec spec) => Of(spec.Limit, spec.Enum, spec.EnumDelimiter);

		public static Restriction Of(SharpAttributeEntry entry) => Of(entry.Limit, entry.Enum, entry.EnumDelimiter);

		private static Restriction Of(string? limit, IReadOnlyList<string>? choices, char delimiter)
			=> choices is { Count: > 0 }
				? new(null, string.Join(delimiter, choices), delimiter)
				: new(string.IsNullOrEmpty(limit) ? null : limit, null, ' ');

		public override string ToString() => Limit is not null
			? $"limit {Limit}"
			: Choices is not null
				? Delimiter == ' ' ? $"enum {Choices}" : $"enum {Choices} (delimiter {Delimiter})"
				: "no limit or enum";
	}

	/// <summary>Default flags compared as a set: lowercase, each once, in order.</summary>
	private static string FlagKey(IEnumerable<string> flags)
		=> string.Join(' ', flags.Select(f => f.ToLowerInvariant()).Distinct().Order(StringComparer.Ordinal));

	/// <summary>Default flags as @attribute shows them: uppercase, each once, in declared order.</summary>
	private static string ShowFlags(IEnumerable<string> flags)
		=> string.Join(' ', flags.Select(f => f.ToUpperInvariant()).Distinct()) is { Length: > 0 } shown ? shown : "none";

	/// <summary>What a new entry is made with: its default flags, and its limit or enum when it has one.</summary>
	private static string Declares(PackageAttributeEntrySpec entry)
		=> Restriction.Of(entry) is { Limit: null, Choices: null }
			? $"Default flags {ShowFlags(entry.Flags)}."
			: $"Default flags {ShowFlags(entry.Flags)}; {Restriction.Of(entry)}.";

	/// <summary>
	/// Diffs the declared attribute entries against what the package owned and the game's table. An entry the game
	/// lacks is created and owned; one the game has and the package does not own is the game's, used as it is, unless
	/// its limit or enum contradicts the manifest's; one another package owns blocks. An owned entry merges field by
	/// field (default flags, and the limit-or-enum), and one the package drops is deleted.
	/// </summary>
	private static (List<PackageAttributeEntryWrite> Writes, List<string> Removals, List<PackageAttributeEntrySpec> Owned) PlanAttributeEntries(
		PackageDeclarations declared, PackageDeclarations owned, PackageDeclarationLiveState live, List<PackageDeclarationChange> changes)
	{
		var writes = new List<PackageAttributeEntryWrite>();
		var removals = new List<string>();
		var keep = new List<PackageAttributeEntrySpec>();
		var table = live.AttributeEntries ?? [];

		foreach (var spec in declared.AttributeEntries)
		{
			var current = table.FirstOrDefault(e => NoCase.Equals(e.Name, spec.Name));
			var baseline = owned.AttributeEntries.FirstOrDefault(e => e.Name == spec.Name);
			if (live.OtherOwners.TryGetValue((PackageDeclarationKind.Attribute, spec.Name), out var other))
			{
				changes.Add(new(PackageDeclarationKind.Attribute, spec.Name, PackageDeclarationAction.Blocked, $"It belongs to the {other} package."));
				continue;
			}

			if (current is not null && baseline is null)
			{
				changes.Add(TheGamesEntry(spec, current));
				continue;
			}

			var merged = current is null || baseline is null ? spec : MergeEntry(spec, baseline, current);
			if (live.AttributeFlags is { } known && merged.Flags.Where(f => !known.Contains(f)).ToArray() is { Length: > 0 } unknown)
			{
				changes.Add(new(PackageDeclarationKind.Attribute, spec.Name, PackageDeclarationAction.Blocked,
					$"The game has no attribute flag named {string.Join(", ", unknown)}."));
				continue;
			}

			keep.Add(spec);
			if (current is null)
			{
				var write = new PackageAttributeEntryWrite(merged, spec.Retroactive);
				writes.Add(write);
				changes.Add(Retroactively(new(PackageDeclarationKind.Attribute, spec.Name, PackageDeclarationAction.Create,
					baseline is null ? Declares(merged) : $"It had been deleted, so it is made again. {Declares(merged)}"), write, live));
				continue;
			}

			var flagsChange = FlagKey(merged.Flags) != FlagKey(current.DefaultFlags);
			var restrictionChange = Restriction.Of(merged) != Restriction.Of(current);
			// A version that turns retroactive on gives the existing copies the flags even when the table is unchanged.
			var retroactive = spec.Retroactive && (flagsChange || baseline is { Retroactive: false });
			if (!flagsChange && !restrictionChange && !retroactive)
			{
				changes.Add(new(PackageDeclarationKind.Attribute, spec.Name, PackageDeclarationAction.NoChange));
				continue;
			}

			var update = new PackageAttributeEntryWrite(merged, retroactive);
			writes.Add(update);
			string[] what =
			[
				.. flagsChange ? [$"Default flags {ShowFlags(current.DefaultFlags)} become {ShowFlags(merged.Flags)}."] : Array.Empty<string>(),
				.. restrictionChange ? [$"Its {Restriction.Of(current)} becomes {Restriction.Of(merged)}."] : Array.Empty<string>()
			];
			changes.Add(Retroactively(new(PackageDeclarationKind.Attribute, spec.Name, PackageDeclarationAction.Update,
				what.Length > 0 ? string.Join(" ", what) : null),
				update, live));
		}

		foreach (var baseline in owned.AttributeEntries.Where(b => declared.AttributeEntries.All(d => d.Name != b.Name)))
		{
			if (table.FirstOrDefault(e => NoCase.Equals(e.Name, baseline.Name)) is not { } current)
			{
				continue;
			}

			removals.Add(current.Name);
			changes.Add(new(PackageDeclarationKind.Attribute, current.Name, PackageDeclarationAction.Remove,
				"Values already stored on objects stay as they are."));
		}

		return (writes, removals, keep);
	}

	/// <summary>An entry the game already has: used as it is, unless its limit or enum contradicts the manifest's.</summary>
	private static PackageDeclarationChange TheGamesEntry(PackageAttributeEntrySpec spec, SharpAttributeEntry current)
	{
		var wanted = Restriction.Of(spec);
		var theirs = Restriction.Of(current);
		if (!wanted.IsNone && wanted != theirs)
		{
			return new(PackageDeclarationKind.Attribute, spec.Name, PackageDeclarationAction.Blocked,
				$"The game's entry has {theirs}, and the package needs {wanted}. Change the game's entry with @attribute/limit or @attribute/enum, or delete it with @attribute/delete, to install.");
		}

		var detail = "The game already has this entry; it is left as it is.";
		if (FlagKey(spec.Flags) != FlagKey(current.DefaultFlags))
		{
			detail += $" Its default flags stay {ShowFlags(current.DefaultFlags)}; the package declares {ShowFlags(spec.Flags)}.";
		}

		if (wanted.IsNone && !theirs.IsNone)
		{
			detail += $" Its {theirs} stays.";
		}

		return new(PackageDeclarationKind.Attribute, spec.Name, PackageDeclarationAction.Existing, detail);
	}

	/// <summary>For a retroactive write, how many existing copies get the new default flags, in the change.</summary>
	private static PackageDeclarationChange Retroactively(
		PackageDeclarationChange change, PackageAttributeEntryWrite write, PackageDeclarationLiveState live)
	{
		if (!write.Retroactive)
		{
			return change;
		}

		var wanted = FlagKey(write.Entry.Flags);
		var copies = live.AttributeCopies?.GetValueOrDefault(write.Entry.Name) ?? [];
		var count = copies.Count(flags => FlagKey(flags.Where(f => !NoCase.Equals(f, "branch"))) != wanted);
		var note = $"Retroactive: {count} existing {(count == 1 ? "copy" : "copies")} of the attribute get{(count == 1 ? "s" : "")} the default flags {ShowFlags(write.Entry.Flags)}.";
		return change with { Detail = change.Detail is null ? note : $"{change.Detail} {note}", Retroactive = count };
	}

	/// <summary>An owned entry as the package declares it now, merged against what it declared before and the game's copy.</summary>
	private static PackageAttributeEntrySpec MergeEntry(PackageAttributeEntrySpec spec, PackageAttributeEntrySpec baseline, SharpAttributeEntry current)
	{
		var flags = FlagKey(spec.Flags) == FlagKey(baseline.Flags)
			? (IReadOnlyList<string>)[.. current.DefaultFlags.Select(f => f.ToLowerInvariant()).Distinct()]
			: spec.Flags;
		var restriction = Restriction.Of(spec) == Restriction.Of(baseline)
			? (current.Limit is { Length: > 0 } ? current.Limit : null, current.Enum is { Length: > 0 } ? (IReadOnlyList<string>)current.Enum : null, current.EnumDelimiter)
			: (spec.Limit, spec.Enum, spec.EnumDelimiter);
		return spec with { Flags = flags, Limit = restriction.Item1, Enum = restriction.Item2, EnumDelimiter = restriction.Item3 };
	}
}
