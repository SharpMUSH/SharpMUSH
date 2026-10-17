using System.Collections.Immutable;
using SharpMUSH.Tools.WikiSemantics;
namespace SharpMUSH.Tests.Wiki.Semantics;

public class SemanticContractTests
{
	private static readonly PageRef Subject = new("subject-1");
	private static readonly FactProvenance Provenance = new("pack", "commit", "page.md", "level", false);
	private static SemanticSchema Schema(SemanticType type = SemanticType.Keyword, Cardinality cardinality = Cardinality.Multiple,
		ImmutableArray<Keyword> allowed = default) => new(1, [new("test:p", 1, type, cardinality, allowed.IsDefault ? [] : allowed)]);
	private static SemanticFact Fact(SemanticValue value) => new(Subject, "test:p", value, Provenance);
	[Test]
	public async Task CanonicalKeywordsDeduplicateAndNormalize()
	{
		var result = CanonicalFacts.Validate(Schema(), [Fact(new Keyword("  CAFÉ ")), Fact(new Keyword("cafe\u0301"))]);
		await Assert.That(result is CanonicalFactSet set && set.Facts.Length == 1 && set.Facts[0].Value is Keyword k && k.Value == "café").IsTrue();
	}
	[Test]
	public async Task RejectsInvalidValuesAndSchema()
	{
		await Error(Schema(), [Fact(new Keyword(new string('é', 129)))], ValidationCode.InvalidKeyword);
		await Error(Schema(allowed: [new("allowed")]), [Fact(new Keyword("other"))], ValidationCode.InvalidEnum);
		await Error(Schema(), [Fact(0L)], ValidationCode.WrongType);
		await Error(Schema(SemanticType.Integer, Cardinality.Single), [Fact(0L), Fact(1L)], ValidationCode.Cardinality);
		await Error(new(2, Schema().Properties), [], ValidationCode.InvalidSchema);
	}
	[Test]
	public async Task ZeroFalseAndSignedExtremesAreKnownFacts()
	{
		foreach (var value in new long[] { long.MinValue, 0, long.MaxValue })
		{
			var result = CanonicalFacts.Validate(Schema(SemanticType.Integer), [Fact(value)]);
			await Assert.That(result is CanonicalFactSet set && set.Facts.Length == 1 && set.Facts[0].Value is long actual && actual == value).IsTrue();
		}
		await Assert.That(CanonicalFacts.Validate(Schema(SemanticType.Boolean), [Fact(false)]) is CanonicalFactSet b && b.Facts.Length == 1).IsTrue();
		await Assert.That(CanonicalFacts.Validate(Schema(), []) is CanonicalFactSet empty && empty.Facts.IsEmpty).IsTrue();
	}
	[Test]
	public async Task CanonicalizationLeavesSchemaAndInputUntouched()
	{
		var definition = new PropertyDefinition("test:p", 1, SemanticType.Keyword, Cardinality.Multiple, [new(" CAFÉ ")]);
		var input = Fact(new Keyword(" CAFE\u0301 "));
		var result = CanonicalFacts.Validate(new(1, [definition]), [input]);
		await Assert.That(result is CanonicalFactSet set && set.SchemaVersion == 1 && set.Facts.Length == 1 && set.Facts[0].Value is Keyword value && value.Value == "café").IsTrue();
		await Assert.That(definition.AllowedKeywords[0].Value).IsEqualTo(" CAFÉ ");
		await Assert.That(input.Value is Keyword original && original.Value == " CAFE\u0301 ").IsTrue();
	}
	[Test]
	public async Task EnforcesPropertyQuota()
		=> await Error(Schema(SemanticType.Integer), Enumerable.Range(0, 33).Select(x => Fact((long)x)), ValidationCode.Quota);
	[Test]
	public async Task EnforcesSubjectAndCanonicalDataQuotas()
	{
		var properties = Enumerable.Range(0, 129).Select(i => new PropertyDefinition($"test:p{i}", 1, SemanticType.Keyword, Cardinality.Multiple, [])).ToImmutableArray();
		var facts = properties.Select(p => new SemanticFact(Subject, p.Id, new Keyword("v"), Provenance));
		await Error(new(1, properties), facts, ValidationCode.Quota);
		var big = Enumerable.Range(0, 32).Select(i => Fact(new Keyword(new string('a', 250) + i))).Select(f => f with { Provenance = Provenance with { SourcePath = new string('x', 2200) } });
		await Error(Schema(), big, ValidationCode.Quota);
	}
	[Test]
	public async Task AliasesCannotGuessOrRetargetDeletedIncarnations()
	{
		var key = new PortableKey("pack", "Main", "key");
		await Assert.That(CanonicalFacts.Resolve(key, [new(key, new("old")), new(key, new("new"))], new HashSet<PageRef> { new("new") }) is ValidationFailure a && a.Code == ValidationCode.AmbiguousAlias).IsTrue();
		await Assert.That(CanonicalFacts.Resolve(key, [new(key, new("old"))], new HashSet<PageRef> { new("new") }) is ValidationFailure m && m.Code == ValidationCode.MissingTarget).IsTrue();
		await Assert.That(CanonicalFacts.Resolve(key, [new(key, new("old"))], new HashSet<PageRef> { new("old") }) is PageRef p && p.Id == "old").IsTrue();
	}
	[Test]
	public async Task RejectsUnqualifiedAndMalformedPropertyIdentity()
	{
		foreach (var id in new[] { "p", ":p", "test:", "a:b:c", "a :b", "a:b\n" })
			await Error(new(1, [new(id, 1, SemanticType.Keyword, Cardinality.Multiple, [])]), [], ValidationCode.InvalidSchema);
	}
	[Test]
	public async Task MalformedMetadataReturnsTypedFailure()
	{
		await Error(Schema(), [Fact(new Keyword("v")) with { Property = null! }], ValidationCode.UnknownProperty);
		await Error(new(1, [null!]), [], ValidationCode.InvalidSchema);
		await Error(Schema(), [null!], ValidationCode.InvalidSchema);
	}
	[Test]
	public async Task CanonicalEnumDuplicatesAreInvalid()
		=> await Error(Schema(allowed: [new("CAFÉ"), new("cafe\u0301")]), [], ValidationCode.InvalidSchema);
	[Test]
	public async Task PermutationsProduceSameOrderAndEvidenceWinner()
	{
		var first = Fact(new Keyword("z"));
		var second = Fact(new Keyword("a")) with { Provenance = Provenance with { SourcePath = "z.md" } };
		var winner = second with { Provenance = Provenance with { SourcePath = "a.md" } };
		foreach (var input in new[] { new[] { first, second, winner }, new[] { winner, second, first } })
		{
			var result = CanonicalFacts.Validate(Schema(), input);
			await Assert.That(result is CanonicalFactSet set && set.Facts.Length == 2
				&& set.Facts[0].Value is Keyword a && a.Value == "a" && set.Facts[0].Provenance.SourcePath == "a.md"
				&& set.Facts[1].Value is Keyword z && z.Value == "z").IsTrue();
		}
	}
	[Test]
	public async Task InclusiveQuotaBoundariesAreAccepted()
	{
		await Assert.That(CanonicalFacts.Validate(Schema(), [Fact(new Keyword(new string('é', 128)))]) is CanonicalFactSet).IsTrue();
		await Assert.That(CanonicalFacts.Validate(Schema(SemanticType.Integer), Enumerable.Range(0, 32).Select(i => Fact((long)i))) is CanonicalFactSet values && values.Facts.Length == 32).IsTrue();
		var properties = Enumerable.Range(0, 128).Select(i => new PropertyDefinition($"test:p{i}", 1, SemanticType.Keyword, Cardinality.Single, [])).ToImmutableArray();
		await Assert.That(CanonicalFacts.Validate(new(1, properties), properties.Select(p => Fact(new Keyword("v")) with { Property = p.Id })) is CanonicalFactSet subject && subject.Facts.Length == 128).IsTrue();
		// One ASCII canonical JSON fact occupies 271 bytes excluding SourcePath contents.
		var boundary = Fact(new Keyword("v")) with { Provenance = Provenance with { SourcePath = new string('x', 65536 - 271) } };
		await Assert.That(CanonicalFacts.Validate(Schema(), [boundary]) is CanonicalFactSet).IsTrue();
		await Error(Schema(), [boundary with { Provenance = boundary.Provenance with { SourcePath = boundary.Provenance.SourcePath + "x" } }], ValidationCode.Quota);
	}
	private static async Task Error(SemanticSchema schema, IEnumerable<SemanticFact> facts, ValidationCode code)
		=> await Assert.That(CanonicalFacts.Validate(schema, facts) is ValidationFailure error && error.Code == code).IsTrue();
}
