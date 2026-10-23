using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Models.Wiki.Semantics;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Wiki.Semantics;

/// <summary>
/// A fact query as a person types it, in <c>wikiquery()</c>, <c>@wiki/query</c> and the portal: one condition per
/// entry. <c>&lt;property&gt;=&lt;value&gt;</c> names a value as <c>@wiki/fact</c> sets it, a page by its title, and
/// <c>a|b</c> either of two; <c>!=</c> a value the page does not state; <c>=*</c> any value and <c>!=*</c> none;
/// <c>&gt;</c>, <c>&lt;</c>, <c>&gt;=</c>, <c>&lt;=</c> and <c>=&lt;low&gt;..&lt;high&gt;</c> whole numbers.
/// <c>category=</c> and <c>namespace=</c> say where the pages are, <c>sort=</c> and <c>order=asc|desc</c> how they
/// are ordered, and <c>show=a|b</c> which values are shown beside them.
/// </summary>
public static class WikiFactQueryText
{
	public const string Example = "pf2e:level=1..3";

	/// <summary>The operators a condition is written with, longest first so <c>!=</c> is not read as <c>=</c>.</summary>
	private static readonly string[] Operators = ["!=", ">=", "<=", "=", ">", "<"];

	/// <summary>
	/// The query <paramref name="conditions"/> state, returning <paramref name="take"/> pages at a time. A page a
	/// condition names is found through <paramref name="visibility"/>, so a page the reader may not see is no page.
	/// Whether the query's properties and values fit the schema is the store's to check.
	/// </summary>
	public static async Task<Result<WikiFactQuery>> ParseAsync(IWikiService wiki, SemanticSchema schema,
		IEnumerable<string> conditions, int take, WikiVisibility visibility)
	{
		var facts = new List<WikiFactCondition>();
		var show = new List<string>();
		string? category = null, ns = null, sort = null, order = null;
		foreach (var typed in conditions.Select(c => c.Trim()).Where(c => c.Length > 0))
		{
			if (Split(typed) is not var (key, op, value)) return new Error<string>($"'{typed}' is not a condition like '{Example}'.");
			if (value.Length == 0) return new Error<string>($"{key} needs a value.");

			var setting = key.ToLowerInvariant() switch
			{
				"category" or "namespace" or "sort" or "order" or "show" when op != "=" => $"{key} is set with '=', as in '{key}=…'.",
				"category" when category is not null => "A query names one category.",
				"category" => Set(ref category, WikiHelpers.CategoryKey(value)),
				"namespace" when ns is not null => "A query names one namespace.",
				"namespace" when WikiHelpers.ParseNamespace(value) is not { } parsed => $"No namespace {value} exists.",
				"namespace" => Set(ref ns, WikiHelpers.NamespaceName(WikiHelpers.ParseNamespace(value)!.Value)),
				"sort" when sort is not null => "A query names one sort.",
				"sort" => Set(ref sort, value),
				"order" when value.ToLowerInvariant() is not ("asc" or "desc") => "order is asc or desc.",
				"order" => Set(ref order, value.ToLowerInvariant()),
				"show" => Add(show, value.Split('|').Select(p => p.Trim()).Where(p => p.Length > 0)),
				_ => null,
			};
			if (setting is { Length: > 0 }) return new Error<string>(setting);
			if (setting is not null) continue;

			if (facts.Count == WikiFactQuery.MaxConditions)
				return new Error<string>($"A query takes from 1 to {WikiFactQuery.MaxConditions} conditions.");
			switch (await ConditionAsync(wiki, schema, key, op, value, visibility))
			{
				case Error<string> error: return error;
				case WikiFactCondition fact: facts.Add(fact); break;
			}
		}
		if (order is not null && sort is null) return new Error<string>("order needs a sort, as in 'sort=pf2e:level'.");
		if (facts.Count == 0 && sort is null)
			return new Error<string>($"A query needs a fact condition, like '{Example}'; category and namespace only narrow it.");
		return new WikiFactQuery(facts, take)
		{
			Category = category, Namespace = ns, Sort = sort, Descending = order == "desc", Show = show
		};
	}

	/// <summary>A setting's value stored, and the empty string that says it was one.</summary>
	private static string Set(ref string? setting, string value)
	{
		setting = value;
		return string.Empty;
	}

	private static string Add(List<string> shown, IEnumerable<string> properties)
	{
		shown.AddRange(properties.Except(shown, StringComparer.Ordinal));
		return string.Empty;
	}

	/// <summary>The property, operator and value of a condition, or null when it has no operator after a property.</summary>
	private static (string Key, string Op, string Value)? Split(string typed)
	{
		var at = typed.IndexOfAny(['=', '!', '<', '>']);
		if (at <= 0) return null;
		var op = Operators.FirstOrDefault(o => typed.AsSpan(at).StartsWith(o, StringComparison.Ordinal));
		return op is null ? null : (typed[..at].Trim(), op, typed[(at + op.Length)..].Trim());
	}

	private static async Task<Result<WikiFactCondition>> ConditionAsync(IWikiService wiki, SemanticSchema schema, string property,
		string op, string value, WikiVisibility visibility)
	{
		var definition = WikiFactEntry.Definition(schema, property);
		var integer = definition is { Type: SemanticType.Integer };
		if (op is ">" or "<" or ">=" or "<=")
			return (definition, WikiFactQueries.Integer(value)) switch
			{
				(null, _) => new Error<string>($"No property {property} is defined."),
				_ when !integer => new Error<string>($"{property} is not a whole number, so it has no range."),
				(_, null) => new Error<string>($"{property} is a whole number, not '{value}'."),
				(_, { } bound) => Bounded(property, op, bound),
			};
		if (op == "=" && integer && value.Contains("..", StringComparison.Ordinal))
			return Range(property, value);

		WikiFactCondition stated;
		if (value == "*")
			stated = new WikiFactStated(property);
		else
		{
			var typed = value.Split('|').Select(v => v.Trim()).ToList();
			if (typed.Any(v => v.Length == 0)) return new Error<string>($"{property} has an empty value in '{value}'.");
			switch (await WikiFactEntry.ResolveAsync(wiki, schema, property, typed, visibility))
			{
				case Error<string> error: return error;
				case IReadOnlyList<string> resolved:
					stated = resolved.Count == 1 ? new WikiFactEquals(property, resolved[0]) : new WikiFactOneOf(property, resolved);
					break;
				default: return new Error<string>($"{property} has no value '{value}'.");
			}
		}
		return op == "!=" ? new WikiFactNot(stated) : stated;
	}

	/// <summary><c>&gt;</c>, <c>&lt;</c>, <c>&gt;=</c> and <c>&lt;=</c> as the range of whole numbers they leave.</summary>
	private static Result<WikiFactCondition> Bounded(string property, string op, long bound) => op switch
	{
		">" when bound == long.MaxValue => new Error<string>($"No whole number is greater than {bound}."),
		"<" when bound == long.MinValue => new Error<string>($"No whole number is less than {bound}."),
		">" => (WikiFactCondition)new WikiFactRange(property, bound + 1, null),
		"<" => (WikiFactCondition)new WikiFactRange(property, null, bound - 1),
		">=" => (WikiFactCondition)new WikiFactRange(property, bound, null),
		_ => (WikiFactCondition)new WikiFactRange(property, null, bound),
	};

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
