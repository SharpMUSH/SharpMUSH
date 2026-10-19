using System.Collections.Immutable;
using System.Security.Cryptography;
using SharpMUSH.Library.Models.Wiki.Semantics;
using SharpMUSH.Library.Wiki.Semantics;

namespace SharpMUSH.Database.Lightning.Semantics;

internal union WikiProjectionPreparationResult(PreparedWikiProjection, ValidationFailure);

/// <summary>Owned, bodyless admission certificate. Only successful preparation can construct it.</summary>
internal sealed class PreparedWikiProjection
{
	private PreparedWikiProjection(WikiPageProjection page, CanonicalFactSet facts, bool deleted,
		string requestDigest, string schemaIdentityDigest)
		=> (Page, Facts, Deleted, RequestDigest, SchemaIdentityDigest) = (page, facts, deleted, requestDigest, schemaIdentityDigest);
	internal WikiPageProjection Page { get; }
	internal CanonicalFactSet Facts { get; }
	internal bool Deleted { get; }
	internal string RequestDigest { get; }
	internal string SchemaIdentityDigest { get; }

	internal static WikiProjectionPreparationResult PrepareProjection(SemanticSchema schema, WikiPageProjection page,
		CanonicalFactSet facts, bool deleted, CancellationToken ct = default)
	{
		ct.ThrowIfCancellationRequested();
		if (schema is null || page is null || page.Visibility is null || facts is null || facts.Facts.IsDefault
			|| string.IsNullOrWhiteSpace(page.Reference.Id) || string.IsNullOrWhiteSpace(page.Title)
			|| string.IsNullOrWhiteSpace(page.Collection) || string.IsNullOrWhiteSpace(page.Visibility.Namespace))
			return Fail(ValidationCode.InvalidSchema, "Complete projection metadata and facts are required.");
		if (page.Source is not LocalWikiSource && page.Source is not PortableWikiSource)
			return Fail(ValidationCode.InvalidSchema, "Unknown semantic source.");
		if (page.Source is PortableWikiSource portable && (string.IsNullOrWhiteSpace(portable.Key.Package)
			|| string.IsNullOrWhiteSpace(portable.Key.Namespace) || string.IsNullOrWhiteSpace(portable.Key.SourceKey)))
			return Fail(ValidationCode.InvalidSchema, "Complete portable source identity is required.");
		if (facts.SchemaVersion != schema.Version)
			return Fail(ValidationCode.InvalidSchema, "Fact schema version differs from admitted schema.");
		try
		{
			// Bound all caller fields before copies, normalization and JSON canonicalization.
			var projectionBytes = WikiSemanticCodec.MeasureProjection(page);
			var summaryBytes = WikiSemanticCodec.SummarySize(page);
			WikiSemanticCodec.DescriptorSize(page.Visibility);
			WikiSemanticCodec.MeasureSchema(schema);
			WikiSemanticCodec.MeasureFacts(facts, WikiSemanticLimits.MaxFactBytes);
			schema = FreezeSchema(schema, ct);
			page = page with { Visibility = FreezeVisibility(page.Visibility, ct) };
			// An ImmutableArray may alias a caller-owned array through ImmutableCollectionsMarshal.
			facts = facts with { Facts = ImmutableArray.Create(facts.Facts.AsSpan()) };
			foreach (var fact in facts.Facts)
			{
				ct.ThrowIfCancellationRequested();
				if (fact is null) return Fail(ValidationCode.InvalidSchema, "A fact is null.");
				if (fact.Subject != page.Reference) return Fail(ValidationCode.MissingTarget, "A fact names another subject.");
				if (deleted) return Fail(ValidationCode.InvalidSchema, "Deleted edits cannot carry facts.");
			}
			var validation = CanonicalFacts.Validate(schema, facts.Facts, ct);
			if (validation is ValidationFailure failure) return failure;
			if (validation is not CanonicalFactSet canonical) throw new InvalidOperationException("Unknown validation result.");
			var schemaBytes = WikiSemanticCodec.SchemaIdentity(schema);
			var encodedPage = WikiSemanticCodec.EncodeProjection(page);
			var encodedFacts = WikiSemanticCodec.EncodeFacts(canonical);
			var knownBytes = checked((long)projectionBytes + summaryBytes + encodedFacts.Length + 24
				+ canonical.Facts.Length * (25L + 41));
			if (knownBytes > WikiSemanticLimits.MaxDeltaBytes)
				return Fail(ValidationCode.Quota, "Known projection exceeds the entire-delta bound.");
			ct.ThrowIfCancellationRequested();
			using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
			digest.AppendData(schemaBytes); digest.AppendData(encodedPage); digest.AppendData(encodedFacts);
			Span<byte> state = stackalloc byte[1];
			state[0] = deleted ? (byte)1 : (byte)0;
			digest.AppendData(state);
			return new PreparedWikiProjection(page, canonical, deleted, Convert.ToHexStringLower(digest.GetHashAndReset()),
				Convert.ToHexStringLower(SHA256.HashData(schemaBytes)));
		}
		catch (InvalidDataException e) { return Fail(ValidationCode.Quota, e.Message); }
		catch (ArgumentException) { return Fail(ValidationCode.InvalidSchema, "Malformed or missing encoded metadata."); }
		catch (OverflowException) { return Fail(ValidationCode.Quota, "Prepared sizes overflowed."); }
	}

	internal static SemanticSchema FreezeSchema(SemanticSchema schema, CancellationToken ct)
	{
		var properties = ImmutableArray.CreateBuilder<PropertyDefinition>(schema.Properties.Length);
		foreach (var property in schema.Properties)
		{
			ct.ThrowIfCancellationRequested();
			properties.Add(property with { AllowedKeywords = ImmutableArray.Create(property.AllowedKeywords.AsSpan()) });
		}
		return schema with { Properties = properties.MoveToImmutable() };
	}
	internal static VisibilityDescriptor FreezeVisibility(VisibilityDescriptor visibility, CancellationToken ct)
		=> visibility with { Categories = CanonicalSet(visibility.Categories, ct), RuleIds = CanonicalSet(visibility.RuleIds, ct) };
	private static ImmutableArray<string> CanonicalSet(ImmutableArray<string> values, CancellationToken ct)
	{
		var result = new SortedSet<string>(StringComparer.Ordinal);
		foreach (var value in values)
		{
			ct.ThrowIfCancellationRequested();
			if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Structural identity is empty.");
			result.Add(value);
		}
		return [.. result];
	}
	private static ValidationFailure Fail(ValidationCode code, string detail) => new(code, detail);
}
