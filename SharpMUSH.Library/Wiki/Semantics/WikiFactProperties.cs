using System.Collections.Immutable;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Wiki.Semantics;

namespace SharpMUSH.Library.Wiki.Semantics;

/// <summary>
/// The properties a world defines for its pages' facts, as a store keeps them: each checked and normalized before it
/// is written, and read back as one <see cref="SemanticSchema"/>.
/// </summary>
public static class WikiFactProperties
{
	/// <summary>The version every schema a store reads carries; the properties themselves are the world's data.</summary>
	public const long SchemaVersion = 1;

	public const int MaxIdLength = 128;
	public const int MaxLabelLength = 100;
	public const int MaxDescriptionLength = 1000;
	public const int MaxKeywords = 256;

	/// <summary>The schema <paramref name="properties"/> make, in id order.</summary>
	public static SemanticSchema Schema(IEnumerable<PropertyDefinition> properties)
		=> new(SchemaVersion, [.. properties.Select(p => p with { SchemaVersion = SchemaVersion }).OrderBy(p => p.Id, StringComparer.Ordinal)]);

	/// <summary>
	/// <paramref name="property"/> as a store writes it: label and description trimmed, keywords normalized as fact
	/// values are. An id that is not a property key, a keyword list on a type that takes none, a repeated keyword or
	/// an over-long text is an error naming it.
	/// </summary>
	public static Result<PropertyDefinition> Checked(PropertyDefinition property)
	{
		if (property.Id is not { Length: <= MaxIdLength } || !CanonicalFacts.QualifiedProperty(property.Id))
			return new Error<string>($"'{property.Id}' is not a property key like 'game:level'.");
		if (!Enum.IsDefined(property.Type) || !Enum.IsDefined(property.Cardinality))
			return new Error<string>($"{property.Id} has no type.");
		var label = property.Label?.Trim() ?? string.Empty;
		var description = property.Description?.Trim() ?? string.Empty;
		if (label.Length > MaxLabelLength || label.Any(char.IsControl))
			return new Error<string>($"{property.Id}'s label is longer than {MaxLabelLength} characters or has a control character.");
		if (description.Length > MaxDescriptionLength || description.Any(char.IsControl))
			return new Error<string>($"{property.Id}'s description is longer than {MaxDescriptionLength} characters or has a control character.");

		var keywords = property.AllowedKeywords.IsDefault ? [] : property.AllowedKeywords;
		if (keywords.Length > 0 && property.Type != SemanticType.Keyword)
			return new Error<string>($"{property.Id} is {TypeName(property.Type)}; only a keyword property lists its values.");
		if (keywords.Length > MaxKeywords)
			return new Error<string>($"{property.Id} lists more than {MaxKeywords} values.");
		var normalized = ImmutableArray.CreateBuilder<Keyword>(keywords.Length);
		foreach (var keyword in keywords)
		{
			if (!CanonicalFacts.TryKeyword(keyword, out var value))
				return new Error<string>($"{property.Id} lists a value that is not a keyword: '{keyword.Value}'.");
			if (normalized.Contains(value))
				return new Error<string>($"{property.Id} lists '{value.Value}' twice.");
			normalized.Add(value);
		}

		return property with
		{
			SchemaVersion = SchemaVersion,
			AllowedKeywords = normalized.ToImmutable(),
			Label = label,
			Description = description
		};
	}

	/// <summary>How a type is written in commands, the API and the portal.</summary>
	public static string TypeName(SemanticType type) => type switch
	{
		SemanticType.Integer => "number",
		SemanticType.Boolean => "boolean",
		SemanticType.PageRef => "page",
		_ => "keyword",
	};

	/// <summary>The type <paramref name="name"/> writes (<see cref="TypeName"/>), or null for none.</summary>
	public static SemanticType? ParseType(string? name) => name?.Trim().ToLowerInvariant() switch
	{
		"number" => SemanticType.Integer,
		"boolean" => SemanticType.Boolean,
		"page" => SemanticType.PageRef,
		"keyword" => SemanticType.Keyword,
		_ => null,
	};
}
