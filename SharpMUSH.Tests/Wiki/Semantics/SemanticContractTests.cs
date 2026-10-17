using System.Collections.Immutable;
using SharpMUSH.Tools.WikiSemantics;
namespace SharpMUSH.Tests.Wiki.Semantics;

public class SemanticContractTests
{
	private static readonly PageRef Subject = new("subject-1");
	private static readonly FactProvenance Provenance = new("pack", "commit", "page.md", "level", false);
	private static SemanticSchema Schema(SemanticType type = SemanticType.Keyword, Cardinality cardinality = Cardinality.Multiple,
		ImmutableArray<Keyword> allowed = default) => new(1, [new("p", 1, type, cardinality, allowed.IsDefault ? [] : allowed)]);
	private static SemanticFact Fact(SemanticValue value) => new(Subject, "p", value, Provenance);
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
		var definition = new PropertyDefinition("p", 1, SemanticType.Keyword, Cardinality.Multiple, [new(" CAFÉ ")]);
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
		var properties = Enumerable.Range(0, 129).Select(i => new PropertyDefinition($"p{i}", 1, SemanticType.Keyword, Cardinality.Multiple, [])).ToImmutableArray();
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
	private static async Task Error(SemanticSchema schema, IEnumerable<SemanticFact> facts, ValidationCode code)
		=> await Assert.That(CanonicalFacts.Validate(schema, facts) is ValidationFailure error && error.Code == code).IsTrue();
}
