using SharpMUSH.Library.Models.Packages;

namespace SharpMUSH.Client.Models;

/// <summary>The packages a remote lists under one category, by subcategory.</summary>
public sealed record PackageLibraryGroup(string Title, IReadOnlyList<PackageLibrarySubcategory> Subcategories)
{
	/// <summary>How many packages the group lists, add-ons included.</summary>
	public int Count => Subcategories.Sum(s => s.Count);
}

/// <summary>The packages of one subcategory in a category; <paramref name="Title"/> is null for those of none.</summary>
public sealed record PackageLibrarySubcategory(string? Title, IReadOnlyList<PackageLibraryFamily> Families)
{
	/// <summary>How many packages the subcategory lists, add-ons included.</summary>
	public int Count => Families.Sum(f => 1 + f.AddOns.Count);
}

/// <summary>A package and the bridges listed under it.</summary>
public sealed record PackageLibraryFamily(PackageRepoEntry Package, IReadOnlyList<PackageRepoEntry> AddOns);

/// <summary>How the package library gathers a remote's packages: by category, each bridge under the package it extends.</summary>
public static class PackageLibraryGroups
{
	/// <summary>
	/// <paramref name="entries"/> by category, ignoring case, categories in alphabetical order and packages by title. A
	/// package with no category is listed last under <paramref name="uncategorizedTitle"/>. In a category, packages of no
	/// subcategory come first, then each subcategory in alphabetical order. A bridge is listed under the
	/// first package it depends on that the remote also lists, whatever that package's category; a bridge over two
	/// packages from elsewhere is listed on its own.
	/// </summary>
	public static IReadOnlyList<PackageLibraryGroup> For(IReadOnlyList<PackageRepoEntry> entries, string uncategorizedTitle)
	{
		var byId = entries.Where(e => e.PackageId is not null)
			.DistinctBy(e => e.PackageId, StringComparer.OrdinalIgnoreCase)
			.ToDictionary(e => e.PackageId!, StringComparer.OrdinalIgnoreCase);
		var baseOf = entries.ToDictionary(e => e, e => Base(e, byId));
		var addOns = entries.Where(e => baseOf[e] is not null)
			.ToLookup(e => baseOf[e]!.PackageId!, StringComparer.OrdinalIgnoreCase);
		return entries
			.Where(e => baseOf[e] is null)
			.GroupBy(e => e.Category ?? string.Empty, StringComparer.OrdinalIgnoreCase)
			.OrderBy(group => group.Key.Length == 0)
			.ThenBy(group => group.Key, StringComparer.CurrentCultureIgnoreCase)
			.Select(group => new PackageLibraryGroup(group.Key.Length == 0 ? uncategorizedTitle : group.Key,
				group.GroupBy(e => e.Subcategory ?? string.Empty, StringComparer.OrdinalIgnoreCase)
					.OrderBy(sub => sub.Key.Length != 0)
					.ThenBy(sub => sub.Key, StringComparer.CurrentCultureIgnoreCase)
					.Select(sub => new PackageLibrarySubcategory(sub.Key.Length == 0 ? null : sub.Key,
						sub.OrderBy(Name, StringComparer.CurrentCultureIgnoreCase)
							.Select(e => new PackageLibraryFamily(e, e.PackageId is { } id
								? addOns[id].OrderBy(Name, StringComparer.CurrentCultureIgnoreCase).ToList()
								: []))
							.ToList()))
					.ToList()))
			.ToList();
	}

	/// <summary>The name a package is listed by: its title, else its id, else its path.</summary>
	public static string Name(PackageRepoEntry entry) => entry.Title ?? entry.PackageId ?? entry.Path;

	/// <summary>Whether <paramref name="entry"/> is listed by <paramref name="search"/>: its title, id, path, description, subcategory or category holds it.</summary>
	public static bool Matches(PackageRepoEntry entry, string search) =>
		string.IsNullOrWhiteSpace(search)
		|| new[] { entry.Title, entry.PackageId, entry.Path, entry.Description, entry.Subcategory, entry.Category }
			.Any(text => text?.Contains(search.Trim(), StringComparison.CurrentCultureIgnoreCase) == true);

	/// <summary>The listed, non-bridge package a bridge is listed under: through its dependencies the remote lists, in order.</summary>
	private static PackageRepoEntry? Base(PackageRepoEntry entry, Dictionary<string, PackageRepoEntry> byId)
	{
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var current = entry;
		while (current.Bridge && current.PackageId is { } id && seen.Add(id)
			&& (current.DependsOn ?? []).FirstOrDefault(byId.ContainsKey) is { } dependency)
		{
			current = byId[dependency];
		}

		return ReferenceEquals(current, entry) || current.Bridge ? null : current;
	}
}
