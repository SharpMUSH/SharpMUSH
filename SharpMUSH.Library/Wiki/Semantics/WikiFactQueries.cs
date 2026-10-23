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
	/// The definition of each condition's property, in order, or an error naming the first condition
	/// <paramref name="schema"/> cannot answer: an undefined property, a range over anything but whole numbers, a
	/// value its property's type cannot hold or no fact can state. Too many or no conditions, or a page size out of
	/// range, is an error too.
	/// </summary>
	public static Result<IReadOnlyList<PropertyDefinition>> Check(SemanticSchema schema, WikiFactQuery query)
	{
		if (query.Conditions.Count is 0 or > WikiFactQuery.MaxConditions)
			return new Error<string>($"A query takes from 1 to {WikiFactQuery.MaxConditions} conditions.");
		if (query.Take is < 1 or > WikiFactQuery.MaxTake)
			return new Error<string>($"A query returns from 1 to {WikiFactQuery.MaxTake} pages at a time.");
		var definitions = new List<PropertyDefinition>();
		foreach (var condition in query.Conditions)
		{
			var property = Property(condition);
			if (schema.Properties.FirstOrDefault(p => p.Id == property) is not { } definition)
				return new Error<string>($"No property {property} is defined.");
			if (condition switch
			{
				WikiFactRange when definition.Type != SemanticType.Integer => $"{property} is not a whole number, so it has no range.",
				WikiFactRange { Min: null, Max: null } => $"A range over {property} needs a bound.",
				WikiFactEquals equals when definition.Type == SemanticType.Integer && Integer(equals.Value) is null
					=> $"{property} is a whole number, not '{equals.Value}'.",
				WikiFactEquals equals when Encoding.UTF8.GetByteCount(equals.Value) > WikiSemanticPolicy.MaxKeywordBytes
					=> $"{property} has no value longer than {WikiSemanticPolicy.MaxKeywordBytes} bytes.",
				_ => null
			} is { } refused)
				return new Error<string>(refused);
			definitions.Add(definition);
		}
		return definitions;
	}

	public static string Property(WikiFactCondition condition) => condition switch
	{
		WikiFactEquals equals => equals.Property,
		WikiFactRange range => range.Property
	};

	/// <summary>Whether <paramref name="facts"/> meet <paramref name="condition"/>, typed by <paramref name="definition"/>.</summary>
	public static bool Meets(WikiFactCondition condition, PropertyDefinition definition, IEnumerable<WikiPageFact> facts)
		=> facts.Any(fact => fact.Property == definition.Id && Meets(condition, definition, fact.Value));

	public static bool Meets(WikiFactCondition condition, PropertyDefinition definition, string value) => condition switch
	{
		WikiFactEquals equals when definition.Type == SemanticType.Integer => Integer(value) is { } number && number == Integer(equals.Value),
		WikiFactEquals equals => string.Equals(value, equals.Value, StringComparison.Ordinal),
		WikiFactRange range => Integer(value) is { } number && number >= (range.Min ?? long.MinValue) && number <= (range.Max ?? long.MaxValue)
	};

	/// <summary>Whether <paramref name="page"/> is in the query's namespace and category, when it names them.</summary>
	public static bool InScope(WikiFactQuery query, WikiPage page)
		=> (query.Namespace is null || page.Namespace == query.Namespace)
			&& (query.Category is null || page.Categories.Contains(query.Category));

	public static long? Integer(string value)
		=> long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number) ? number : null;
}
