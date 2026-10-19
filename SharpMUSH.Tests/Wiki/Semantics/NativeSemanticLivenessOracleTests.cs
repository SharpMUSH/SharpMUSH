using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text.Json;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Models.Wiki.Semantics;
using SharpMUSH.Tools.WikiSemantics;
using SharpMUSH.Tools.WikiSemantics.Native;

namespace SharpMUSH.Tests.Wiki.Semantics;

public class NativeSemanticLivenessOracleTests
{
	[Test]
	public async Task ReopenedDeletedNativeTargetRemainsUnavailableToOfflineEqualityAndProjection()
	{
		using var fixture = new NativeSemanticFixture();
		var store = new NativeFixtureStore(fixture, NativeSemanticFixture.Schema);
		var subject = NativeSemanticFixture.Page("subject");
		var target = NativeSemanticFixture.Page("old-target");
		// The source fact is nonzero and committed through the existing B1 aggregate.
		(await store.CommitAsync(NativeSemanticFixture.Prepare(subject, value: target.Reference))).Expect<NativeCommitted>();
		(await store.CommitAsync(NativeSemanticFixture.Prepare(target))).Expect<NativeCommitted>();
		var query = new SemanticQuery("collection", [], ["test:p"], new IdSort(), IncludeCount: true);
		var live = ProjectNativeSnapshot(store);
		var before = ReferenceEvaluator.Evaluate(live, query with
		{
			Predicates = [new EqualPredicate("test:p", target.Reference)]
		}, WikiVisibility.All).Expect<QuerySuccess>();
		await Assert.That(before.Rows.Single().Subject).IsEqualTo(subject.Reference);
		await Assert.That(before.Rows.Single().Cells.Single().Values.Single() is PageRef liveTarget
			&& liveTarget == target.Reference).IsTrue();

		(await store.CommitAsync(NativeSemanticFixture.Prepare(target, version: 1, deleted: true))).Expect<NativeCommitted>();
		// A replacement reuses the portable label/title but receives another immutable PageRef.
		var replacement = NativeSemanticFixture.Page("new-target") with { Key = target.Key, Title = target.Title };
		(await store.CommitAsync(NativeSemanticFixture.Prepare(replacement))).Expect<NativeCommitted>();
		fixture.Reopen();
		store = new(fixture, NativeSemanticFixture.Schema);
		var nativeBeforeOracle = NativeSemanticCommitTests.Snapshot(store);
		var snapshot = ProjectNativeSnapshot(store);
		await Assert.That(snapshot.Pages.Select(page => page.Reference.Id)).IsEquivalentTo(new[] { "subject", "new-target" });
		await Assert.That(snapshot.Pages.Single(page => page.Reference == replacement.Reference).Key).IsEqualTo(target.Key);
		// Independent exact-ID oracle: neither a display label nor a replacement identity rewrites the edge.
		var incoming = snapshot.Facts.Facts.Single();
		await Assert.That(incoming.Subject).IsEqualTo(subject.Reference);
		await Assert.That(incoming.Property).IsEqualTo("test:p");
		await Assert.That(incoming.Value is PageRef original && original.Id == "old-target").IsTrue();
		await Assert.That(incoming.Value is PageRef healed && healed == replacement.Reference).IsFalse();
		var projected = ReferenceEvaluator.Evaluate(snapshot, query, WikiVisibility.All).Expect<QuerySuccess>();
		await Assert.That(projected.Rows.Select(row => row.Subject.Id)).IsEquivalentTo(new[] { "subject", "new-target" });
		await Assert.That(projected.Count).IsEqualTo(2L);
		await Assert.That(projected.HasMore).IsFalse();
		await Assert.That(projected.Rows.Single(row => row.Subject == subject.Reference).Cells.Single().Values).IsEmpty();
		var withoutIncoming = snapshot with { Facts = new(snapshot.Schema.Version, []) };
		await Assert.That(JsonSerializer.Serialize(projected)).IsEqualTo(JsonSerializer.Serialize(
			ReferenceEvaluator.Evaluate(withoutIncoming, query, WikiVisibility.All).Expect<QuerySuccess>()));
		var deletedEquality = ReferenceEvaluator.Evaluate(snapshot, query with
		{
			Predicates = [new EqualPredicate("test:p", target.Reference)]
		}, WikiVisibility.All).Expect<QueryFailure>();
		var missingEquality = ReferenceEvaluator.Evaluate(snapshot, query with
		{
			Predicates = [new EqualPredicate("test:p", new PageRef("never-existed"))]
		}, WikiVisibility.All).Expect<QueryFailure>();
		await Assert.That(deletedEquality.Code).IsEqualTo(QueryFailureCode.UnavailableReference);
		await Assert.That(deletedEquality).IsEqualTo(missingEquality);
		var replacementEquality = ReferenceEvaluator.Evaluate(snapshot, query with
		{
			Predicates = [new EqualPredicate("test:p", replacement.Reference)]
		}, WikiVisibility.All).Expect<QuerySuccess>();
		await Assert.That(replacementEquality.Rows).IsEmpty();
		await Assert.That(replacementEquality.Count).IsEqualTo(0L);
		await Assert.That(NativeSemanticCommitTests.Snapshot(store)).IsEqualTo(nativeBeforeOracle);
	}

	// Tiny complete offline observation of three fixture pages. Each decoder constructs owned models
	// while its checked native batch is live. No native transaction/borrowed entry crosses an await.
	private static SemanticSnapshot ProjectNativeSnapshot(NativeFixtureStore store)
	{
		var summaries = ReadOwned(store, "summaries", bytes => NativeStateCodec.Summary(bytes));
		var pages = ReadOwned(store, "pages", bytes => NativeCodec.DecodePage(bytes));
		var facts = ReadOwned(store, "facts", bytes => NativeCodec.DecodeFacts(bytes));
		if (!summaries.Keys.ToHashSet().SetEquals(pages.Keys) || !summaries.Keys.ToHashSet().SetEquals(facts.Keys))
			throw new InvalidOperationException("Native fixture page/summary/fact identities differ.");
		var projected = ImmutableArray.CreateBuilder<PageSnapshot>();
		var retainedFacts = ImmutableArray.CreateBuilder<SemanticFact>();
		foreach (var (nativeId, state) in summaries)
		{
			var summary = state.Summary;
			var page = pages[nativeId];
			if (summary.NativeId != nativeId || summary.Reference != page.Reference
				|| facts[nativeId].SchemaVersion != NativeSemanticFixture.Schema.Version)
				throw new InvalidOperationException("Native fixture summary identity/schema differs.");
			// Incoming facts are preserved by native commit; exclude deleted subjects, not dead targets.
			retainedFacts.AddRange(facts[nativeId].Facts);
			if (summary.Deleted) continue;
			projected.Add(page with
			{
				Reference = summary.Reference,
				Title = summary.Title,
				Visibility = summary.Visibility,
				Collections = [summary.Collection]
			});
		}
		return new(NativeSemanticFixture.Schema, projected.ToImmutable(),
			new(NativeSemanticFixture.Schema.Version, retainedFacts.ToImmutable()), SemanticReferenceTests.Stamp);
	}

	private static Dictionary<ulong, T> ReadOwned<T>(NativeFixtureStore store, string suffix, Func<byte[], T> decode)
	{
		var table = store.Tables.Single(table => table.Name.EndsWith(suffix, StringComparison.Ordinal));
		using var rows = store.Inspect(table, [], NativeSemanticFixture.Budget());
		if (!rows.Exhausted) throw new InvalidOperationException("Tiny liveness fixture observation is incomplete.");
		var result = new Dictionary<ulong, T>();
		foreach (var row in rows.Entries)
			result.Add(BinaryPrimitives.ReadUInt64BigEndian(row.Key), decode(row.Value));
		return result;
	}
}
