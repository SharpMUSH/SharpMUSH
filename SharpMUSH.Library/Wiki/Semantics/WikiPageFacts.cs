using System.Globalization;
using System.Text;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Models.Wiki.Semantics;

namespace SharpMUSH.Library.Wiki.Semantics;

/// <summary>
/// A page's facts as its metadata holds them, and the canonical facts a semantic projection indexes from them.
/// Normalizing checks the metadata form for shape only; a store with a fact schema types it against that schema
/// when it writes it, and the projection indexes the typed form.
/// </summary>
public static class WikiPageFacts
{
	/// <summary>Where every fact a page states about itself comes from.</summary>
	public const string AuthoredField = "page-metadata";

	/// <summary>
	/// Trims each property and value, drops repeats, and sorts by property and then value (ordinal). A property
	/// that is not a qualified key, an empty or over-long value, more than 128 facts, or more than 32 values for
	/// one property is an error naming it.
	/// </summary>
	public static Result<IReadOnlyList<WikiPageFact>> Normalize(IEnumerable<WikiPageFact> facts)
	{
		var normalized = new SortedSet<(string Property, string Value)>(Comparer<(string Property, string Value)>.Create((a, b) =>
			string.CompareOrdinal(a.Property, b.Property) is var byProperty and not 0 ? byProperty : string.CompareOrdinal(a.Value, b.Value)));
		foreach (var fact in facts)
		{
			var property = fact.Property?.Trim() ?? string.Empty;
			var value = fact.Value?.Trim() ?? string.Empty;
			if (!CanonicalFacts.QualifiedProperty(property)) return new Error<string>($"'{property}' is not a property key like 'pf2e:level'.");
			if (value.Length == 0) return new Error<string>($"{property} needs a value.");
			if (Encoding.UTF8.GetByteCount(value) > WikiSemanticPolicy.MaxKeywordBytes || value.Any(char.IsControl))
				return new Error<string>($"{property}'s value is longer than {WikiSemanticPolicy.MaxKeywordBytes} bytes or has a control character.");
			normalized.Add((property, value));
		}
		if (normalized.Count > WikiSemanticPolicy.MaxFacts) return new Error<string>($"A page states at most {WikiSemanticPolicy.MaxFacts} facts.");
		if (normalized.GroupBy(f => f.Property).FirstOrDefault(g => g.Count() > WikiSemanticPolicy.MaxValuesPerProperty) is { } crowded)
			return new Error<string>($"{crowded.Key} has more than {WikiSemanticPolicy.MaxValuesPerProperty} values.");
		return normalized.Select(f => new WikiPageFact(f.Property, f.Value)).ToList();
	}

	/// <summary>
	/// The canonical facts <paramref name="subject"/>'s metadata states under <paramref name="schema"/>: each value
	/// read as its property's type, then validated as any fact set is. A page reference is taken as written;
	/// whether its target exists is the caller's to check.
	/// </summary>
	public static ValidationResult ToCanonical(SemanticSchema schema, PageRef subject, IEnumerable<WikiPageFact> facts,
		CancellationToken cancellationToken = default)
	{
		var definitions = (schema.Properties.IsDefault ? [] : schema.Properties).ToDictionary(p => p.Id, StringComparer.Ordinal);
		var provenance = new FactProvenance(string.Empty, string.Empty, subject.Id, AuthoredField, false);
		var typed = new List<SemanticFact>();
		foreach (var fact in facts)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (!definitions.TryGetValue(fact.Property, out var definition))
				return new ValidationFailure(ValidationCode.UnknownProperty, fact.Property);
			if (Read(definition.Type, fact.Value) is not { } value)
				return new ValidationFailure(ValidationCode.WrongType, $"{fact.Property} is {Describe(definition.Type)}, not '{fact.Value}'.");
			typed.Add(new SemanticFact(subject, fact.Property, value, provenance));
		}
		return CanonicalFacts.Validate(schema, typed, cancellationToken);
	}

	/// <summary>The metadata form of canonical facts: how a page would state them.</summary>
	public static IReadOnlyList<WikiPageFact> FromCanonical(IEnumerable<SemanticFact> facts)
		=> Normalize(facts.Select(f => new WikiPageFact(f.Property, Write(f.Value)))) switch
		{
			IReadOnlyList<WikiPageFact> normalized => normalized,
			Error<string> error => throw new ArgumentException(error.Value, nameof(facts))
		};

	private static SemanticValue? Read(SemanticType type, string value) => type switch
	{
		SemanticType.Integer when long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var integer) => integer,
		SemanticType.Boolean when value == "true" => true,
		SemanticType.Boolean when value == "false" => false,
		SemanticType.Keyword => new Keyword(value),
		SemanticType.PageRef => new PageRef(value),
		SemanticType.Text => new FactText(value),
		SemanticType.Decimal when WikiFactQueries.Decimal(value) is { } number => number,
		SemanticType.Date when WikiFactQueries.Date(value) is { } date => date,
		_ => null
	};

	private static string Write(SemanticValue value) => value switch
	{
		PageRef reference => reference.Id,
		long integer => integer.ToString(CultureInfo.InvariantCulture),
		bool boolean => boolean ? "true" : "false",
		Keyword keyword => keyword.Value,
		FactText text => text.Value,
		double number => WikiFactQueries.WriteDecimal(number),
		DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
		_ => throw new ArgumentException("Unknown semantic value.", nameof(value))
	};

	/// <summary>What a value of <paramref name="type"/> is, as a refusal tells it.</summary>
	public static string Describe(SemanticType type) => type switch
	{
		SemanticType.Integer => "a whole number",
		SemanticType.Decimal => "a number like 1.5",
		SemanticType.Date => "a date like 2026-10-23",
		SemanticType.Boolean => "true or false",
		SemanticType.PageRef => "a page",
		SemanticType.Text => "text",
		_ => "a keyword"
	};
}
