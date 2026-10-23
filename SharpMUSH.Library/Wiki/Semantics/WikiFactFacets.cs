using System.Globalization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Models.Wiki.Semantics;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Wiki.Semantics;

/// <summary>
/// A category's facets, as Semantic Drilldown shows them: for each defined property its pages state, the values they
/// state and how many pages state each, among the pages that meet the conditions already chosen. A property's own
/// conditions are left out of its counts, so the values a reader could pick instead stay listed. A whole-number
/// property with more than <see cref="MaxRanges"/> values is counted in that many ranges instead.
/// </summary>
public static class WikiFactFacets
{
	/// <summary>The most pages of a category counted; past them the counts say they are partial.</summary>
	public const int MaxPages = 5000;

	/// <summary>The most values listed for one property, the most stated first.</summary>
	public const int MaxValues = 12;

	/// <summary>The most ranges a whole-number property's values are counted in.</summary>
	public const int MaxRanges = 6;

	private const int Batch = 200;

	/// <summary>
	/// The facets of <paramref name="category"/>'s pages that <paramref name="visibility"/> admits and that meet every
	/// one of <paramref name="conditions"/>, typed as <c>wikiquery()</c> takes them. Only fact conditions are taken:
	/// the category is the one counted, and a facet has no sort or shown values.
	/// </summary>
	public static async Task<WikiFacetsResult> CountAsync(IWikiService wiki, string category, IEnumerable<string> conditions,
		WikiVisibility visibility)
	{
		var schema = await wiki.GetFactSchemaAsync();
		var typed = conditions.Select(c => c.Trim()).Where(c => c.Length > 0).ToList();
		var filters = new List<(WikiFactCondition Condition, PropertyDefinition Definition)>();
		if (typed.Any(IsSetting))
			return new Error<string>($"Facets take fact conditions only, like '{WikiFactQueryText.Example}'.");
		if (typed.Count > 0)
		{
			switch (await WikiFactQueryText.ParseAsync(wiki, schema, typed, WikiFactQuery.MaxTake, visibility))
			{
				case Error<string> error:
					return error;
				case WikiFactQuery query:
					switch (WikiFactQueries.CheckConditions(schema, query.Conditions))
					{
						case Error<string> error: return error;
						case IReadOnlyList<PropertyDefinition> definitions: filters.AddRange(query.Conditions.Zip(definitions)); break;
					}
					break;
			}
		}

		var key = WikiHelpers.CategoryKey(category);
		var stated = new Dictionary<string, List<IReadOnlyList<string>>>(StringComparer.Ordinal);
		var (read, matched, partial) = (0, 0, false);
		for (var skip = 0; !partial; skip += Batch)
		{
			var pages = await wiki.GetByCategoryAsync(key, skip, Batch, visibility);
			foreach (var page in pages)
			{
				if (read++ == MaxPages)
				{
					partial = true;
					break;
				}
				var failed = filters.Where(f => !WikiFactQueries.Meets(f.Condition, f.Definition, page.Facts))
					.Select(f => f.Definition.Id).Distinct(StringComparer.Ordinal).Take(2).ToList();
				if (failed.Count > 1) continue;
				if (failed.Count == 0) matched++;
				foreach (var property in page.Facts.GroupBy(f => f.Property).Where(p => failed.Count == 0 || p.Key == failed[0]))
				{
					if (!stated.TryGetValue(property.Key, out var values)) stated[property.Key] = values = [];
					values.Add([.. property.Select(f => f.Value).Distinct(StringComparer.Ordinal)]);
				}
			}
			if (pages.Count < Batch) break;
		}

		var facets = new List<WikiFacet>();
		foreach (var definition in schema.Properties.IsDefault ? [] : schema.Properties)
		{
			if (!stated.TryGetValue(definition.Id, out var values)) continue;
			var counted = definition.Type switch
			{
				SemanticType.Integer => Numbers(values),
				SemanticType.PageRef => await PagesAsync(wiki, values, visibility),
				_ => Counted(values, value => (value, value)),
			};
			if (counted.Count == 0) continue;
			facets.Add(new WikiFacet(definition.Id, WikiFactEntry.Label(schema, definition.Id), definition.Type,
				counted.Count > MaxValues ? counted[..MaxValues] : counted, Math.Max(0, counted.Count - MaxValues)));
		}
		return new WikiFacets(matched, partial, facets);
	}

	/// <summary>Whether <paramref name="typed"/> sets where a query looks or what it shows, rather than stating a condition.</summary>
	private static bool IsSetting(string typed)
		=> typed.IndexOfAny(['=', '!', '<', '>']) is > 0 and var at
			&& typed[..at].Trim().ToLowerInvariant() is "category" or "namespace" or "sort" or "order" or "show";

	/// <summary>Each value and how many pages state it, the most stated first, then by label.</summary>
	private static List<WikiFacetValue> Counted(List<IReadOnlyList<string>> pages, Func<string, (string Value, string Label)?> shown)
		=> pages.SelectMany(values => values)
			.GroupBy(value => value, StringComparer.Ordinal)
			.Select(group => shown(group.Key) is { } pair ? new WikiFacetValue(pair.Value, pair.Label, group.Count()) : null)
			.OfType<WikiFacetValue>()
			.OrderByDescending(v => v.Count)
			.ThenBy(v => v.Label, StringComparer.OrdinalIgnoreCase)
			.ToList();

	/// <summary>A page value as the page it names, typed as <c>namespace:slug</c>; a page the reader may not see is left out.</summary>
	private static async Task<List<WikiFacetValue>> PagesAsync(IWikiService wiki, List<IReadOnlyList<string>> pages, WikiVisibility visibility)
	{
		var named = new Dictionary<string, (string Value, string Label)?>(StringComparer.Ordinal);
		foreach (var id in pages.SelectMany(values => values).Distinct(StringComparer.Ordinal))
			named[id] = await wiki.GetByIdAsync(id) is WikiPage target && visibility.Admits(target)
				? ($"{target.Namespace}:{target.Slug}", target.Title)
				: null;
		return Counted(pages, id => named[id]);
	}

	/// <summary>
	/// Whole numbers in their own order: each value while there are at most <see cref="MaxRanges"/>, else that many
	/// ranges of equal width from the least to the greatest, written <c>low..high</c>. A page is counted once in a range
	/// however many of its values fall there.
	/// </summary>
	private static List<WikiFacetValue> Numbers(List<IReadOnlyList<string>> pages)
	{
		var numbers = pages.Select(values => values.Select(WikiFactQueries.Integer).OfType<long>().ToHashSet()).ToList();
		var distinct = numbers.SelectMany(n => n).Distinct().Order().ToList();
		if (distinct.Count <= MaxRanges)
			return [.. distinct.Select(n => new WikiFacetValue(Text(n), Text(n), numbers.Count(page => page.Contains(n))))];

		var (low, high) = (distinct[0], distinct[^1]);
		// Computed in decimal: the width of a range near long's limits does not fit in a long.
		var width = (long)Math.Ceiling(((decimal)high - low + 1) / MaxRanges);
		var ranges = new List<WikiFacetValue>();
		for (var start = (decimal)low; start <= high; start += width)
		{
			var (from, to) = ((long)start, (long)Math.Min(high, start + width - 1));
			var count = numbers.Count(page => page.Any(n => n >= from && n <= to));
			if (count > 0) ranges.Add(new WikiFacetValue($"{Text(from)}..{Text(to)}", from == to ? Text(from) : $"{Text(from)} to {Text(to)}", count));
		}
		return ranges;
	}

	private static string Text(long number) => number.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// How many pages meet the chosen conditions, whether only the first <see cref="WikiFactFacets.MaxPages"/> of the
/// category were counted, and the facets of those pages in the schema's order, by property id.
/// </summary>
public sealed record WikiFacets(int Pages, bool Partial, IReadOnlyList<WikiFacet> Facets);

/// <summary>
/// A property's values among the pages, shown under <paramref name="Label"/>, and how many values
/// <paramref name="More"/> were left out.
/// </summary>
public sealed record WikiFacet(string Property, string Label, SemanticType Type, IReadOnlyList<WikiFacetValue> Values, int More);

/// <summary>
/// One value, or one range of whole numbers, and how many pages state it. <paramref name="Value"/> is written as a
/// condition takes it after <c>property=</c>; <paramref name="Label"/> is what a reader is shown.
/// </summary>
public sealed record WikiFacetValue(string Value, string Label, int Count);

public union WikiFacetsResult(WikiFacets, Error<string>);
