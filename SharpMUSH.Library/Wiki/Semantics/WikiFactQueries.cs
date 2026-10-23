using System.Globalization;
using System.Text;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Models.Wiki.Semantics;

namespace SharpMUSH.Library.Wiki.Semantics;

/// <summary>What a fact query means, whichever store answers it: which queries are valid, and which pages meet one.</summary>
public static class WikiFactQueries
{
	/// <summary>
	/// The definition of each condition's property, in order, or an error naming the first thing
	/// <paramref name="schema"/> cannot answer: an undefined property, a range over anything but whole numbers, a
	/// value its property's type cannot hold or no fact can state, or a sort or shown property it does not define.
	/// Too many conditions, none a page meets by stating a value (and no sort), or a page size out of range, is an
	/// error too.
	/// </summary>
	public static Result<IReadOnlyList<PropertyDefinition>> Check(SemanticSchema schema, WikiFactQuery query)
	{
		if (query.Conditions.Count > WikiFactQuery.MaxConditions || (query.Conditions.Count == 0 && query.Sort is null))
			return new Error<string>($"A query takes from 1 to {WikiFactQuery.MaxConditions} conditions.");
		if (query.Sort is null && !query.Conditions.Any(Drives))
			return new Error<string>("A query needs a condition a page meets by stating a value; != alone finds no pages.");
		if (query.Take is < 1 or > WikiFactQuery.MaxTake)
			return new Error<string>($"A query returns from 1 to {WikiFactQuery.MaxTake} pages at a time.");
		if (query.Show.Count > WikiFactQuery.MaxShown)
			return new Error<string>($"A query shows at most {WikiFactQuery.MaxShown} properties.");
		if ((query.Sort is null ? query.Show : [query.Sort, .. query.Show]).FirstOrDefault(p => Definition(schema, p) is null) is { } undefined)
			return new Error<string>($"No property {undefined} is defined.");

		if (query.Sort is { } sort && Definition(schema, sort) is { Type: SemanticType.PageRef })
			return new Error<string>($"{sort} names pages, so it does not sort them; sort by a number or a keyword.");

		var definitions = new List<PropertyDefinition>();
		foreach (var condition in query.Conditions)
		{
			var property = Property(condition);
			if (Definition(schema, property) is not { } definition)
				return new Error<string>($"No property {property} is defined.");
			if (Refused(condition, definition) is { } refused)
				return new Error<string>(refused);
			definitions.Add(definition);
		}
		return definitions;
	}

	private static PropertyDefinition? Definition(SemanticSchema schema, string property)
		=> schema.Properties.IsDefault ? null : schema.Properties.FirstOrDefault(p => p.Id == property);

	private static string? Refused(WikiFactCondition condition, PropertyDefinition definition) => condition switch
	{
		WikiFactRange when definition.Type != SemanticType.Integer => $"{definition.Id} is not a whole number, so it has no range.",
		WikiFactRange { Min: null, Max: null } => $"A range over {definition.Id} needs a bound.",
		WikiFactRange => null,
		WikiFactEquals equals => Refused(equals.Value, definition),
		WikiFactOneOf { Values.Count: 0 or > WikiFactQuery.MaxValues }
			=> $"A condition on {definition.Id} names from 1 to {WikiFactQuery.MaxValues} values.",
		WikiFactOneOf oneOf => oneOf.Values.Select(value => Refused(value, definition)).FirstOrDefault(refused => refused is not null),
		WikiFactStated => null,
		WikiFactNot { Condition: WikiFactNot } => $"A condition on {definition.Id} is negated twice.",
		WikiFactNot not => Refused(not.Condition, definition),
	};

	private static string? Refused(string value, PropertyDefinition definition)
		=> definition.Type == SemanticType.Integer && Integer(value) is null ? $"{definition.Id} is a whole number, not '{value}'."
			: Encoding.UTF8.GetByteCount(value) > WikiSemanticPolicy.MaxKeywordBytes
				? $"{definition.Id} has no value longer than {WikiSemanticPolicy.MaxKeywordBytes} bytes."
				: null;

	public static string Property(WikiFactCondition condition) => condition switch
	{
		WikiFactEquals equals => equals.Property,
		WikiFactRange range => range.Property,
		WikiFactOneOf oneOf => oneOf.Property,
		WikiFactStated stated => stated.Property,
		WikiFactNot not => Property(not.Condition),
	};

	/// <summary>Whether a page meets <paramref name="condition"/> by stating a value, so its index entries can drive a query.</summary>
	public static bool Drives(WikiFactCondition condition) => condition is not WikiFactNot;

	/// <summary>Whether <paramref name="facts"/> meet <paramref name="condition"/>, typed by <paramref name="definition"/>.</summary>
	public static bool Meets(WikiFactCondition condition, PropertyDefinition definition, IEnumerable<WikiPageFact> facts)
		=> condition is WikiFactNot not
			? !Meets(not.Condition, definition, facts)
			: facts.Any(fact => fact.Property == definition.Id && Meets(condition, definition, fact.Value));

	/// <summary>
	/// Whether one stated <paramref name="value"/> meets <paramref name="condition"/>. A negation is met by what a page
	/// does not state, so no one value meets it.
	/// </summary>
	public static bool Meets(WikiFactCondition condition, PropertyDefinition definition, string value) => condition switch
	{
		WikiFactEquals equals => SameValue(definition, value, equals.Value),
		WikiFactOneOf oneOf => oneOf.Values.Any(wanted => SameValue(definition, value, wanted)),
		WikiFactRange range => Integer(value) is { } number && number >= (range.Min ?? long.MinValue) && number <= (range.Max ?? long.MaxValue),
		WikiFactStated => true,
		WikiFactNot => false,
	};

	private static bool SameValue(PropertyDefinition definition, string value, string wanted)
		=> definition.Type == SemanticType.Integer
			? Integer(value) is { } number && number == Integer(wanted)
			: string.Equals(value, wanted, StringComparison.Ordinal);

	/// <summary>Whether <paramref name="page"/> is in the query's namespace and category, when it names them.</summary>
	public static bool InScope(WikiFactQuery query, WikiPage page)
		=> (query.Namespace is null || page.Namespace == query.Namespace)
			&& (query.Category is null || page.Categories.Contains(query.Category));

	public static long? Integer(string value)
		=> long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number) ? number : null;
}
