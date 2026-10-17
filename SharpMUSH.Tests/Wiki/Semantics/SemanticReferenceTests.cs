using System.Collections.Immutable;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Tools.WikiSemantics;

namespace SharpMUSH.Tests.Wiki.Semantics;

public class SemanticReferenceTests
{
	internal static readonly ExperimentalStamp Stamp = new("test", 1, 1, 1, 1, 1, 1, 1);
	internal static PageSnapshot Page(string id, string ns = "main") => new(new(id), new("pack", ns, id), "Same", "", ["package:pf2e-remaster"], new(ns, [], [], true, null), Stamp);
	internal static SemanticSnapshot Graph(params SemanticFact[] facts) => new(new(1, [
		new("t:level", 1, SemanticType.Integer, Cardinality.Single, []),
		new("t:class", 1, SemanticType.Keyword, Cardinality.Multiple, [new("mage"), new("fighter")]),
		new("t:link", 1, SemanticType.PageRef, Cardinality.Multiple, [])]),
		[Page("a"), Page("b"), Page("c"), Page("d")], new(1, [.. facts]), Stamp);
	internal static SemanticFact Fact(string id, string prop, SemanticValue value) => new(new(id), prop, value, new("pack", "commit", "path", "field", false));
	internal static SemanticQuery Query => new("package:pf2e-remaster", [], [], new IdSort(), IncludeCount: true);
	internal static QuerySuccess Success(SemanticSnapshot snapshot, SemanticQuery? query = null, WikiVisibility? visibility = null) => ReferenceEvaluator.Evaluate(snapshot, query ?? Query, visibility ?? WikiVisibility.All).Expect<QuerySuccess>();

	[Test]
	public async Task IntegerOrderIsNumericWithMissingLastAndIdTieBreak()
	{
		var graph = Graph(Fact("a", "t:level", 10L), Fact("b", "t:level", -2L), Fact("c", "t:level", 10L));
		await Assert.That(string.Join(",", Success(graph, Query with { Sort = new IntegerSort("t:level") }).Rows.Select(r => r.Subject.Id))).IsEqualTo("b,a,c,d");
		await Assert.That(string.Join(",", Success(graph, Query with { Sort = new IntegerSort("t:level", true) }).Rows.Select(r => r.Subject.Id))).IsEqualTo("a,c,b,d");
	}
	[Test]
	public async Task ConjunctionDeduplicatesBeforePagingCountAndEnumFacets()
	{
		var graph = Graph(Fact("a", "t:level", 2L), Fact("a", "t:class", new Keyword("mage")), Fact("a", "t:class", new Keyword("mage")), Fact("b", "t:level", 3L), Fact("b", "t:class", new Keyword("fighter")));
		var result = Success(graph, Query with { Predicates = [new IntegerRangePredicate("t:level", 1, 3), new EqualPredicate("t:class", new Keyword("mage"))], Columns = ["t:class"], Facets = ["t:class"], PageSize = 1 });
		await Assert.That(result.Rows.Single().Subject.Id).IsEqualTo("a");
		await Assert.That(result.Rows[0].Cells[0].Values.Length).IsEqualTo(1);
		await Assert.That(result.Count).IsEqualTo(1L);
		await Assert.That(result.Facets[0].Buckets.Single().Count).IsEqualTo(1);
		await Assert.That(result.HasMore).IsFalse();
	}
	[Test]
	public async Task LookaheadAndOffsetUseUniqueSubjects()
	{
		foreach (var size in new[] { 3, 4, 5 })
		{
			var result = Success(Graph(), Query with { PageSize = size });
			await Assert.That(result.Rows.Length).IsEqualTo(Math.Min(size, 4));
			await Assert.That(result.HasMore).IsEqualTo(size == 3);
		}
		await Assert.That(Success(Graph(), Query with { Offset = 3, PageSize = 1 }).Rows.Single().Subject.Id).IsEqualTo("d");
	}
	[Test]
	public async Task RejectsUnsupportedShapesWithoutGuessing()
	{
		foreach (var query in new[] { Query with { PageSize = 0 }, Query with { PageSize = 101 }, Query with { Offset = -1 }, Query with { Sort = new IntegerSort("t:class") }, Query with { Predicates = [new IntegerRangePredicate("t:level", 3, 2)] }, Query with { Columns = ["unknown"] } })
			await Assert.That(ReferenceEvaluator.Evaluate(Graph(), query, WikiVisibility.All) is QueryFailure).IsTrue();
		await Assert.That(ReferenceEvaluator.Evaluate(Graph(), Query, WikiVisibility.All with { Search = "Same" }) is QueryFailure).IsTrue();
	}
	[Test]
	public async Task QueryLimitsAndInvalidSnapshotAreExplicit()
	{
		await Assert.That(Success(Graph(), Query with { PageSize = 100 }).Rows.Length).IsEqualTo(4);
		var predicate = new IntegerRangePredicate("t:level", long.MinValue, long.MaxValue);
		await Assert.That(Success(Graph(), Query with { Predicates = [predicate, predicate, predicate, predicate] }).Count).IsEqualTo(0L);
		await Assert.That(ReferenceEvaluator.Evaluate(Graph(), Query with { Predicates = [predicate, predicate, predicate, predicate, predicate] }, WikiVisibility.All) is QueryFailure failure && failure.Code == QueryFailureCode.InvalidQuery).IsTrue();
		var invalid = Graph(Fact("a", "t:level", 1L), Fact("a", "t:level", 2L));
		await Assert.That(ReferenceEvaluator.Evaluate(invalid, Query, WikiVisibility.All) is QueryFailure snapshotFailure && snapshotFailure.Code == QueryFailureCode.InvalidSnapshot).IsTrue();
		var definitions = Enumerable.Range(0, 7).Select(i => new PropertyDefinition($"t:p{i}", 1, SemanticType.Boolean, Cardinality.Single, [])).ToImmutableArray();
		var graph = Graph() with { Schema = new(1, definitions) };
		var columns = definitions.Select(p => p.Id).ToImmutableArray();
		await Assert.That(Success(graph, Query with { Columns = columns.Take(6).ToImmutableArray() }).Rows.Length).IsEqualTo(4);
		await Assert.That(ReferenceEvaluator.Evaluate(graph, Query with { Columns = columns }, WikiVisibility.All) is QueryFailure).IsTrue();
	}

	[Test]
	public async Task FacetsAcceptOnlyRegisteredEnumsAtMostSixteenMembers()
	{
		foreach (var property in new[] { "t:link", "t:level" })
			await Assert.That(ReferenceEvaluator.Evaluate(Graph(), Query with { Facets = [property] }, WikiVisibility.All) is QueryFailure).IsTrue();
		foreach (var size in new[] { 0, 15, 16, 17 })
		{
			var graph = Graph() with { Schema = new(1, [new("t:p", 1, SemanticType.Keyword, Cardinality.Multiple, [.. Enumerable.Range(0, size).Select(i => new Keyword($"v{i}"))])]) };
			await Assert.That(ReferenceEvaluator.Evaluate(graph, Query with { Facets = ["t:p"] }, WikiVisibility.All) is QuerySuccess).IsEqualTo(size is 15 or 16);
		}
	}

	[Test]
	public async Task CancellationIsObservedBeforeScanning()
	{
		using var source = new CancellationTokenSource();
		source.Cancel();
		await Assert.That(() => ReferenceEvaluator.Evaluate(Graph(), Query, WikiVisibility.All, source.Token)).Throws<OperationCanceledException>();
	}
	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async Task NullNestedSnapshotRecordsReturnInvalidSnapshot(bool nullSchema)
	{
		var snapshot = nullSchema ? Graph() with { Schema = null! } : Graph() with { Facts = null! };
		await Assert.That(ReferenceEvaluator.Evaluate(snapshot, Query, WikiVisibility.All) is QueryFailure failure && failure.Code == QueryFailureCode.InvalidSnapshot).IsTrue();
	}

	[Test]
	public async Task MultipleIntegerCannotBeAnOrderKeyRegardlessOfPresentValues()
	{
		foreach (var values in new long[][] { [], [0], [-1, 1] })
		{
			var graph = Graph([.. values.Select(value => Fact("a", "t:level", value))]);
			graph = graph with { Schema = graph.Schema with { Properties = [.. graph.Schema.Properties.Select(property => property.Id == "t:level" ? property with { Cardinality = Cardinality.Multiple } : property)] } };
			await Assert.That(ReferenceEvaluator.Evaluate(graph, Query with { Sort = new IntegerSort("t:level") }, WikiVisibility.All) is QueryFailure failure && failure.Code == QueryFailureCode.InvalidQuery).IsTrue();
		}
	}

	[Test]
	public async Task IntegerOrderCoversTheEntireSignedDomainWithoutOverflow()
	{
		var graph = Graph(Fact("a", "t:level", long.MaxValue), Fact("b", "t:level", 0L), Fact("c", "t:level", long.MinValue));
		await Assert.That(string.Join(",", Success(graph, Query with { Sort = new IntegerSort("t:level") }).Rows.Select(row => row.Subject.Id))).IsEqualTo("c,b,a,d");
		await Assert.That(string.Join(",", Success(graph, Query with { Sort = new IntegerSort("t:level", true) }).Rows.Select(row => row.Subject.Id))).IsEqualTo("a,b,c,d");
	}

}
