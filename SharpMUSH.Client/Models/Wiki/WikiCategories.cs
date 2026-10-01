using SharpMUSH.Library.Services;

namespace SharpMUSH.Client.Models.Wiki;

/// <summary>One category as the wiki sidebar, home cards and category page present it.</summary>
public sealed record WikiCategoryGroup(string Key, IReadOnlyList<WikiPageSummary> Pages)
{
	/// <summary>The first page image in the category, used as its cover; null when none has one.</summary>
	public string? Cover => Pages.Select(p => p.Image).FirstOrDefault(image => image is not null);

	public bool IsGeneral => Key == WikiHelpers.DefaultCategory;
}

/// <summary>
/// The one place that turns a page's category string into a key, a label and a route, so the
/// sidebar, the home cards and the category page can never disagree. Blank is "general", which
/// sorts after every named category.
/// </summary>
public static class WikiCategories
{
	public static string Key(string? category) => WikiHelpers.NormalizeCategory(category);

	/// <summary>The display label: the localized "General" for the default, else the key with its first letter upper-cased.</summary>
	public static string Label(string? category, string generalLabel)
	{
		var key = Key(category);
		return key == WikiHelpers.DefaultCategory ? generalLabel : char.ToUpperInvariant(key[0]) + key[1..];
	}

	public static string Route(string? category) => $"/wiki/category/{Uri.EscapeDataString(Key(category))}";

	/// <summary>Pages grouped by category: named categories alphabetically, "general" last; pages newest first.</summary>
	public static IReadOnlyList<WikiCategoryGroup> Group(IEnumerable<WikiPageSummary> pages) =>
		pages
			.GroupBy(p => Key(p.Category), StringComparer.Ordinal)
			.Select(g => new WikiCategoryGroup(g.Key, g.OrderByDescending(p => p.UpdatedAt).ToList()))
			.OrderBy(g => g.IsGeneral ? 1 : 0)
			.ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
			.ToList();
}
