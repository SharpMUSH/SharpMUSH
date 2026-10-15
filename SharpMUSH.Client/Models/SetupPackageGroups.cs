using SharpMUSH.Library.Models.Portal.Setup;

namespace SharpMUSH.Client.Models;

/// <summary>The packages the setup wizard lists under one category.</summary>
public sealed record SetupPackageGroup(string Title, IReadOnlyList<SetupGroupedPackage> Packages);

/// <summary>A package in its group; <paramref name="AddOn"/> when it is listed under a package it builds on.</summary>
public sealed record SetupGroupedPackage(BundledPackageState Package, bool AddOn);

/// <summary>How the setup wizard gathers the bundled packages: by the category their manifests give.</summary>
public static class SetupPackageGroups
{
	/// <summary>
	/// <paramref name="packages"/> by category, ignoring case, in the order the server lists them. A package with no
	/// category is listed under <paramref name="uncategorized"/>, last, with any that name it. In a group, a package
	/// that builds on another of the group's packages is listed under it.
	/// </summary>
	public static IReadOnlyList<SetupPackageGroup> For(IReadOnlyList<BundledPackageState> packages, string uncategorized) => packages
		.GroupBy(p => p.Category ?? uncategorized, StringComparer.OrdinalIgnoreCase)
		.OrderBy(group => string.Equals(group.Key, uncategorized, StringComparison.OrdinalIgnoreCase))
		.Select(group => new SetupPackageGroup(group.Key, Gathered(group.ToList())))
		.ToList();

	/// <summary>
	/// <paramref name="group"/> with each package that builds on no other package of the group followed by those that
	/// build on it, directly or through others of the group, each in the server's order.
	/// </summary>
	private static IReadOnlyList<SetupGroupedPackage> Gathered(IReadOnlyList<BundledPackageState> group)
	{
		var byId = group.ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);
		var roots = group.ToLookup(p => Root(p, byId), StringComparer.OrdinalIgnoreCase);
		return group
			.Where(p => string.Equals(Root(p, byId), p.Id, StringComparison.OrdinalIgnoreCase))
			.SelectMany(root => roots[root.Id].Select(p => new SetupGroupedPackage(p, !ReferenceEquals(p, root))))
			.ToList();
	}

	/// <summary>The package of the group that <paramref name="package"/> builds on, through its first dependency there.</summary>
	private static string Root(BundledPackageState package, Dictionary<string, BundledPackageState> byId)
	{
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		while (seen.Add(package.Id) && package.DependsOn.FirstOrDefault(byId.ContainsKey) is { } baseId)
		{
			package = byId[baseId];
		}

		return package.Id;
	}
}
