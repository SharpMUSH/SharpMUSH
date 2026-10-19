using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using SharpMUSH.Database.Lightning.Semantics;
using SharpMUSH.Library.Models.Wiki.Semantics;
using SharpMUSH.Tools.WikiSemantics.Native;

namespace SharpMUSH.Tests.Wiki.Semantics;

public class WikiSemanticProjectionTests
{
	private static WikiPageProjection Local(string id = "local")
		=> new(new(id), new LocalWikiSource(), id, "game", new("Main", ["category"], [], true, null));
	private static PreparedWikiProjection Prepare(WikiPageProjection page, CanonicalFactSet? facts = null, bool deleted = false)
		=> PreparedWikiProjection.PrepareProjection(NativeSemanticFixture.Schema, page, facts ?? new(1, []), deleted).Expect<PreparedWikiProjection>();
	private static WikiSemanticTables ProjectionTables(NativeTables tables)
		=> new(tables.Metadata, tables.Registry, tables.Summaries, tables.Facts, tables.Forward, tables.Postings, tables.Catalog, tables.Partitions);

	[Test]
	public async Task LocalSourceProjectionHasNoPortableFields()
	{
		var page = Local();
		var prepared = Prepare(page);
		var encoded = WikiSemanticCodec.EncodeProjection(prepared.Page);
		var decoded = WikiSemanticCodec.DecodeProjection(encoded);
		// Independently construct the complete fixed-length-string local envelope. After tag 0,
		// title follows immediately: there is no synthetic portable package, namespace or source key.
		using var expected = new MemoryStream();
		expected.Write([1, 5]);
		void Text(string value)
		{
			var bytes = Encoding.UTF8.GetBytes(value);
			Span<byte> length = stackalloc byte[4];
			BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
			expected.Write(length); expected.Write(bytes);
		}
		Text("local"); expected.WriteByte(0); Text("local"); Text("game"); Text("Main");
		expected.Write([1, 0, 0, 0]); Text("category"); expected.Write([0, 0, 0, 0, 1, 0]);
		await Assert.That(encoded.SequenceEqual(expected.ToArray())).IsTrue();
		await Assert.That(decoded.Reference).IsEqualTo(page.Reference);
		await Assert.That(decoded.Source is LocalWikiSource).IsTrue();
		await Assert.That(decoded.Collection).IsEqualTo("game");
	}

	[Test]
	public async Task BodylessProjectionNeverEncodesBody()
	{
		const string body = "BODY_ONLY_SENTINEL_982e4e";
		const string stamp = "STAMP_ONLY_SENTINEL_a38650";
		var original = NativeSemanticFixture.Page() with { Body = body, Stamp = NativeSemanticFixture.Page().Stamp with { SemanticCommit = stamp } };
		var native = NativeSemanticFixture.Prepare(original);
		var projectionBytes = WikiSemanticCodec.EncodeProjection(native.Projection.Page);
		var oldEnvelope = NativeCodec.EncodePage(native.Page);
		await Assert.That(projectionBytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes(body)) < 0).IsTrue();
		await Assert.That(projectionBytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes(stamp)) < 0).IsTrue();
		await Assert.That(oldEnvelope.AsSpan().IndexOf(Encoding.UTF8.GetBytes(body)) >= 0).IsTrue();
		await Assert.That(oldEnvelope.AsSpan().IndexOf(Encoding.UTF8.GetBytes(stamp)) >= 0).IsTrue();
		await Assert.That(WikiSemanticCodec.DecodeProjection(projectionBytes).Source is PortableWikiSource portable && portable.Key == original.Key).IsTrue();
	}

	[Test]
	public async Task PortableProjectionRoundtripPreservesCompleteSource()
	{
		var key = new PortableKey("pack", "namespace", "source");
		var page = Local() with { Source = new PortableWikiSource(key), Visibility = new("Main", ["a", "b"], ["r"], false, "author") };
		var prepared = Prepare(page);
		var encoded = WikiSemanticCodec.EncodeProjection(prepared.Page);
		var decoded = WikiSemanticCodec.DecodeProjection(encoded);
		await Assert.That(decoded.Source is PortableWikiSource portable && portable.Key == key).IsTrue();
		await Assert.That(decoded.Reference).IsEqualTo(page.Reference);
		await Assert.That(decoded.Title).IsEqualTo(page.Title);
		await Assert.That(decoded.Collection).IsEqualTo(page.Collection);
		await Assert.That(decoded.Visibility.Categories.SequenceEqual(page.Visibility.Categories)).IsTrue();
		await Assert.That(decoded.Visibility.RuleIds.SequenceEqual(page.Visibility.RuleIds)).IsTrue();
		await Assert.That(decoded.Visibility.DraftAuthor).IsEqualTo("author");
		await Assert.That(decoded.Visibility.Published).IsFalse();
		await Assert.That(WikiSemanticCodec.EncodeProjection(decoded).SequenceEqual(encoded)).IsTrue();
	}

	[Test]
	public async Task ProjectionCodecRejectsUnknownSourceFormatKindAndTrailingData()
	{
		var page = Local();
		var bytes = WikiSemanticCodec.EncodeProjection(Prepare(page).Page);
		var sourceTag = 2 + 4 + Encoding.UTF8.GetByteCount(page.Reference.Id);
		foreach (var offset in new[] { 0, 1, sourceTag })
		{
			var malformed = bytes.ToArray(); malformed[offset] = 255;
			await Assert.That(() => WikiSemanticCodec.DecodeProjection(malformed)).Throws<InvalidDataException>();
		}
		await Assert.That(() => WikiSemanticCodec.DecodeProjection([.. bytes, 0])).Throws<InvalidDataException>();
	}

	[Test]
	public async Task FrozenInputCollectionsCannotChangeProjectionCertificate()
	{
		var allowed = new[] { new Keyword("allowed") };
		var definitions = new[] { new PropertyDefinition("test:k", 1, SemanticType.Keyword, Cardinality.Multiple, ImmutableCollectionsMarshal.AsImmutableArray(allowed)) };
		var schema = new SemanticSchema(1, ImmutableCollectionsMarshal.AsImmutableArray(definitions));
		var categories = new[] { "category" }; var rules = new[] { "rule" };
		var page = Local() with { Visibility = new("Main", ImmutableCollectionsMarshal.AsImmutableArray(categories), ImmutableCollectionsMarshal.AsImmutableArray(rules), true, null) };
		var entries = new[] { new SemanticFact(page.Reference, "test:k", new Keyword("allowed"), new("pack", "commit", "path", "field", false)) };
		var prepared = PreparedWikiProjection.PrepareProjection(schema, page, new(1, ImmutableCollectionsMarshal.AsImmutableArray(entries)), false).Expect<PreparedWikiProjection>();
		var encoded = WikiSemanticCodec.EncodeProjection(prepared.Page);
		var encodedFacts = WikiSemanticCodec.EncodeFacts(prepared.Facts);
		var digest = prepared.RequestDigest;
		allowed[0] = new("changed"); definitions[0] = definitions[0] with { Type = SemanticType.Integer };
		categories[0] = "changed"; rules[0] = "changed"; entries[0] = entries[0] with { Value = new Keyword("changed") };
		var cleanSchema = new SemanticSchema(1, [new("test:k", 1, SemanticType.Keyword, Cardinality.Multiple, [new("allowed")])]);
		var cleanPage = Local() with { Visibility = new("Main", ["category"], ["rule"], true, null) };
		var cleanFacts = new CanonicalFactSet(1, [new(cleanPage.Reference, "test:k", new Keyword("allowed"), new("pack", "commit", "path", "field", false))]);
		var clean = PreparedWikiProjection.PrepareProjection(cleanSchema, cleanPage, cleanFacts, false).Expect<PreparedWikiProjection>();
		await Assert.That(prepared.RequestDigest).IsEqualTo(digest);
		await Assert.That(prepared.RequestDigest).IsEqualTo(clean.RequestDigest);
		await Assert.That(prepared.SchemaIdentityDigest).IsEqualTo(clean.SchemaIdentityDigest);
		await Assert.That(WikiSemanticCodec.EncodeProjection(prepared.Page).SequenceEqual(encoded)).IsTrue();
		await Assert.That(WikiSemanticCodec.EncodeFacts(prepared.Facts).SequenceEqual(encodedFacts)).IsTrue();
	}

	[Test]
	public async Task DeletedProjectionRejectsFactsAndHasDistinctDigest()
	{
		var page = Local();
		var facts = new CanonicalFactSet(1, [new(page.Reference, "test:i", 1L, new("pack", "commit", "path", "field", false))]);
		await Assert.That(PreparedWikiProjection.PrepareProjection(NativeSemanticFixture.Schema, page, facts, true) is ValidationFailure).IsTrue();
		await Assert.That(Prepare(page).RequestDigest != Prepare(page, deleted: true).RequestDigest).IsTrue();
	}

	[Test]
	public async Task PlannerContributesOnlyProjectionRowsWithoutApplying()
	{
		using var fixture = new NativeSemanticFixture(); var tables = new NativeTables(fixture);
		var native = NativeSemanticFixture.Prepare(value: 1L);
		var planned = fixture.Read(tx =>
		{
			var budget = NativeSemanticFixture.Budget(); using var lifetime = budget.ReserveWorking(0);
			var delta = new WikiSemanticPlannedDelta(lifetime, default);
			using var work = WikiSemanticWork.Create(tx, tables.Projection, budget, lifetime, delta);
			var result = WikiSemanticProjectionPlanner.PlanProjection(work, native.Projection, 0, new AllocateNextSequence()).Expect<WikiProjectionPlanned>();
			return (Result: result, Bytes: delta.Bytes,
				Page: delta.Lookup(tables.Pages, WikiSemanticKeys.Subject(result.NativeId)).Kind,
				Receipt: delta.Lookup(tables.Mutations, native.MutationId.ToByteArray()).Kind);
		});
		var committedSequence = fixture.Read(tx =>
		{
			using var work = WikiSemanticWork.Create(tx, tables.Projection, NativeSemanticFixture.Budget());
			return work.Counter(work.Key(2));
		});
		await Assert.That(planned.Result.StateVersion).IsEqualTo(1L);
		await Assert.That(planned.Result.CommitSequence).IsEqualTo(1L);
		await Assert.That(planned.Bytes > 0 && planned.Bytes <= WikiSemanticLimits.MaxDeltaBytes).IsTrue();
		await Assert.That(planned.Page).IsEqualTo(WikiSemanticPlannedKind.Absent);
		await Assert.That(planned.Receipt).IsEqualTo(WikiSemanticPlannedKind.Absent);
		await Assert.That(committedSequence).IsEqualTo(0UL);
	}

	[Test]
	public async Task RegistryCachesFollowOverlayDeletionRecreationAndCounterWrites()
	{
		using var fixture = new NativeSemanticFixture(); var tables = new NativeTables(fixture);
		var observed = fixture.Read(tx =>
		{
			using var work = WikiSemanticWork.Create(tx, tables.Projection, NativeSemanticFixture.Budget());
			var first = work.Registry(WikiSemanticRegistryKind.Page, "first");
			work.Delta.Put(tables.Metadata, work.Key(21), work.Number(42));
			var second = work.Registry(WikiSemanticRegistryKind.Page, "second");
			work.Delta.Delete(tables.Registry, work.Lookup(WikiSemanticRegistryKind.Page, "first"));
			var removed = work.Registry(WikiSemanticRegistryKind.Page, "first", false);
			var recreated = work.Registry(WikiSemanticRegistryKind.Page, "first");
			return (First: first, Second: second, Removed: removed, Recreated: recreated);
		});
		await Assert.That(observed.First).IsEqualTo(1UL);
		await Assert.That(observed.Second).IsEqualTo(42UL);
		await Assert.That(observed.Removed).IsEqualTo(0UL);
		await Assert.That(observed.Recreated).IsEqualTo(43UL);
	}

	[Test]
	public async Task PlannedLookupOwnsInputAndReturnedArrays()
	{
		using var fixture = new NativeSemanticFixture(); var tables = new NativeTables(fixture);
		var observed = fixture.Read(tx =>
		{
			using var work = WikiSemanticWork.Create(tx, tables.Projection, NativeSemanticFixture.Budget());
			byte[] key = [99]; byte[] value = [7];
			work.Delta.Put(tables.Metadata, key, value);
			key[0] = 100; value[0] = 8;
			var first = work.Delta.Lookup(tables.Metadata, [99]);
			var original = first.Value?[0];
			if (first.Value is { } owned) owned[0] = 9;
			var second = work.Delta.Lookup(tables.Metadata, [99]);
			work.Delta.Delete(tables.Metadata, [99]);
			return (Kind: first.Kind, Original: original, Owned: second.Value?[0],
				Deleted: work.Delta.Lookup(tables.Metadata, [99]).Kind, Absent: work.Delta.Lookup(tables.Metadata, [100]).Kind);
		});
		await Assert.That(observed.Kind).IsEqualTo(WikiSemanticPlannedKind.Put);
		await Assert.That(observed.Original).IsEqualTo((byte?)7);
		await Assert.That(observed.Owned).IsEqualTo((byte?)7);
		await Assert.That(observed.Deleted).IsEqualTo(WikiSemanticPlannedKind.Delete);
		await Assert.That(observed.Absent).IsEqualTo(WikiSemanticPlannedKind.Absent);
	}

	[Test]
	public async Task LocalContributionsShareOneBootstrapSequenceAndNextEditAdvancesOnce()
	{
		using var fixture = new NativeSemanticFixture(); var tables = new NativeTables(fixture); var roles = ProjectionTables(tables);
		var pages = new[] { Prepare(Local("Home")), Prepare(Local("MarkdownGuide")), Prepare(Local("NameCategories")) };
		var observed = await fixture.WriteAsync(tx =>
		{
			var budget = NativeSemanticFixture.Budget(); using var lifetime = budget.ReserveWorking(0);
			var delta = new WikiSemanticPlannedDelta(lifetime, default);
			using var work = WikiSemanticWork.Create(tx, roles, budget, lifetime, delta);
			var sequence = ExistingBootstrapSequence.AllocateForActivation(work);
			var seeded = pages.Select(page => WikiSemanticProjectionPlanner.PlanProjection(work, page, 0, sequence).Expect<WikiProjectionPlanned>()).ToArray();
			var states = seeded.Select(seed => work.Read(roles.Summaries, WikiSemanticKeys.Subject(seed.NativeId), WikiSemanticStateCodec.ProjectionState) ?? throw new InvalidOperationException()).ToArray();
			var next = WikiSemanticProjectionPlanner.PlanProjection(work, pages[0], 1, new AllocateNextSequence()).Expect<WikiProjectionPlanned>();
			var bytes = delta.Bytes;
			delta.Apply(tx, default);
			return (Seeded: seeded, States: states, Next: next, Bytes: bytes);
		});
		await Assert.That(observed.Seeded.Select(seed => seed.NativeId).Distinct().Count()).IsEqualTo(3);
		await Assert.That(observed.Seeded.All(seed => seed.StateVersion == 1 && seed.CommitSequence == 1)).IsTrue();
		await Assert.That(observed.States.All(state => state.Source is LocalWikiSource && state.State.Summary.StateVersion == 1 && state.State.CommitSequence == 1)).IsTrue();
		await Assert.That(observed.Next.CommitSequence).IsEqualTo(2L);
		await Assert.That(observed.Next.StateVersion).IsEqualTo(2L);
		await Assert.That(observed.Bytes <= WikiSemanticLimits.MaxDeltaBytes).IsTrue();
		fixture.Reopen();
		// Reopen replaces the store instance, so explicitly register the retained fixture tables again.
		roles = ProjectionTables(new NativeTables(fixture));
		var reopened = fixture.Read(tx =>
		{
			using var work = WikiSemanticWork.Create(tx, roles, NativeSemanticFixture.Budget());
			return observed.Seeded.Skip(1).Select(seed => work.Read(roles.Summaries, WikiSemanticKeys.Subject(seed.NativeId), WikiSemanticStateCodec.ProjectionState) ?? throw new InvalidOperationException()).ToArray();
		});
		await Assert.That(reopened.Select(WikiSemanticStateCodec.ProjectionState).Select(Convert.ToHexString)
			.SequenceEqual(observed.States.Skip(1).Select(WikiSemanticStateCodec.ProjectionState).Select(Convert.ToHexString))).IsTrue();
	}

	[Test]
	public async Task FactContributionsSharePendingCatalogAndAdvanceOnePartitionTwice()
	{
		using var fixture = new NativeSemanticFixture(); var tables = new NativeTables(fixture); var roles = ProjectionTables(tables);
		PreparedWikiProjection FactPage(string id)
		{
			var page = Local(id);
			return Prepare(page, new(1, [new(page.Reference, "test:i", 1L, new("pack", "commit", "path", "field", false))]));
		}
		var first = FactPage("first"); var second = FactPage("second");
		var observed = fixture.Read(tx =>
		{
			var budget = NativeSemanticFixture.Budget(); using var lifetime = budget.ReserveWorking(0);
			var delta = new WikiSemanticPlannedDelta(lifetime, default);
			using var work = WikiSemanticWork.Create(tx, roles, budget, lifetime, delta);
			var firstResult = WikiSemanticProjectionPlanner.PlanProjection(work, first, 0, new AllocateNextSequence()).Expect<WikiProjectionPlanned>();
			var secondResult = WikiSemanticProjectionPlanner.PlanProjection(work, second, 0, new AllocateNextSequence()).Expect<WikiProjectionPlanned>();
			var firstState = work.Read(roles.Summaries, WikiSemanticKeys.Subject(firstResult.NativeId), WikiSemanticStateCodec.ProjectionState) ?? throw new InvalidOperationException();
			var secondState = work.Read(roles.Summaries, WikiSemanticKeys.Subject(secondResult.NativeId), WikiSemanticStateCodec.ProjectionState) ?? throw new InvalidOperationException();
			var key = work.Pair(firstState.State.CollectionId, firstState.State.ClassId);
			var catalog = work.Read(roles.Catalog, key, WikiSemanticStateCodec.Catalog) ?? throw new InvalidOperationException();
			var partition = work.ReadBytes(roles.Partitions, key) ?? throw new InvalidOperationException();
			return (FirstClass: firstState.State.ClassId, SecondClass: secondState.State.ClassId,
				References: catalog.References, Count: work.Counter(work.Key(30, firstState.State.CollectionId)),
				Partition: BinaryPrimitives.ReadUInt64BigEndian(partition), Sequence: work.Counter(work.Key(2)));
		});
		await Assert.That(observed.FirstClass > 0 && observed.FirstClass == observed.SecondClass).IsTrue();
		await Assert.That(observed.References).IsEqualTo(2L);
		await Assert.That(observed.Count).IsEqualTo(1UL);
		await Assert.That(observed.Partition).IsEqualTo(2UL);
		await Assert.That(observed.Sequence).IsEqualTo(2UL);
	}

	[Test]
	public async Task DeletingFactBearingSourceRemovesOutgoingAndReclaimsClassWithoutResurrection()
	{
		using var fixture = new NativeSemanticFixture(); var store = new NativeFixtureStore(fixture, NativeSemanticFixture.Schema);
		await Assert.That(await store.CommitAsync(NativeSemanticFixture.Prepare(value: new PageRef("target"))) is NativeCommitted).IsTrue();
		await Assert.That(NativeSemanticCommitTests.Snapshot(store, "forward").Length > 0).IsTrue();
		await Assert.That(NativeSemanticCommitTests.Snapshot(store, "postings").Length > 0).IsTrue();
		await Assert.That(NativeSemanticCommitTests.Snapshot(store, "catalog").Length > 0).IsTrue();
		await Assert.That(await store.CommitAsync(NativeSemanticFixture.Prepare(version: 1, deleted: true)) is NativeCommitted { StateVersion: 2 }).IsTrue();
		await Assert.That(NativeSemanticCommitTests.Snapshot(store, "forward")).IsEqualTo("");
		await Assert.That(NativeSemanticCommitTests.Snapshot(store, "postings")).IsEqualTo("");
		await Assert.That(NativeSemanticCommitTests.Snapshot(store, "catalog")).IsEqualTo("");
		var budget = NativeSemanticFixture.Budget(); using var lifetime = budget.ReserveWorking(0);
		var stored = store.ReadPage(new("subject"), budget, lifetime).Expect<NativeStoredPage>();
		await Assert.That(stored.Summary.Deleted).IsTrue();
		await Assert.That(stored.Facts.Facts.IsEmpty).IsTrue();
		var tombstone = NativeSemanticCommitTests.Snapshot(store);
		await Assert.That(await store.CommitAsync(NativeSemanticFixture.Prepare(version: 2, value: 1L)) is NativeConflict { CurrentStateVersion: 2 }).IsTrue();
		await Assert.That(NativeSemanticCommitTests.Snapshot(store)).IsEqualTo(tombstone);
		fixture.Reopen(); store = new(fixture, NativeSemanticFixture.Schema);
		await Assert.That(NativeSemanticCommitTests.Snapshot(store)).IsEqualTo(tombstone);
	}
}
