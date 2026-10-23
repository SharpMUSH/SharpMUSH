using System.Globalization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Models.Wiki.Semantics;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Wiki.Semantics;

/// <summary>
/// Facts as a person types and reads them, in <c>@wiki/fact</c>, <c>wikifact()</c> and the portal: a page
/// reference is written as the page's title and shown as the page, and a page the reader may not see is no page.
/// </summary>
public static class WikiFactEntry
{
	/// <summary>The page's facts with <paramref name="property"/>'s values replaced by <paramref name="values"/>; none clears it.</summary>
	public static IReadOnlyList<WikiPageFact> WithProperty(IEnumerable<WikiPageFact> facts, string property, IEnumerable<string> values)
		=> [.. facts.Where(f => f.Property != property), .. values.Select(v => new WikiPageFact(property, v))];

	/// <summary>
	/// Each typed value as a page states it. A page reference may be typed as a title (<c>Help:Fireball</c>) or a
	/// page id, and names a page only when <paramref name="visibility"/> admits it; any other value is kept as typed,
	/// for the store to check against the schema.
	/// </summary>
	public static async Task<Result<IReadOnlyList<string>>> ResolveAsync(IWikiService wiki, SemanticSchema schema, string property,
		IEnumerable<string> values, WikiVisibility visibility)
	{
		var typed = values.Select(v => v.Trim()).Where(v => v.Length > 0).ToList();
		if (Definition(schema, property) is not { Type: SemanticType.PageRef }) return typed;
		var resolved = new List<string>(typed.Count);
		foreach (var value in typed)
		{
			if (await TargetAsync(wiki, value, visibility) is not WikiPage target)
				return new Error<string>($"{property} names a page that does not exist: '{value}'.");
			resolved.Add(target.Id);
		}
		return resolved;
	}

	/// <summary>
	/// The values of <paramref name="facts"/> a reader is shown, in order: a page reference as
	/// <paramref name="page"/> renders the page it names, left out when <paramref name="visibility"/> does not admit it.
	/// </summary>
	public static async Task<IReadOnlyList<WikiPageFact>> ShownAsync(IWikiService wiki, SemanticSchema schema,
		IEnumerable<WikiPageFact> facts, WikiVisibility visibility, Func<WikiPage, string> page)
	{
		var shown = new List<WikiPageFact>();
		foreach (var fact in facts)
		{
			if (!NamesPage(schema, fact))
				shown.Add(fact);
			else if (await wiki.GetByIdAsync(fact.Value) is WikiPage target && visibility.Admits(target))
				shown.Add(fact with { Value = page(target) });
		}
		return shown;
	}

	/// <summary>
	/// <paramref name="stated"/>, from a reader who was shown only some of <paramref name="stored"/>, with the page
	/// references they were not shown kept: a fact naming a page they may not see stays, unless they stated a
	/// single-valued property's value themselves.
	/// </summary>
	public static async Task<IReadOnlyList<WikiPageFact>> KeepingUnseenAsync(IWikiService wiki, SemanticSchema schema,
		IEnumerable<WikiPageFact> stored, IReadOnlyList<WikiPageFact> stated, WikiVisibility visibility)
	{
		var kept = new List<WikiPageFact>(stated);
		var references = stored.Where(fact => NamesPage(schema, fact)
			&& (Definition(schema, fact.Property) is not { Cardinality: Cardinality.Single } || stated.All(s => s.Property != fact.Property)));
		foreach (var fact in references)
			if (await wiki.GetByIdAsync(fact.Value) is WikiPage target && !visibility.Admits(target))
				kept.Add(fact);
		return kept;
	}

	/// <summary>
	/// Whether <paramref name="fact"/> names a page, and is shown only to a reader who may see it: a value of a page
	/// property, or a page id a page keeps from when its property was one (a property removed or retyped since).
	/// </summary>
	public static bool NamesPage(SemanticSchema schema, WikiPageFact fact)
		=> Definition(schema, fact.Property) is { Type: SemanticType.PageRef } || IsPageId(fact.Value);

	private static bool IsPageId(string value)
		=> value.StartsWith(PageIdPrefix, StringComparison.Ordinal)
			&& long.TryParse(value.AsSpan(PageIdPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out _);

	private const string PageIdPrefix = "wiki_page/";

	/// <summary>The schema's definition of <paramref name="property"/>, or null when it defines none.</summary>
	public static PropertyDefinition? Definition(SemanticSchema schema, string property)
		=> schema.Properties.IsDefault ? null : schema.Properties.FirstOrDefault(p => p.Id == property);

	private static async Task<WikiPage?> TargetAsync(IWikiService wiki, string value, WikiVisibility visibility)
	{
		var id = long.TryParse(value, out _) ? $"wiki_page/{value}" : value;
		if (id.StartsWith("wiki_page/", StringComparison.Ordinal) && await wiki.GetByIdAsync(id) is WikiPage byId)
			return visibility.Admits(byId) ? byId : null;
		var (ns, slug) = WikiHelpers.ResolveTitle(value);
		return await wiki.GetBySlugAsync(slug, ns) is WikiPage byTitle && visibility.Admits(byTitle) ? byTitle : null;
	}
}
