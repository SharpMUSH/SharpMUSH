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

	/// <summary>
	/// The display label: the localized "Uncategorized" for no category, else the category's name from
	/// <paramref name="names"/> (its category page's title, translated), else its key as a title.
	/// </summary>
	public static string Label(string? category, string uncategorizedLabel, IReadOnlyDictionary<string, string>? names = null)
	{
		var key = Key(category);
		return key.Length == 0 ? uncategorizedLabel : WikiHelpers.CategoryLabel(key, names);
	}

	/// <summary>
	/// The label of a category in an editor's list: its name from <paramref name="names"/>, else the name as
	/// typed for one just added (the server titles its new category page with it), else its key as a title.
	/// </summary>
	public static string Display(string category, IReadOnlyDictionary<string, string>? names)
	{
		var key = Key(category);
		return names?.ContainsKey(key) != true && category.Trim() is var typed && typed != key
			? typed
			: WikiHelpers.CategoryLabel(key, names);
	}

	/// <summary>Adds <paramref name="entry"/> as typed unless the list already holds its category.</summary>
	public static bool Add(List<string> categories, string? entry)
	{
		var key = Key(entry);
		if (key.Length == 0 || categories.Any(c => Key(c) == key)) return false;
		categories.Add(entry!.Trim());
		return true;
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
