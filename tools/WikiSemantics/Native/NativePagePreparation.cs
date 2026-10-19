using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using SharpMUSH.Database.Lightning.Semantics;
using SharpMUSH.Library.Models.Wiki.Semantics;

namespace SharpMUSH.Tools.WikiSemantics.Native;

/// <summary>Pure admission checks. A successful preparation never authorizes a transactional delta.</summary>
public static class NativePagePreparation
{
	public static NativePreparationResult Prepare(SemanticSchema schema, PageSnapshot page,
		IEnumerable<SemanticFact> facts, long expectedStateVersion, Guid mutationId, bool deleted,
		CancellationToken ct = default)
	{
		ct.ThrowIfCancellationRequested();
		if (mutationId == Guid.Empty || expectedStateVersion < 0)
			return Fail(ValidationCode.InvalidSchema, "Mutation identity and nonnegative expected version are required.");
		if (schema is null || page is null || facts is null || page.Visibility is null || page.Stamp is null)
			return Fail(ValidationCode.InvalidSchema, "Schema, page metadata and facts are required.");
		if (string.IsNullOrWhiteSpace(page.Reference.Id) || string.IsNullOrWhiteSpace(page.Key.Package)
			|| string.IsNullOrWhiteSpace(page.Key.Namespace) || string.IsNullOrWhiteSpace(page.Key.SourceKey)
			|| string.IsNullOrWhiteSpace(page.Visibility.Namespace) || string.IsNullOrWhiteSpace(page.Title)
			|| page.Collections.IsDefault || page.Collections.Length != 1 || string.IsNullOrWhiteSpace(page.Collections[0]))
			return Fail(ValidationCode.InvalidSchema, "Complete identity and exactly one collection are required.");
		try
		{
			// Measure before normalization, copying, JSON canonicalization or encoding allocations.
			var pageBytes = NativeCodec.MeasurePage(page);
			NativeCodec.DescriptorSize(page.Visibility);
			var summaryBytes = NativeCodec.SummarySize(page);
			NativeCodec.MeasureSchema(schema);
			// The caller's enumerable is executable code, even in GetEnumerator. Own every
			// array before invoking it so schema admission and its certificate share one snapshot.
			schema = PreparedWikiProjection.FreezeSchema(schema, ct);
			page = page with
			{
				Collections = ImmutableArray.Create(page.Collections.AsSpan()),
				Visibility = PreparedWikiProjection.FreezeVisibility(page.Visibility, ct)
			};
			ct.ThrowIfCancellationRequested();
			var schemaBytes = NativeCodec.SchemaIdentity(schema);
			var raw = ImmutableArray.CreateBuilder<SemanticFact>();
			foreach (var fact in facts)
			{
				ct.ThrowIfCancellationRequested();
				if (raw.Count == NativeFixtureLimits.MaxFacts)
					return Fail(ValidationCode.Quota, "More than 128 raw fact entries in a prototype edit.");
				if (fact is null) return Fail(ValidationCode.InvalidSchema, "A fact is null.");
				if (fact.Subject != page.Reference) return Fail(ValidationCode.MissingTarget, "A fact names another subject.");
				if (deleted) return Fail(ValidationCode.InvalidSchema, "Deleted edits cannot carry facts.");
				// One-item measurement prevents a huge field reaching CanonicalFacts JSON/Normalize.
				NativeCodec.MeasureFacts(new(schema.Version, [fact]), NativeFixtureLimits.MaxFactBytes);
				raw.Add(fact);
			}
			var rawFacts = new CanonicalFactSet(schema.Version, raw.ToImmutable());
			NativeCodec.MeasureFacts(rawFacts, NativeFixtureLimits.MaxFactBytes);
			ct.ThrowIfCancellationRequested();
			var validation = PreparedWikiProjection.PrepareProjection(schema, NativeCodec.Projection(page), rawFacts, deleted, ct);
			if (validation is ValidationFailure failure) return failure;
			if (validation is not PreparedWikiProjection projection) throw new InvalidOperationException("Invalid projection result.");
			var canonical = projection.Facts;
			var frozen = page;
			var encodedPage = NativeCodec.EncodePage(frozen);
			var encodedFacts = NativeCodec.EncodeFacts(canonical);
			// Known immutable inputs only. Registry/catalog/counters/old deletes are unknown here.
			var knownBytes = checked((long)pageBytes + summaryBytes + encodedFacts.Length + 24
				+ canonical.Facts.Length * (25L + 41));
			if (knownBytes > NativeFixtureLimits.MaxDeltaBytes)
				return Fail(ValidationCode.Quota, "Known prepared input exceeds the entire-delta bound.");
			ct.ThrowIfCancellationRequested();
			using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
			digest.AppendData(schemaBytes);
			digest.AppendData(encodedPage);
			digest.AppendData(encodedFacts);
			Span<byte> state = stackalloc byte[9];
			BinaryPrimitives.WriteInt64LittleEndian(state, expectedStateVersion);
			state[8] = deleted ? (byte)1 : (byte)0;
			digest.AppendData(state);
			return new PreparedNativePage(frozen, canonical, expectedStateVersion, mutationId, deleted,
				Convert.ToHexStringLower(digest.GetHashAndReset()), Convert.ToHexStringLower(SHA256.HashData(schemaBytes)), projection);
		}
		catch (InvalidDataException e) { return Fail(ValidationCode.Quota, e.Message); }
		catch (ArgumentException) { return Fail(ValidationCode.InvalidSchema, "Malformed or missing encoded metadata."); }
		catch (OverflowException) { return Fail(ValidationCode.Quota, "Prepared sizes overflowed."); }
	}
	private static ValidationFailure Fail(ValidationCode code, string detail) => new(code, detail);
}
