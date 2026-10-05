using SharpMUSH.Library.Services;

namespace SharpMUSH.Client.Models.Wiki;

/// <summary>One category as the wiki sidebar and home cards present it.</summary>
public sealed record WikiCategoryGroup(string Key, IReadOnlyList<WikiPageSummary> Pages)
{
	/// <summary>The first page image in the category, used as its cover; null when none has one.</summary>
	public string? Cover => Pages.Select(p => p.Image).FirstOrDefault(image => image is not null);

	/// <summary>The pages in no category at all, which have no category page to link to.</summary>
	public bool IsUncategorized => Key.Length == 0;
}

/// <summary>
/// The one place that turns a category name into a key, a label and a route, so the sidebar, the home
/// cards, a page's category bar and the category page can never disagree. A page is in every category
/// on its list, so it appears in each of those groups; a page in none is
/// grouped as uncategorized, which sorts after every named category.
/// </summary>
public static class WikiCategories
{
	public static string Key(string? category) => WikiHelpers.CategoryKey(category);

	/// <summary>The display label: the localized "Uncategorized" for no category, else the category's title.</summary>
	public static string Label(string? category, string uncategorizedLabel)
	{
		var key = Key(category);
		return key.Length == 0 ? uncategorizedLabel : WikiHelpers.CategoryLabel(key);
	}

	public static string Route(string category) => WikiRoutes.CategoryPath(category);

	/// <summary>
	/// Pages grouped by category: named categories alphabetically, uncategorized last; pages newest first.
	/// Pages in the category namespace are categories themselves and are left out of the groups.
	/// </summary>
	public static IReadOnlyList<WikiCategoryGroup> Group(IEnumerable<WikiPageSummary> pages) =>
		pages
			.Where(p => !IsCategoryPage(p))
			.SelectMany(p => (p.Categories.Count == 0 ? [string.Empty] : p.Categories.Select(Key)).Distinct()
				.Select(key => (Key: key, Page: p)))
			.GroupBy(x => x.Key, x => x.Page, StringComparer.Ordinal)
			.Select(g => new WikiCategoryGroup(g.Key, g.OrderByDescending(p => p.UpdatedAt).ToList()))
			.OrderBy(g => g.IsUncategorized ? 1 : 0)
			.ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
			.ToList();

	/// <summary>True when the row is a category's own page (the <c>category</c> namespace).</summary>
	public static bool IsCategoryPage(WikiPageSummary page) =>
		string.Equals(page.Namespace, "category", StringComparison.OrdinalIgnoreCase);
}
