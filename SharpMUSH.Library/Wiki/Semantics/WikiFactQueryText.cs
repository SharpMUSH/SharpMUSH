using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Models.Wiki.Semantics;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Wiki.Semantics;

/// <summary>
/// A fact query as a person types it, in <c>wikiquery()</c>, <c>@wiki/query</c> and the portal: one condition per
/// entry. <c>&lt;property&gt;=&lt;value&gt;</c> names a value as <c>@wiki/fact</c> sets it, a page by its title;
/// <c>&lt;property&gt;=&lt;low&gt;..&lt;high&gt;</c> a range of whole numbers, either end left open;
/// <c>category=&lt;name&gt;</c> and <c>namespace=&lt;name&gt;</c> where the pages are.
/// </summary>
public static class WikiFactQueryText
{
	public const string Example = "pf2e:level=1..3";

	/// <summary>
	/// The query <paramref name="conditions"/> state, returning <paramref name="take"/> pages at a time. A page a
	/// condition names is found through <paramref name="visibility"/>, so a page the reader may not see is no page.
	/// Whether the query's properties and values fit the schema is the store's to check.
	/// </summary>
	public static async Task<Result<WikiFactQuery>> ParseAsync(IWikiService wiki, SemanticSchema schema,
		IEnumerable<string> conditions, int take, WikiVisibility visibility)
	{
		var facts = new List<WikiFactCondition>();
		string? category = null, ns = null;
		foreach (var typed in conditions.Select(c => c.Trim()).Where(c => c.Length > 0))
		{
			var split = typed.IndexOf('=');
			if (split <= 0) return new Error<string>($"'{typed}' is not a condition like '{Example}'.");
			var (key, value) = (typed[..split].Trim(), typed[(split + 1)..].Trim());
			if (value.Length == 0) return new Error<string>($"{key} needs a value.");

			if (key.Equals("category", StringComparison.OrdinalIgnoreCase))
			{
				if (category is not null) return new Error<string>("A query names one category.");
				category = WikiHelpers.CategoryKey(value);
			}
			else if (key.Equals("namespace", StringComparison.OrdinalIgnoreCase))
			{
				if (ns is not null) return new Error<string>("A query names one namespace.");
				if (WikiHelpers.ParseNamespace(value) is not { } parsed) return new Error<string>($"No namespace {value} exists.");
				ns = WikiHelpers.NamespaceName(parsed);
			}
			else if (facts.Count == WikiFactQuery.MaxConditions)
				return new Error<string>($"A query takes from 1 to {WikiFactQuery.MaxConditions} conditions.");
			else
				switch (await ConditionAsync(wiki, schema, key, value, visibility))
				{
					case Error<string> error: return error;
					case WikiFactCondition fact: facts.Add(fact); break;
				}
		}
		return new WikiFactQuery(facts, take) { Category = category, Namespace = ns };
	}

	private static async Task<Result<WikiFactCondition>> ConditionAsync(IWikiService wiki, SemanticSchema schema, string property,
		string value, WikiVisibility visibility)
	{
		if (WikiFactEntry.Definition(schema, property) is { Type: SemanticType.Integer } && value.Contains("..", StringComparison.Ordinal))
			return Range(property, value);
		return await WikiFactEntry.ResolveAsync(wiki, schema, property, [value], visibility) switch
		{
			IReadOnlyList<string> resolved => (WikiFactCondition)new WikiFactEquals(property, resolved[0]),
			Error<string> error => error
		};
	}

	private static Result<WikiFactCondition> Range(string property, string value)
	{
		var split = value.IndexOf("..", StringComparison.Ordinal);
		var (low, high) = (value[..split].Trim(), value[(split + 2)..].Trim());
		long? Bound(string text) => text.Length == 0 ? null : WikiFactQueries.Integer(text);
		if ((low.Length > 0 && Bound(low) is null) || (high.Length > 0 && Bound(high) is null))
			return new Error<string>($"{property} is a whole number; '{value}' is not a range like '1..3'.");
		return (WikiFactCondition)new WikiFactRange(property, Bound(low), Bound(high));
	}
}
