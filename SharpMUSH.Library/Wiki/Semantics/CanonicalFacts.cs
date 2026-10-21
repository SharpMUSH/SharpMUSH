using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using System.Text;
using SharpMUSH.Library.Models.Wiki.Semantics;

namespace SharpMUSH.Library.Wiki.Semantics;

/// <summary>Pure offline validation. It makes no authorization decision about source or target pages.</summary>
public static class CanonicalFacts
{
	public static ValidationResult Validate(SemanticSchema schema, IEnumerable<SemanticFact> facts,
		CancellationToken cancellationToken = default)
	{
		if (schema.Version <= 0 || schema.Properties.IsDefault)
			return Fail(ValidationCode.InvalidSchema, "Schema version and definitions are required.");
		var definitions = new Dictionary<string, PropertyDefinition>(StringComparer.Ordinal);
		foreach (var definition in schema.Properties)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (definition is null || !QualifiedProperty(definition.Id) || definition.SchemaVersion != schema.Version
				|| !Enum.IsDefined(definition.Type) || !Enum.IsDefined(definition.Cardinality)
				|| definition.AllowedKeywords.IsDefault || definitions.ContainsKey(definition.Id))
				return Fail(ValidationCode.InvalidSchema, "Invalid, duplicate or differently versioned property definition.");
			var keywords = ImmutableArray.CreateBuilder<Keyword>();
			foreach (var keyword in definition.AllowedKeywords)
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (!TryKeyword(keyword, out var normalized) || keywords.Contains(normalized))
					return Fail(ValidationCode.InvalidSchema, "Invalid or canonically duplicate enum member.");
				keywords.Add(normalized);
			}
			if (keywords.Count > 0 && definition.Type != SemanticType.Keyword)
				return Fail(ValidationCode.InvalidSchema, "Only keywords have enums.");
			definitions.Add(definition.Id, definition with { AllowedKeywords = keywords.ToImmutable() });
		}
		var canonical = new SortedDictionary<string, SemanticFact>(StringComparer.Ordinal);
		foreach (var fact in facts)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (fact is null)
				return Fail(ValidationCode.InvalidSchema, "A fact entry is null.");
			if (string.IsNullOrWhiteSpace(fact.Subject.Id))
				return Fail(ValidationCode.MissingTarget, "Subject incarnation is required.");
			if (!QualifiedProperty(fact.Property) || !definitions.TryGetValue(fact.Property, out var definition))
				return Fail(ValidationCode.UnknownProperty, "Unknown or malformed property key.");
			var value = fact.Value;
			if (!Matches(value, definition.Type))
				return Fail(ValidationCode.WrongType, fact.Property);
			if (value is Keyword keyword)
			{
				if (!TryKeyword(keyword, out var normalized))
					return Fail(ValidationCode.InvalidKeyword, fact.Property);
				if (!definition.AllowedKeywords.IsEmpty && !definition.AllowedKeywords.Contains(normalized))
					return Fail(ValidationCode.InvalidEnum, fact.Property);
				value = normalized;
			}
			if (value is PageRef reference && string.IsNullOrWhiteSpace(reference.Id))
				return Fail(ValidationCode.MissingTarget, fact.Property);
			var normalizedFact = fact with { Value = value };
			var key = JsonSerializer.Serialize(new[] { fact.Subject.Id, fact.Property, ValueKey(value) });
			// Duplicate facts with different evidence choose the ordinal-first complete evidence, independent of input order.
			if (!canonical.TryGetValue(key, out var previous)
				|| string.CompareOrdinal(JsonSerializer.Serialize(normalizedFact.Provenance), JsonSerializer.Serialize(previous.Provenance)) < 0)
				canonical[key] = normalizedFact;
		}
		foreach (var subject in canonical.Values.GroupBy(f => f.Subject))
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (subject.Count() > WikiSemanticPolicy.MaxFacts)
				return Fail(ValidationCode.Quota, "More than 128 facts per subject.");
			var bytes = 0L;
			foreach (var property in subject.GroupBy(f => f.Property))
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (property.Count() > WikiSemanticPolicy.MaxValuesPerProperty)
					return Fail(ValidationCode.Quota, "More than 32 values per property.");
				if (definitions[property.Key].Cardinality == Cardinality.Single && property.Count() > 1)
					return Fail(ValidationCode.Cardinality, property.Key);
				foreach (var fact in property)
				{
					cancellationToken.ThrowIfCancellationRequested();
					bytes += Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(new { fact.Subject, fact.Property, Value = ValueKey(fact.Value), fact.Provenance }));
					if (bytes > WikiSemanticPolicy.MaxFactBytes)
						return Fail(ValidationCode.Quota, "More than 64 KiB canonical data per subject.");
				}
			}
		}
		return new CanonicalFactSet(schema.Version, [.. canonical.Values]);
	}

	public static IdentityResult Resolve(PortableKey key, IEnumerable<PortableAlias> aliases,
		IReadOnlySet<PageRef> existing, CancellationToken cancellationToken = default)
	{
		var matches = new HashSet<PageRef>();
		foreach (var alias in aliases)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (alias.Key == key)
				matches.Add(alias.Target);
		}
		if (matches.Count > 1)
			return new ValidationFailure(ValidationCode.AmbiguousAlias, "Portable alias has multiple incarnations.");
		if (matches.Count == 1 && existing.Contains(matches.Single()))
			return matches.Single();
		return new ValidationFailure(ValidationCode.MissingTarget, "Portable target is unavailable.");
	}

	/// <summary>Stable typed scalar encoding used only for canonical ordering, never authorization.</summary>
	public static string ValueKey(SemanticValue value) => value switch
	{
		PageRef reference => "p:" + reference.Id,
		long integer => "i:" + integer.ToString(CultureInfo.InvariantCulture),
		bool boolean => boolean ? "b:1" : "b:0",
		Keyword keyword => "k:" + keyword.Value,
		_ => "invalid"
	};

	private static bool QualifiedProperty(string? value)
	{
		if (value is null)
			return false;
		var separator = value.IndexOf(':');
		if (separator <= 0 || separator == value.Length - 1 || value.LastIndexOf(':') != separator)
			return false;
		for (var i = 0; i < value.Length; i++)
		{
			if (i == separator)
				continue;
			var character = value[i];
			if (!char.IsAsciiLetterOrDigit(character) && (i == 0 || i == separator + 1 || character is not ('.' or '_' or '-')))
				return false;
		}
		return true;
	}

	private static bool Matches(SemanticValue value, SemanticType type) => type switch
	{
		SemanticType.PageRef => value is PageRef,
		SemanticType.Integer => value is long,
		SemanticType.Boolean => value is bool,
		SemanticType.Keyword => value is Keyword,
		_ => false
	};

	internal static bool TryKeyword(Keyword keyword, out Keyword normalized)
	{
		normalized = default;
		if (keyword.Value is null)
			return false;
		try
		{
			var value = keyword.Value.Normalize(NormalizationForm.FormKC).Trim().ToLowerInvariant();
			if (value.Length == 0 || Encoding.UTF8.GetByteCount(value) > WikiSemanticPolicy.MaxKeywordBytes || value.Any(char.IsControl))
				return false;
			normalized = new(value);
			return true;
		}
		catch (ArgumentException)
		{
			return false;
		}
	}

	private static ValidationFailure Fail(ValidationCode code, string detail) => new(code, detail);
}
