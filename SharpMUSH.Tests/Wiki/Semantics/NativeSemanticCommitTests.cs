using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Tools.WikiSemantics.Native;
using SharpMUSH.Database.Lightning.Semantics;
using SharpMUSH.Library.Models.Wiki.Semantics;

namespace SharpMUSH.Tests.Wiki.Semantics;

public class NativeSemanticCommitTests
{
	// Complete-table oracles are separately allocated offline evidence. No borrowed native arrays/models escape.
	internal static string Snapshot(NativeFixtureStore store, string? suffix = null)
	{
		var lines = new List<string>();
		foreach (var table in store.Tables.Where(t => suffix is null || t.Name.EndsWith(suffix, StringComparison.Ordinal)))
		{
			byte[] after = [];
			while (true)
			{
				using var batch = store.Inspect(table, after, NativeSemanticFixture.Budget());
				foreach (var entry in batch.Entries) lines.Add(table.Name + ":" + Convert.ToHexString(entry.Key) + "=" + Convert.ToHexString(entry.Value));
				if (batch.Exhausted) break;
				after = batch.Entries[^1].Key.ToArray();
			}
		}
		return string.Join('\n', lines);
	}
	private static List<(byte[] Key, byte[] Value)> OwnedRows(NativeFixtureStore store, string suffix)
	{
		var rows = new List<(byte[], byte[])>(); var table = store.Tables.Single(t => t.Name.EndsWith(suffix, StringComparison.Ordinal));
		byte[] after = [];
		while (true)
		{
			using var batch = store.Inspect(table, after, NativeSemanticFixture.Budget());
			foreach (var row in batch.Entries) rows.Add((row.Key.ToArray(), row.Value.ToArray()));
			if (batch.Exhausted) break;
			after = batch.Entries[^1].Key.ToArray();
		}
		return rows;
	}
	private static void AssertIndexSymmetry(NativeFixtureStore store)
	{
		var identities = new Dictionary<(NativeRegistryKind, string), ulong>();
		// This dictionary resolves scalar index values only. Retired class reverse rows
		// intentionally retain repeated external identities with distinct historical IDs.
		foreach (var row in OwnedRows(store, "registry").Where(r => r.Key.Length == 9
			&& (NativeRegistryKind)r.Key[0] is NativeRegistryKind.Page or NativeRegistryKind.Property or NativeRegistryKind.Keyword))
		{
			var entry = NativeStateCodec.Registry(row.Value);
			identities.Add(((NativeRegistryKind)row.Key[0], entry.Identity), entry.Id);
		}
		var states = OwnedRows(store, "summaries").Select(r => NativeStateCodec.Summary(r.Value)).ToDictionary(s => s.Summary.NativeId);
		var forwards = new HashSet<string>(StringComparer.Ordinal); var postings = new HashSet<string>(StringComparer.Ordinal);
		foreach (var row in OwnedRows(store, "facts"))
		{
			var id = BinaryPrimitives.ReadUInt64BigEndian(row.Key); var state = states[id];
			foreach (var fact in NativeCodec.DecodeFacts(row.Value).Facts)
			{
				var property = identities[(NativeRegistryKind.Property, fact.Property)];
				byte tag; ulong scalar;
				switch (fact.Value)
				{
					case PageRef page: tag = 1; scalar = identities[(NativeRegistryKind.Page, page.Id)]; break;
					case long integer: tag = 2; scalar = unchecked((ulong)integer) ^ 0x8000000000000000UL; break;
					case bool boolean: tag = 3; scalar = boolean ? 1UL : 0UL; break;
					case Keyword keyword: tag = 4; scalar = identities[(NativeRegistryKind.Keyword, keyword.Value)]; break;
					default: throw new InvalidOperationException("Unexpected fact.");
				}
				var forward = new byte[25]; BinaryPrimitives.WriteUInt64BigEndian(forward, id);
				BinaryPrimitives.WriteUInt64BigEndian(forward.AsSpan(8), property); forward[16] = tag;
				BinaryPrimitives.WriteUInt64BigEndian(forward.AsSpan(17), scalar);
				var posting = new byte[41]; BinaryPrimitives.WriteUInt64BigEndian(posting, state.CollectionId);
				BinaryPrimitives.WriteUInt64BigEndian(posting.AsSpan(8), state.ClassId);
				BinaryPrimitives.WriteUInt64BigEndian(posting.AsSpan(16), property); posting[24] = tag;
				BinaryPrimitives.WriteUInt64BigEndian(posting.AsSpan(25), scalar); BinaryPrimitives.WriteUInt64BigEndian(posting.AsSpan(33), id);
				forwards.Add(Convert.ToHexString(forward)); postings.Add(Convert.ToHexString(posting));
			}
		}
		foreach (var (role, expected) in new[] { ("forward", forwards), ("postings", postings) })
		{
			var rows = OwnedRows(store, role);
			if (rows.Any(r => r.Value.Length != 0) || !expected.SetEquals(rows.Select(r => Convert.ToHexString(r.Key))))
				throw new InvalidOperationException("Complete independent index symmetry failed for " + role);
		}
		if (!OwnedRows(store, "pages").Select(r => Convert.ToHexString(r.Key)).ToHashSet().SetEquals(OwnedRows(store, "facts").Select(r => Convert.ToHexString(r.Key))))
			throw new InvalidOperationException("Page/facts key sets disagree.");
	}

	private static long Version(NativeFixtureStore store, string id)
	{
		var budget = NativeSemanticFixture.Budget();
		using var lifetime = budget.ReserveWorking(0);
		return store.ReadPage(new(id), budget, lifetime).Expect<NativeStoredPage>().StateVersion;
	}
	private static ulong NativeId(NativeFixtureStore store, string id)
	{
		var budget = NativeSemanticFixture.Budget();
		using var lifetime = budget.ReserveWorking(0);
		return store.ReadPage(new(id), budget, lifetime).Expect<NativeStoredPage>().Summary.NativeId;
	}
	private static long MutationSequence(NativeFixtureStore store, Guid id)
	{
		var budget = NativeSemanticFixture.Budget();
		using var lifetime = budget.ReserveWorking(0);
		return store.GetMutation(id, budget, lifetime).Expect<NativeMutationStatus>().Committed.CommitSequence;
	}
	private static long Rows(NativeFixtureStore store, string suffix)
		=> Snapshot(store, suffix).Split('\n', StringSplitOptions.RemoveEmptyEntries).LongLength;
	[Test]
	public async Task MutationIsAtomicAcrossFactsAndIndexes()
	{
		using var fixture = new NativeSemanticFixture();
		var store = new NativeFixtureStore(fixture, NativeSemanticFixture.Schema);
		var edit = NativeSemanticFixture.Prepare(value: 1L);
		await Assert.That(await store.CommitAsync(edit) is NativeCommitted { StateVersion: 1, CommitSequence: 1 }).IsTrue();
		await Assert.That(Rows(store, "forward")).IsEqualTo(1L);
		await Assert.That(Rows(store, "postings")).IsEqualTo(1L);
		await Assert.That(Rows(store, "catalog")).IsEqualTo(1L);
		await Assert.That(await store.CommitAsync(NativeSemanticFixture.Prepare(version: 1, value: 2L)) is NativeCommitted { StateVersion: 2 }).IsTrue();
		AssertIndexSymmetry(store);
		var before = Snapshot(store);
		fixture.Reopen(); store = new(fixture, NativeSemanticFixture.Schema);
		AssertIndexSymmetry(store);
		await Assert.That(Snapshot(store)).IsEqualTo(before);
		await Assert.That(Rows(store, "postings")).IsEqualTo(1L);
		var budget = NativeSemanticFixture.Budget();
		using var lifetime = budget.ReserveWorking(0);
		var page = store.ReadPage(new("subject"), budget, lifetime).Expect<NativeStoredPage>();
		await Assert.That(page.Facts.Facts.Single().Value is long value && value == 2).IsTrue();
		await Assert.That(page.Page.Stamp).IsEqualTo(edit.Page.Stamp);
		await Assert.That(page.CommitSequence).IsEqualTo(2L);
	}
	[Test]
	public async Task CasFailureDoesNotAllocateState()
	{
		using var fixture = new NativeSemanticFixture();
		var store = new NativeFixtureStore(fixture, NativeSemanticFixture.Schema);
		var before = Snapshot(store);
		await Assert.That(await store.CommitAsync(NativeSemanticFixture.Prepare(version: 1, value: new PageRef("absent"))) is NativeConflict { CurrentStateVersion: 0 }).IsTrue();
		await Assert.That(Snapshot(store)).IsEqualTo(before);
	}
	[Test]
	public async Task MetadataOnlyEditMovesStateVersion()
	{
		using var fixture = new NativeSemanticFixture();
		var store = new NativeFixtureStore(fixture, NativeSemanticFixture.Schema);
		await store.CommitAsync(NativeSemanticFixture.Prepare(value: 1L));
		await store.CommitAsync(NativeSemanticFixture.Prepare(NativeSemanticFixture.Page() with { Title = "renamed", Body = "unchanged facts" }, 1, 1L));
		await Assert.That(Version(store, "subject")).IsEqualTo(2L);
		await Assert.That(Rows(store, "postings")).IsEqualTo(1L);
		var budget = NativeSemanticFixture.Budget(); using var lifetime = budget.ReserveWorking(0);
		await Assert.That(store.ReadPage(new("subject"), budget, lifetime).Expect<NativeStoredPage>().Summary.Title).IsEqualTo("renamed");
	}
	[Test]
	public async Task ZeroFactTargetMovesStateVersion()
	{
		using var fixture = new NativeSemanticFixture(); var store = new NativeFixtureStore(fixture, NativeSemanticFixture.Schema);
		await store.CommitAsync(NativeSemanticFixture.Prepare());
		await store.CommitAsync(NativeSemanticFixture.Prepare(version: 1));
		await Assert.That(Version(store, "subject")).IsEqualTo(2L);
		await Assert.That(Rows(store, "catalog")).IsEqualTo(0L);
		await Assert.That(Rows(store, "postings")).IsEqualTo(0L);
	}
	[Test]
	public async Task DeletedTargetDoesNotRetargetReplacement()
	{
		using var fixture = new NativeSemanticFixture(); var store = new NativeFixtureStore(fixture, NativeSemanticFixture.Schema);
		await store.CommitAsync(NativeSemanticFixture.Prepare(value: new PageRef("target")));
		await store.CommitAsync(NativeSemanticFixture.Prepare(NativeSemanticFixture.Page("target")));
		var targetId = NativeId(store, "target"); var postings = Snapshot(store, "postings");
		await store.CommitAsync(NativeSemanticFixture.Prepare(NativeSemanticFixture.Page("target"), 1, deleted: true));
		var before = Snapshot(store);
		await Assert.That(await store.CommitAsync(NativeSemanticFixture.Prepare(NativeSemanticFixture.Page("target"), 2)) is NativeConflict).IsTrue();
		await Assert.That(Snapshot(store)).IsEqualTo(before);
		await store.CommitAsync(NativeSemanticFixture.Prepare(NativeSemanticFixture.Page("new-target") with { Title = "target" }));
		await Assert.That(NativeId(store, "new-target") > targetId).IsTrue();
		await Assert.That(Snapshot(store, "postings")).IsEqualTo(postings);
		fixture.Reopen(); store = new(fixture, NativeSemanticFixture.Schema);
		var budget = NativeSemanticFixture.Budget(); using var lifetime = budget.ReserveWorking(0);
		await Assert.That(store.ReadPage(new("target"), budget, lifetime).Expect<NativeStoredPage>().Summary.Deleted).IsTrue();
	}
	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task SubjectAndTargetBootstrapInEitherOrder(bool targetFirst)
	{
		using var fixture = new NativeSemanticFixture(); var store = new NativeFixtureStore(fixture, NativeSemanticFixture.Schema);
		if (targetFirst) await store.CommitAsync(NativeSemanticFixture.Prepare(NativeSemanticFixture.Page("target")));
		await store.CommitAsync(NativeSemanticFixture.Prepare(value: new PageRef("target")));
		await store.CommitAsync(NativeSemanticFixture.Prepare(NativeSemanticFixture.Page("second"), value: new PageRef("target")));
		if (!targetFirst)
		{
			var budget = NativeSemanticFixture.Budget(); using var lifetime = budget.ReserveWorking(0);
			await Assert.That(store.ReadPage(new("target"), budget, lifetime) is NotFound).IsTrue();
			fixture.Reopen(); store = new(fixture, NativeSemanticFixture.Schema);
			await store.CommitAsync(NativeSemanticFixture.Prepare(NativeSemanticFixture.Page("target")));
		}
		await Assert.That(Rows(store, "postings")).IsEqualTo(2L);
		await Assert.That(Version(store, "target")).IsEqualTo(1L);
		await Assert.That(Rows(store, "pages")).IsEqualTo(3L);
	}
	[Test]
	public async Task MutationRetryAndPayloadMismatchAreDurable()
	{
		using var fixture = new NativeSemanticFixture(); var store = new NativeFixtureStore(fixture, NativeSemanticFixture.Schema);
		var edit = NativeSemanticFixture.Prepare(value: 1L);
		var result = (await store.CommitAsync(edit)).Expect<NativeCommitted>();
		var before = Snapshot(store);
		await Assert.That((await store.CommitAsync(edit)).Expect<NativeCommitted>()).IsEqualTo(result);
		await Assert.That(Snapshot(store)).IsEqualTo(before);
		await Assert.That(await store.CommitAsync(NativeSemanticFixture.Prepare(value: 2L, mutation: edit.MutationId)) is NativeMutationMismatch).IsTrue();
		await Assert.That(Snapshot(store)).IsEqualTo(before);
		fixture.Reopen(); store = new(fixture, NativeSemanticFixture.Schema);
		await Assert.That(MutationSequence(store, edit.MutationId)).IsEqualTo(1L);
		await Assert.That((await store.CommitAsync(edit)).Expect<NativeCommitted>()).IsEqualTo(result);
	}
	[Test]
	public async Task ExactSchemaCertificateAndReopenRejectSameVersionChanges()
	{
		using var fixture = new NativeSemanticFixture(); var store = new NativeFixtureStore(fixture, NativeSemanticFixture.Schema);
		var changed = NativeSemanticFixture.Schema with { Properties = [new("test:i", 1, SemanticType.Integer, Cardinality.Single, [])] };
		var edit = NativePagePreparation.Prepare(changed, NativeSemanticFixture.Page(), [], 0, Guid.NewGuid(), false).Expect<PreparedNativePage>();
		var before = Snapshot(store);
		await Assert.That(await store.CommitAsync(edit) is NativeUnavailable).IsTrue();
		await Assert.That(Snapshot(store)).IsEqualTo(before);
		await Assert.That(() => new NativeFixtureStore(fixture, changed)).Throws<InvalidDataException>();
		await Assert.That(Snapshot(store)).IsEqualTo(before);
	}
	[Test]
	public async Task ConstructorOwnsNestedSchemaArrays()
	{
		using var fixture = new NativeSemanticFixture();
		var keywords = new[] { new Keyword("allowed") };
		var definitions = new[] { new PropertyDefinition("test:k", 1, SemanticType.Keyword, Cardinality.Multiple, ImmutableCollectionsMarshal.AsImmutableArray(keywords)) };
		var schema = new SemanticSchema(1, ImmutableCollectionsMarshal.AsImmutableArray(definitions));
		var store = new NativeFixtureStore(fixture, schema);
		var prepared = NativePagePreparation.Prepare(schema, NativeSemanticFixture.Page(), [], 0, Guid.NewGuid(), false).Expect<PreparedNativePage>();
		keywords[0] = new("changed"); definitions[0] = definitions[0] with { Type = SemanticType.Integer };
		await Assert.That(await store.CommitAsync(prepared) is NativeCommitted).IsTrue();
	}
	[Test]
	public async Task ClassCatalogLimitDoesNotCreatePartialFacts()
	{
		using var fixture = new NativeSemanticFixture(); var store = new NativeFixtureStore(fixture, NativeSemanticFixture.Schema);
		for (var i = 0; i < 1024; i++)
			if (await store.CommitAsync(NativeSemanticFixture.Prepare(NativeSemanticFixture.Page("s" + i, "c" + i), value: 1L)) is not NativeCommitted)
				throw new InvalidOperationException("Catalog seed rejected " + i);
		var before = Snapshot(store);
		await Assert.That(await store.CommitAsync(NativeSemanticFixture.Prepare(NativeSemanticFixture.Page("overflow", "c1024"), value: 1L)) is NativeCapacity { Dimension: "catalog-classes" }).IsTrue();
		await Assert.That(Snapshot(store)).IsEqualTo(before);
		// Net replacement at capacity frees the last member before admitting its successor.
		await Assert.That(await store.CommitAsync(NativeSemanticFixture.Prepare(NativeSemanticFixture.Page("s0", "replacement"), 1, 2L)) is NativeCommitted).IsTrue();
		await Assert.That(Rows(store, "catalog")).IsEqualTo(1024L);
	}
	[Test]
	public async Task SharedClassReclamationAndReactivationNeverReuseIds()
	{
		using var fixture = new NativeSemanticFixture(); var store = new NativeFixtureStore(fixture, NativeSemanticFixture.Schema);
		await store.CommitAsync(NativeSemanticFixture.Prepare(value: 1L));
		var original = Snapshot(store, "postings");
		AssertIndexSymmetry(store);
		await store.CommitAsync(NativeSemanticFixture.Prepare(NativeSemanticFixture.Page("second"), value: 2L));
		await Assert.That(Rows(store, "catalog")).IsEqualTo(1L);
		AssertIndexSymmetry(store);
		await store.CommitAsync(NativeSemanticFixture.Prepare(version: 1));
		await Assert.That(Rows(store, "catalog")).IsEqualTo(1L);
		AssertIndexSymmetry(store);
		await store.CommitAsync(NativeSemanticFixture.Prepare(NativeSemanticFixture.Page("second"), 1));
		await Assert.That(Rows(store, "catalog")).IsEqualTo(0L);
		AssertIndexSymmetry(store);
		var history = Rows(store, "partitions");
		await store.CommitAsync(NativeSemanticFixture.Prepare(version: 2, value: 1L));
		await Assert.That(Snapshot(store, "postings") != original).IsTrue();
		await Assert.That(Rows(store, "partitions")).IsEqualTo(history + 1);
		AssertIndexSymmetry(store);
		var classHistory = OwnedRows(store, "registry")
			.Where(r => r.Key.Length == 9 && (NativeRegistryKind)r.Key[0] == NativeRegistryKind.Class)
			.Select(r => NativeStateCodec.Registry(r.Value)).ToArray();
		await Assert.That(classHistory.Length).IsEqualTo(2);
		await Assert.That(classHistory.Select(e => e.Identity).Distinct(StringComparer.Ordinal).Count()).IsEqualTo(1);
		await Assert.That(classHistory.Select(e => e.Id).Distinct().Count()).IsEqualTo(2);
		var beforeReopen = Snapshot(store);
		fixture.Reopen(); store = new(fixture, NativeSemanticFixture.Schema);
		await Assert.That(Snapshot(store)).IsEqualTo(beforeReopen);
		AssertIndexSymmetry(store);
	}
	[Test]
	public async Task CanonicalSetsShareClassButExactAuthorsRemainDistinct()
	{
		using var fixture = new NativeSemanticFixture(); var store = new NativeFixtureStore(fixture, NativeSemanticFixture.Schema);
		var first = NativeSemanticFixture.Page() with { Visibility = new("Main", ["b", "a", "a"], ["r"], false, null) };
		await store.CommitAsync(NativeSemanticFixture.Prepare(first, value: 1L));
		var second = NativeSemanticFixture.Page("second") with { Visibility = new("Main", ["a", "b"], ["r", "r"], false, null) };
		await store.CommitAsync(NativeSemanticFixture.Prepare(second, value: 1L));
		await Assert.That(Rows(store, "catalog")).IsEqualTo(1L);
		await store.CommitAsync(NativeSemanticFixture.Prepare(NativeSemanticFixture.Page("third") with { Visibility = second.Visibility with { DraftAuthor = "" } }, value: 1L));
		await Assert.That(Rows(store, "catalog")).IsEqualTo(2L);
	}
	[Test]
	public async Task CatalogByteQuotaRejectsWithoutAllocatingIds()
	{
		using var fixture = new NativeSemanticFixture(); var store = new NativeFixtureStore(fixture, NativeSemanticFixture.Schema);
		var rejected = false;
		for (var i = 0; i < 200; i++)
		{
			var before = Snapshot(store);
			var result = await store.CommitAsync(NativeSemanticFixture.Prepare(NativeSemanticFixture.Page("s" + i, new string('x', 7000) + i), value: 1L));
			if (result is NativeCapacity { Dimension: "catalog-bytes" })
			{
				await Assert.That(Snapshot(store)).IsEqualTo(before); rejected = true; break;
			}
			if (result is not NativeCommitted) throw new InvalidOperationException(result.ToString());
		}
		await Assert.That(rejected).IsTrue();
	}
	[Test]
	public async Task ActualDeltaOverflowRejectsFittingPreparedInput()
	{
		using var fixture = new NativeSemanticFixture(); var store = new NativeFixtureStore(fixture, NativeSemanticFixture.Schema);
		var page = NativeSemanticFixture.Page() with { Body = new string('b', 120000) };
		var facts = Enumerable.Range(0, 32).Select(i => new SemanticFact(page.Reference, "test:p", new PageRef(new string('t', 1700) + i), new("p", "c", "s", "f", false)));
		var prepared = NativePagePreparation.Prepare(NativeSemanticFixture.Schema, page, facts, 0, Guid.NewGuid(), false).Expect<PreparedNativePage>();
		var before = Snapshot(store);
		await Assert.That(await store.CommitAsync(prepared) is NativeCapacity { Dimension: "delta-bytes" }).IsTrue();
		await Assert.That(Snapshot(store)).IsEqualTo(before);
	}
	[Test]
	public async Task ConcurrentLastSlotContendersHaveOneWinner()
	{
		using var fixture = new NativeSemanticFixture(); var store = new NativeFixtureStore(fixture, NativeSemanticFixture.Schema);
		for (var i = 0; i < 1023; i++) await store.CommitAsync(NativeSemanticFixture.Prepare(NativeSemanticFixture.Page("s" + i, "c" + i), value: 1L));
		var contenders = Enumerable.Range(0, 2).Select(i => Task.Run(async () => await store.CommitAsync(NativeSemanticFixture.Prepare(NativeSemanticFixture.Page("contender" + i, "new" + i), value: 1L)))).ToArray();
		var results = await Task.WhenAll(contenders);
		await Assert.That(results.Count(r => r is NativeCommitted)).IsEqualTo(1);
		await Assert.That(results.Count(r => r is NativeCapacity)).IsEqualTo(1);
		await Assert.That(Rows(store, "catalog")).IsEqualTo(1024L);
	}
	[Test]
	public async Task ReadBudgetOwnersAndDecodedLifetimeAreSeparate()
	{
		using var fixture = new NativeSemanticFixture(); var store = new NativeFixtureStore(fixture, NativeSemanticFixture.Schema);
		await store.CommitAsync(NativeSemanticFixture.Prepare(value: 1L));
		var budget = NativeSemanticFixture.Budget(); var lifetime = budget.ReserveWorking(0);
		var page = store.ReadPage(new("subject"), budget, lifetime).Expect<NativeStoredPage>();
		await Assert.That(page.Page.Body).IsEqualTo("body");
		await Assert.That(budget.WorkingBytes > 0).IsTrue();
		lifetime.Dispose(); await Assert.That(budget.WorkingBytes).IsEqualTo(0L);
		var small = NativeSemanticFixture.Budget(probes: 1); using var smallLifetime = small.ReserveWorking(0);
		await Assert.That(() => store.ReadPage(new("subject"), small, smallLifetime)).Throws<SharpMUSH.Library.Plugins.Storage.Lightning.NativeReadLimitException>();
	}
	[Test]
	public async Task HighIncomingDegreeDeletionHasConstantOwnWork()
	{
		using var fixture = new NativeSemanticFixture(); var store = new NativeFixtureStore(fixture, NativeSemanticFixture.Schema);
		await store.CommitAsync(NativeSemanticFixture.Prepare(NativeSemanticFixture.Page("low")));
		await store.CommitAsync(NativeSemanticFixture.Prepare(NativeSemanticFixture.Page("low"), 1, deleted: true));
		var baseline = fixture.LastProbes;
		await store.CommitAsync(NativeSemanticFixture.Prepare(NativeSemanticFixture.Page("target")));
		for (var i = 0; i < 256; i++) await store.CommitAsync(NativeSemanticFixture.Prepare(NativeSemanticFixture.Page("incoming" + i), value: new PageRef("target")));
		var postings = Snapshot(store, "postings"); var forward = Snapshot(store, "forward");
		await store.CommitAsync(NativeSemanticFixture.Prepare(NativeSemanticFixture.Page("target"), 1, deleted: true));
		await Assert.That(fixture.LastProbes).IsEqualTo(baseline);
		await Assert.That(Snapshot(store, "postings")).IsEqualTo(postings);
		await Assert.That(Snapshot(store, "forward")).IsEqualTo(forward);
		Console.WriteLine($"target deletion incoming=256 probes={baseline} nativeBytes={fixture.LastBytes}");
	}
	[Test]
	public async Task DescriptorBoundaryIsExplicitAndNeverTruncated()
	{
		using var fixture = new NativeSemanticFixture(); var store = new NativeFixtureStore(fixture, NativeSemanticFixture.Schema);
		// Find the exact pure descriptor boundary using its checked measuring codec.
		var length = 1;
		for (; length < 8192; length++)
		{
			try { NativeCodec.DescriptorSize(NativeSemanticFixture.Page(category: new string('c', length)).Visibility); }
			catch (InvalidDataException) { break; }
		}
		var accepted = NativeSemanticFixture.Prepare(NativeSemanticFixture.Page(category: new string('c', length - 1)), value: 1L);
		await Assert.That(await store.CommitAsync(accepted) is NativeCommitted).IsTrue();
		var over = NativePagePreparation.Prepare(NativeSemanticFixture.Schema, NativeSemanticFixture.Page(category: new string('c', length)), [], 0, Guid.NewGuid(), false);
		await Assert.That(over is ValidationFailure).IsTrue();
	}

	[Test]
	public async Task CompactDecodedStringsFitConservativeReservation()
	{
		using var fixture = new NativeSemanticFixture(); var store = new NativeFixtureStore(fixture, NativeSemanticFixture.Schema);
		var categories = Enumerable.Range(0, 1000).Select(i => ((char)(0x100 + i)).ToString()).ToImmutableArray();
		var page = NativeSemanticFixture.Page() with { Visibility = new("Main", categories, [], true, null) };
		await store.CommitAsync(NativeSemanticFixture.Prepare(page));
		var table = store.Tables.Single(t => t.Name.EndsWith("summaries", StringComparison.Ordinal));
		var tables = new NativeTables(fixture); var key = OwnedRows(store, "summaries").Single().Key;
		(long Allocated, long Reserved) Measure()
		{
			var budget = NativeSemanticFixture.Budget();
			return fixture.Read(tx =>
			{
				var before = GC.GetAllocatedBytesForCurrentThread();
				using var work = WikiSemanticWork.Create(tx, tables.Projection, budget);
				var decoded = work.Read(table, key, NativeStateCodec.Summary) ?? throw new InvalidOperationException();
				if (decoded.Summary.Visibility.Categories.Length != 1000) throw new InvalidOperationException();
				return (GC.GetAllocatedBytesForCurrentThread() - before, budget.WorkingBytes);
			});
		}
		for (var i = 0; i < 3; i++) Measure();
		var measured = Measure();
		Console.WriteLine($"compactUnicode allocation={measured.Allocated} reservation={measured.Reserved}; cumulativeGC is reported separately from outstanding working");
		await Assert.That(measured.Allocated <= measured.Reserved).IsTrue();
		var low = NativeSemanticFixture.Budget(working: 64);
		await Assert.That(() => fixture.Read(tx => WikiSemanticWork.Create(tx, tables.Projection, low))).Throws<SharpMUSH.Library.Plugins.Storage.Lightning.NativeReadLimitException>();
		await Assert.That(low.WorkingBytes).IsEqualTo(0L);
	}
	[Test]
	public async Task CompactMalformedStringAndContainerLengthsFailBeforeAllocation()
	{
		var state = new NativeSummaryState(new(new("subject"), 1, "title", new("Main", ["a"], [], true, null), "collection", false, 1), 1, 0, 1);
		var original = NativeStateCodec.Summary(state);
		using var stream = new MemoryStream(original); using var reader = new BinaryReader(stream);
		reader.ReadByte(); reader.ReadString(); reader.ReadUInt64(); reader.ReadString(); reader.ReadString();
		var countOffset = checked((int)stream.Position);
		foreach (var count in new[] { -1, int.MaxValue, original.Length + 1 })
		{
			var malformed = original.ToArray(); BinaryPrimitives.WriteInt32LittleEndian(malformed.AsSpan(countOffset), count);
			await Assert.That(() => NativeStateCodec.Summary(malformed)).Throws<InvalidDataException>();
		}
		byte[] oversizedString = [1, 255, 255, 255, 255, 7];
		void RejectOversizedString()
		{
			try { NativeStateCodec.Summary(oversizedString); }
			catch (InvalidDataException) { return; }
			throw new InvalidOperationException("Malformed compact string was accepted.");
		}
		for (var i = 0; i < 3; i++) RejectOversizedString();
		var before = GC.GetAllocatedBytesForCurrentThread();
		RejectOversizedString();
		var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
		await Assert.That(allocated < 8192).IsTrue();
		var ascii = state with { Summary = state.Summary with { Visibility = new("Main", ["a", "b", ""], ["r"], false, "") } };
		var roundtrip = NativeStateCodec.Summary(NativeStateCodec.Summary(ascii));
		await Assert.That(roundtrip.Summary.Visibility.Categories.SequenceEqual(ascii.Summary.Visibility.Categories)).IsTrue();
		await Assert.That(roundtrip.Summary.Visibility.DraftAuthor).IsEqualTo("");
	}
	[Test]
	public async Task ActualWriteMetricsIncludeFullDeltaAndBoundedPlanning()
	{
		using var fixture = new NativeSemanticFixture(); var store = new NativeFixtureStore(fixture, NativeSemanticFixture.Schema);
		await store.CommitAsync(NativeSemanticFixture.Prepare(value: new PageRef("target")));
		await Assert.That(fixture.LastDeltaBytes > 0 && fixture.LastDeltaBytes <= NativeFixtureLimits.MaxDeltaBytes).IsTrue();
		await Assert.That(fixture.LastWorkingBytes > 0 && fixture.LastWorkingBytes <= 4L << 20).IsTrue();
		await Assert.That(fixture.LastProbes > 0 && fixture.LastProbes <= 8192).IsTrue();
		Console.WriteLine($"actual delta={fixture.LastDeltaBytes} working={fixture.LastWorkingBytes} probes={fixture.LastProbes} nativeBytes={fixture.LastBytes} jobMs={fixture.LastJobMilliseconds:F3}");
		AssertIndexSymmetry(store);
	}

	[Test]
	public async Task EveryTypedPostingMatchesIndependentCompleteIndexOracle()
	{
		using var fixture = new NativeSemanticFixture(); var store = new NativeFixtureStore(fixture, NativeSemanticFixture.Schema);
		var page = NativeSemanticFixture.Page(); var evidence = new FactProvenance("pack", "commit", "path", "field", false);
		SemanticFact[] facts = [new(page.Reference, "test:i", long.MinValue, evidence), new(page.Reference, "test:i", long.MaxValue, evidence),
			new(page.Reference, "test:p", new PageRef("target"), evidence), new(page.Reference, "test:k", new Keyword(" CAFÉ "), evidence),
			new(page.Reference, "test:b", false, evidence)];
		var edit = NativePagePreparation.Prepare(NativeSemanticFixture.Schema, page, facts, 0, Guid.NewGuid(), false).Expect<PreparedNativePage>();
		await store.CommitAsync(edit); AssertIndexSymmetry(store);
		await Assert.That(Rows(store, "forward")).IsEqualTo(5L);
		fixture.Reopen(); store = new(fixture, NativeSemanticFixture.Schema); AssertIndexSymmetry(store);
		// Add a validly encoded orphan to prove the complete oracle detects unreachable extra postings.
		var postings = store.Tables.Single(t => t.Name.EndsWith("postings", StringComparison.Ordinal));
		var extra = OwnedRows(store, "postings").First().Key; BinaryPrimitives.WriteUInt64BigEndian(extra.AsSpan(33), 9999);
		await fixture.WriteAsync(tx => { tx.Put(postings, extra, []); return true; });
		await Assert.That(() => AssertIndexSymmetry(store)).Throws<InvalidOperationException>();
	}

}
