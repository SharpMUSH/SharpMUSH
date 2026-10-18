using System.Collections.Immutable;
using System.Text;
using SharpMUSH.Tools.WikiSemantics;
using SharpMUSH.Tools.WikiSemantics.Native;
using SharpMUSH.Library.Models.Wiki.Semantics;

namespace SharpMUSH.Tests.Wiki.Semantics;

public class NativeSemanticPreparationTests
{
	private static readonly Guid Mutation = Guid.Parse("11111111-2222-3333-4444-555555555555");
	private static SemanticSchema Schema => new(3, [new("test:i", 3, SemanticType.Integer, Cardinality.Multiple, []),
		new("test:b", 3, SemanticType.Boolean, Cardinality.Single, []), new("test:p", 3, SemanticType.PageRef, Cardinality.Multiple, []),
		new("test:k", 3, SemanticType.Keyword, Cardinality.Multiple, [])]);
	private static PageSnapshot Page => new(new("incarnation"), new("pack", "Main", "source"), "Títle", "full\nbody\0界", ["collection"],
		new("Main", ["z", "a", "a"], ["r2", "r1"], false, " ExactAuthor "), new("original", 2, 3, 4, 5, 6, 7, 8));
	private static FactProvenance Evidence => new("pack", "commit", "path", "field", true, "v1", "blob", "rule", "location界");
	private static SemanticFact Fact(string property, SemanticValue value) => new(Page.Reference, property, value, Evidence);
	private static NativePreparationResult Prepare(PageSnapshot? page = null, IEnumerable<SemanticFact>? facts = null,
		long expected = 0, Guid? mutation = null, bool deleted = false, SemanticSchema? schema = null, CancellationToken ct = default)
		=> NativePagePreparation.Prepare(schema ?? Schema, page ?? Page, facts ?? [], expected, mutation ?? Mutation, deleted, ct);
	private static PreparedNativePage Accepted(NativePreparationResult result) => result is PreparedNativePage page ? page : throw new InvalidOperationException("Expected prepared edit: " + result);
	[Test]
	public async Task PageRoundtripPreservesEveryFieldAndOriginalStamp()
	{
		var actual = NativeCodec.DecodePage(NativeCodec.EncodePage(Page));
		await Assert.That(actual.Reference).IsEqualTo(Page.Reference);
		await Assert.That(actual.Key).IsEqualTo(Page.Key);
		await Assert.That(actual.Title).IsEqualTo(Page.Title);
		await Assert.That(actual.Body).IsEqualTo(Page.Body);
		await Assert.That(actual.Collections.SequenceEqual(Page.Collections)).IsTrue();
		await Assert.That(actual.Visibility.Namespace).IsEqualTo("Main");
		await Assert.That(actual.Visibility.Categories.SequenceEqual(new[] { "z", "a", "a" })).IsTrue();
		await Assert.That(actual.Visibility.RuleIds.SequenceEqual(new[] { "r2", "r1" })).IsTrue();
		await Assert.That(actual.Visibility.Published).IsFalse();
		await Assert.That(actual.Visibility.DraftAuthor).IsEqualTo(" ExactAuthor ");
		await Assert.That(actual.Stamp).IsEqualTo(new ExperimentalStamp("original", 2, 3, 4, 5, 6, 7, 8));
		var nullable = NativeCodec.DecodePage(NativeCodec.EncodePage(Page with { Visibility = Page.Visibility with { DraftAuthor = null } }));
		await Assert.That(nullable.Visibility.DraftAuthor is null).IsTrue();
	}
	[Test]
	public async Task FactRoundtripPreservesTypesSignedBoundsAndAllEvidence()
	{
		ImmutableArray<SemanticFact> facts = [Fact("test:i", long.MinValue), Fact("test:i", long.MaxValue), Fact("test:i", 0L),
			Fact("test:b", false), Fact("test:p", new PageRef("target1")), Fact("test:p", new PageRef("target2")), Fact("test:k", new Keyword("keyword"))];
		var actual = NativeCodec.DecodeFacts(NativeCodec.EncodeFacts(new(3, facts)));
		await Assert.That(actual.SchemaVersion).IsEqualTo(3L);
		await Assert.That(actual.Facts.SequenceEqual(facts)).IsTrue();
		await Assert.That(actual.Facts.All(f => f.Provenance == Evidence)).IsTrue();
		await Assert.That(NativeCodec.DecodeFacts(NativeCodec.EncodeFacts(new(3, []))).Facts.IsEmpty).IsTrue();
	}
	[Test]
	public async Task MalformedCodecVersionTagLengthAndTrailingBytesReject()
	{
		var page = NativeCodec.EncodePage(Page);
		page[0] = 99;
		await Assert.That(() => NativeCodec.DecodePage(page)).Throws<InvalidDataException>();
		await Assert.That(() => NativeCodec.DecodePage(new byte[] { 1, 1, 255, 255, 255, 127 })).Throws<InvalidDataException>();
		await Assert.That(() => NativeCodec.DecodeFacts(new byte[] { 1, 99 })).Throws<InvalidDataException>();
		var good = NativeCodec.EncodePage(Page);
		await Assert.That(() => NativeCodec.DecodePage(good.AsSpan(0, good.Length - 1))).Throws<InvalidDataException>();
		await Assert.That(() => NativeCodec.DecodePage(good.Concat(new byte[] { 0 }).ToArray())).Throws<InvalidDataException>();
	}
	[Test]
	public async Task PreparationCanonicalizesSetsAndFreezesInputArrays()
	{
		var categories = new[] { "z", "a", "a" };
		var p = Page with { Visibility = Page.Visibility with { Categories = System.Runtime.InteropServices.ImmutableCollectionsMarshal.AsImmutableArray(categories) } };
		var prepared = Accepted(Prepare(p, [Fact("test:k", new Keyword(" CAFÉ "))]));
		categories[0] = "mutated";
		await Assert.That(prepared.Page.Visibility.Categories.SequenceEqual(new[] { "a", "z" })).IsTrue();
		await Assert.That(prepared.Page.Visibility.DraftAuthor).IsEqualTo(" ExactAuthor ");
		await Assert.That(prepared.Facts.Facts[0].Value is Keyword k && k.Value == "café").IsTrue();
	}
	[Test]
	public async Task PreparationRejectsIdentityStateDeletionAndStructuralErrors()
	{
		await Assert.That(Prepare(expected: -1) is ValidationFailure).IsTrue();
		await Assert.That(Prepare(mutation: Guid.Empty) is ValidationFailure).IsTrue();
		await Assert.That(Prepare(Page with { Collections = [] }) is ValidationFailure).IsTrue();
		await Assert.That(Prepare(Page with { Collections = ["a", "b"] }) is ValidationFailure).IsTrue();
		await Assert.That(Prepare(facts: [Fact("test:i", 1L) with { Subject = new("other") }]) is ValidationFailure).IsTrue();
		await Assert.That(Prepare(facts: [Fact("test:i", 1L)], deleted: true) is ValidationFailure).IsTrue();
		await Assert.That(Prepare(Page with { Visibility = Page.Visibility with { RuleIds = default } }) is ValidationFailure).IsTrue();
		await Assert.That(Prepare(facts: [Fact("test:b", true), Fact("test:b", false)]) is ValidationFailure failure && failure.Code == ValidationCode.Cardinality).IsTrue();
	}
	[Test]
	public async Task DuplicateStreamStopsOn129thRawInput()
	{
		var visited = 0;
		IEnumerable<SemanticFact> Flood()
		{
			while (true)
			{
				visited++;
				if (visited > 129) throw new InvalidOperationException("Unbounded enumeration");
				yield return Fact("test:i", 1L);
			}
		}
		await Assert.That(Prepare(facts: Flood()) is ValidationFailure f && f.Code == ValidationCode.Quota).IsTrue();
		await Assert.That(visited).IsEqualTo(129);
	}
	[Test]
	public async Task HugeFieldsRejectBeforeProportionalSerializationCopies()
	{
		var huge = new string('x', 4 * 1024 * 1024);
		_ = Prepare();
		var page = Page with { Body = huge };
		var fact = Fact("test:i", 1L) with { Provenance = Evidence with { SourcePath = huge } };
		var before = GC.GetAllocatedBytesForCurrentThread();
		var pageResult = Prepare(page);
		var factResult = Prepare(facts: [fact]);
		var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
		await Assert.That(pageResult is ValidationFailure && factResult is ValidationFailure).IsTrue();
		await Assert.That(bytes < 64 * 1024).IsTrue();
	}
	[Test]
	public async Task QuotasSchemaAndCancellationAreEnforced()
	{
		await Assert.That(Prepare(facts: Enumerable.Range(0, 33).Select(i => Fact("test:i", (long)i))) is ValidationFailure).IsTrue();
		await Assert.That(Prepare(facts: [Fact("test:k", new Keyword(new string('é', 129)))]) is ValidationFailure).IsTrue();
		await Assert.That(Prepare(schema: Schema with { Version = 0 }) is ValidationFailure).IsTrue();
		await Assert.That(Prepare(Page with { Visibility = Page.Visibility with { Categories = [new string('a', 9000)] } }) is ValidationFailure).IsTrue();
		await Assert.That(Prepare(Page with { Title = new string('a', 17000) }) is ValidationFailure).IsTrue();
		using var canceled = new CancellationTokenSource();
		canceled.Cancel();
		await Assert.That(() => Prepare(ct: canceled.Token)).Throws<OperationCanceledException>();
	}
	[Test]
	public async Task DigestCoversEveryEffectAndCanonicalSetPermutations()
	{
		var original = Accepted(Prepare(facts: [Fact("test:i", 1L)]));
		var permutations = Accepted(Prepare(Page with { Visibility = Page.Visibility with { Categories = ["a", "z"], RuleIds = ["r1", "r2"] } }, [Fact("test:i", 1L)]));
		await Assert.That(original.RequestDigest).IsEqualTo(permutations.RequestDigest);
		foreach (var changed in new[] { Accepted(Prepare(Page with { Body = "changed" }, [Fact("test:i", 1L)])),
			Accepted(Prepare(Page with { Stamp = Page.Stamp with { TranslationVersion = 99 } }, [Fact("test:i", 1L)])),
			Accepted(Prepare(facts: [Fact("test:i", 1L) with { Provenance = Evidence with { EvidenceLocation = "changed" } }])),
			Accepted(Prepare(facts: [Fact("test:i", 1L)], expected: 1)),
			Accepted(Prepare(schema: Schema with { Properties = Schema.Properties.Add(new("test:new", 3, SemanticType.Boolean, Cardinality.Single, [])) }, facts: [Fact("test:i", 1L)])) })
			await Assert.That(changed.RequestDigest != original.RequestDigest).IsTrue();
		await Assert.That(Accepted(Prepare()).RequestDigest != Accepted(Prepare(deleted: true)).RequestDigest).IsTrue();
	}
	[Test]
	public async Task MalformedScalarBooleanUtf8AndContainerCountsReject()
	{
		var encoded = NativeCodec.EncodeFacts(new(3, [Fact("test:b", false)]));
		// Header 14; subject length+11; property length+6 => scalar tag offset 39.
		encoded[39] = 99;
		await Assert.That(() => NativeCodec.DecodeFacts(encoded)).Throws<InvalidDataException>();
		encoded[39] = 3;
		encoded[40] = 2;
		await Assert.That(() => NativeCodec.DecodeFacts(encoded)).Throws<InvalidDataException>();
		var page = NativeCodec.EncodePage(Page);
		page[6] = 255;
		await Assert.That(() => NativeCodec.DecodePage(page)).Throws<InvalidDataException>();
		var facts = NativeCodec.EncodeFacts(new(3, []));
		System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(facts.AsSpan(10), int.MaxValue);
		await Assert.That(() => NativeCodec.DecodeFacts(facts)).Throws<InvalidDataException>();
	}
	[Test]
	public async Task SchemaHugeFieldRefusedBeforeCanonicalAllocation()
	{
		var huge = new string('x', 4 * 1024 * 1024);
		var schema = new SemanticSchema(3, [new("test:k", 3, SemanticType.Keyword, Cardinality.Multiple, [new(huge)])]);
		_ = Prepare();
		var before = GC.GetAllocatedBytesForCurrentThread();
		var result = Prepare(schema: schema);
		var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
		await Assert.That(result is ValidationFailure).IsTrue();
		await Assert.That(allocated < 64 * 1024).IsTrue();
	}
	[Test]
	public async Task InclusiveFactAndReadbackBoundsAreAccepted()
	{
		var schema = new SemanticSchema(3, [.. Enumerable.Range(0, 4).Select(i => new PropertyDefinition($"test:i{i}", 3, SemanticType.Integer, Cardinality.Multiple, []))]);
		var facts = Enumerable.Range(0, 128).Select(i => Fact($"test:i{i / 32}", (long)(i % 32)));
		await Assert.That(Accepted(Prepare(schema: schema, facts: facts)).Facts.Facts.Length).IsEqualTo(128);
		await Assert.That(Accepted(Prepare(facts: Enumerable.Repeat(Fact("test:i", 1L), 128))).Facts.Facts.Length).IsEqualTo(1);
		var emptyBody = Page with { Body = "" };
		var overhead = NativeCodec.EncodePage(emptyBody).Length;
		var boundary = emptyBody with { Body = new string('x', 128 * 1024 - 8 - overhead) };
		await Assert.That(NativeCodec.EncodePage(boundary).Length + 8).IsEqualTo(128 * 1024);
		await Assert.That(Prepare(boundary) is PreparedNativePage).IsTrue();
		await Assert.That(Prepare(boundary with { Body = boundary.Body + "x" }) is ValidationFailure).IsTrue();
	}
	[Test]
	public async Task DetachedCollectionsAndRulesCannotChangeDigestOrPreparedPage()
	{
		var collections = new[] { "collection" };
		var rules = new[] { "rule" };
		var input = Page with
		{
			Collections = System.Runtime.InteropServices.ImmutableCollectionsMarshal.AsImmutableArray(collections),
			Visibility = Page.Visibility with { RuleIds = System.Runtime.InteropServices.ImmutableCollectionsMarshal.AsImmutableArray(rules) }
		};
		var prepared = Accepted(Prepare(input));
		var digest = prepared.RequestDigest;
		collections[0] = "other";
		rules[0] = "other";
		await Assert.That(prepared.Page.Collections[0]).IsEqualTo("collection");
		await Assert.That(prepared.Page.Visibility.RuleIds[0]).IsEqualTo("rule");
		await Assert.That(prepared.RequestDigest).IsEqualTo(digest);
		await Assert.That(Accepted(Prepare(Page with { Visibility = Page.Visibility with { DraftAuthor = null } })).RequestDigest
			!= Accepted(Prepare(Page with { Visibility = Page.Visibility with { DraftAuthor = "" } })).RequestDigest).IsTrue();
	}
	[Test]
	public async Task CancellationDuringRawEnumerationStopsBeforeAnotherItem()
	{
		using var cancel = new CancellationTokenSource();
		var visited = 0;
		IEnumerable<SemanticFact> Input()
		{
			visited++;
			yield return Fact("test:i", 1L);
			visited++;
			cancel.Cancel();
			yield return Fact("test:i", 2L);
			throw new InvalidOperationException("Enumerated after cancellation.");
		}
		await Assert.That(() => Prepare(facts: Input(), ct: cancel.Token)).Throws<OperationCanceledException>();
		await Assert.That(visited).IsEqualTo(2);
	}

	[Test]
	public async Task PreparedSchemaFingerprintDistinguishesSameVersionDefinitions()
	{
		// The certificate is internal to the tool assembly; its store consumer is Task4.
		static string Fingerprint(PreparedNativePage page) => typeof(PreparedNativePage)
			.GetProperty("SchemaIdentityDigest", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
			?.GetValue(page) as string ?? "";
		var first = Fingerprint(Accepted(Prepare()));
		var equivalent = Fingerprint(Accepted(Prepare(schema: new(3, [.. Schema.Properties]))));
		var changed = Fingerprint(Accepted(Prepare(schema: Schema with
		{
			Properties = Schema.Properties.SetItem(0,
			Schema.Properties[0] with { Cardinality = Cardinality.Single })
		})));
		await Assert.That(first.Length).IsEqualTo(64);
		await Assert.That(first).IsEqualTo(equivalent);
		await Assert.That(first != changed).IsTrue();
	}

	[Test]
	public async Task IteratorCannotChangeSchemaBehindItsCertificate()
	{
		var definitions = new[] { new PropertyDefinition("test:i", 3, SemanticType.Integer, Cardinality.Single, []) };
		var schema = new SemanticSchema(3, System.Runtime.InteropServices.ImmutableCollectionsMarshal.AsImmutableArray(definitions));
		IEnumerable<SemanticFact> Input()
		{
			definitions[0] = definitions[0] with { Type = SemanticType.Boolean };
			yield return Fact("test:i", true);
		}
		await Assert.That(Prepare(schema: schema, facts: Input()) is ValidationFailure failure && failure.Code == ValidationCode.WrongType).IsTrue();
		definitions[0] = definitions[0] with { Type = SemanticType.Integer };
		var baseline = Accepted(Prepare(schema: schema, facts: [Fact("test:i", 1L)]));
		IEnumerable<SemanticFact> ValidInput()
		{
			definitions[0] = definitions[0] with { Type = SemanticType.Boolean };
			yield return Fact("test:i", 1L);
		}
		// Admission and the complete request certificate must both use the original Integer schema.
		var consistent = Accepted(Prepare(schema: schema, facts: ValidInput()));
		await Assert.That(consistent.RequestDigest).IsEqualTo(baseline.RequestDigest);
	}
	[Test]
	public async Task IteratorCannotReplaceNestedSchemaEnumMembers()
	{
		var allowed = new[] { new Keyword("allowed") };
		var schema = new SemanticSchema(3, [new("test:k", 3, SemanticType.Keyword, Cardinality.Single,
			System.Runtime.InteropServices.ImmutableCollectionsMarshal.AsImmutableArray(allowed))]);
		IEnumerable<SemanticFact> Input()
		{
			allowed[0] = new("other");
			yield return Fact("test:k", new Keyword("other"));
		}
		await Assert.That(Prepare(schema: schema, facts: Input()) is ValidationFailure failure && failure.Code == ValidationCode.InvalidEnum).IsTrue();
	}
	[Test]
	public async Task IteratorCannotReplaceAlreadyMeasuredPageArrays()
	{
		var collections = new[] { "collection" };
		var categories = new[] { "small" };
		var rules = new[] { "rule" };
		var page = Page with
		{
			Collections = System.Runtime.InteropServices.ImmutableCollectionsMarshal.AsImmutableArray(collections),
			Visibility = Page.Visibility with
			{
				Categories = System.Runtime.InteropServices.ImmutableCollectionsMarshal.AsImmutableArray(categories),
				RuleIds = System.Runtime.InteropServices.ImmutableCollectionsMarshal.AsImmutableArray(rules)
			}
		};
		var baseline = Accepted(Prepare(page));
		IEnumerable<SemanticFact> Input()
		{
			collections[0] = "";
			categories[0] = new string('x', 9000);
			rules[0] = "changed";
			yield break;
		}
		var result = Accepted(Prepare(page, Input()));
		await Assert.That(result.Page.Collections.SequenceEqual(new[] { "collection" })).IsTrue();
		await Assert.That(result.Page.Visibility.Categories.SequenceEqual(new[] { "small" })).IsTrue();
		await Assert.That(result.Page.Visibility.RuleIds.SequenceEqual(new[] { "rule" })).IsTrue();
		await Assert.That(result.RequestDigest).IsEqualTo(baseline.RequestDigest);
	}

	[Test]
	[Explicit]
	public async Task VerifiedFullCorpusFitsPurePreparationAndLosslessCodecs()
	{
		var source = Environment.GetEnvironmentVariable("SEMANTIC_PF2E_SOURCE")
			?? throw new InvalidOperationException("Set SEMANTIC_PF2E_SOURCE; missing source fails this required offline measurement.");
		var dataset = PinnedDataset.Load(source).Expect<VerifiedDataset>();
		var snapshot = Pf2eExtractor.Extract(dataset).Expect<ExtractionSuccess>().Snapshot;
		await Assert.That(snapshot.Pages.Length).IsEqualTo(4711);
		var bySubject = snapshot.Facts.Facts.ToLookup(f => f.Subject);
		var maxPage = 0;
		var maxFacts = 0;
		var maxDescriptor = 0;
		var maxSummary = 0;
		var maxKnownDelta = 0;
		var totalFacts = 0;
		foreach (var original in snapshot.Pages)
		{
			var facts = bySubject[original.Reference].ToImmutableArray();
			var pageBytes = NativeCodec.EncodePage(original);
			var factBytes = NativeCodec.EncodeFacts(new(snapshot.Schema.Version, facts));
			await SamePage(original, NativeCodec.DecodePage(pageBytes));
			var decodedFacts = NativeCodec.DecodeFacts(factBytes);
			await Assert.That(decodedFacts.SchemaVersion).IsEqualTo(snapshot.Schema.Version);
			await Assert.That(decodedFacts.Facts.SequenceEqual(facts)).IsTrue();
			var prepared = Accepted(NativePagePreparation.Prepare(snapshot.Schema, original, facts, 0, Mutation, false));
			await SamePage(original with
			{
				Visibility = original.Visibility with
				{
					Categories = [.. original.Visibility.Categories.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)],
					RuleIds = [.. original.Visibility.RuleIds.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)]
				}
			}, prepared.Page);
			await Assert.That(prepared.Facts.Facts.SequenceEqual(facts)).IsTrue();
			var descriptor = DescriptorBytes(original.Visibility);
			// This is Task3's documented compact-summary estimate, not Task4 serialized storage.
			var summary = 2 + TextBytes(original.Reference.Id) + 8 + TextBytes(original.Title) + descriptor
				+ TextBytes(original.Collections[0]) + 1 + 8;
			var known = pageBytes.Length + factBytes.Length + summary + 24 + facts.Length * 66;
			maxPage = Math.Max(maxPage, pageBytes.Length);
			maxFacts = Math.Max(maxFacts, factBytes.Length);
			maxDescriptor = Math.Max(maxDescriptor, descriptor);
			maxSummary = Math.Max(maxSummary, summary);
			maxKnownDelta = Math.Max(maxKnownDelta, known);
			totalFacts += facts.Length;
		}
		Console.WriteLine($"PURE_CODEC pages={snapshot.Pages.Length} facts={totalFacts} maxPageBytes={maxPage} maxFactBytes={maxFacts} maxDescriptorBytes={maxDescriptor} maxSummaryEstimateBytes={maxSummary} maxKnownPreparedDeltaBytes={maxKnownDelta}");
		await Assert.That(totalFacts).IsEqualTo(snapshot.Facts.Facts.Length);
	}
	private static int TextBytes(string value) => checked(4 + Encoding.UTF8.GetByteCount(value));
	private static int DescriptorBytes(VisibilityDescriptor value) => checked(TextBytes(value.Namespace) + 4
		+ value.Categories.Sum(TextBytes) + 4 + value.RuleIds.Sum(TextBytes) + 2
		+ (value.DraftAuthor is null ? 0 : TextBytes(value.DraftAuthor)));
	private static async Task SamePage(PageSnapshot expected, PageSnapshot actual)
	{
		await Assert.That(actual.Reference == expected.Reference && actual.Key == expected.Key && actual.Title == expected.Title
			&& actual.Body == expected.Body && actual.Collections.SequenceEqual(expected.Collections)
			&& actual.Visibility.Namespace == expected.Visibility.Namespace && actual.Visibility.Categories.SequenceEqual(expected.Visibility.Categories)
			&& actual.Visibility.RuleIds.SequenceEqual(expected.Visibility.RuleIds) && actual.Visibility.Published == expected.Visibility.Published
			&& actual.Visibility.DraftAuthor == expected.Visibility.DraftAuthor && actual.Stamp == expected.Stamp).IsTrue();
	}

}
