using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Tools.WikiSemantics;
using SharpMUSH.Tools.WikiSemantics.Native;

namespace SharpMUSH.Tests.Wiki.Semantics;

public class NativeSemanticCorpusTests
{
	private sealed record Row(byte[] Key, byte[] Value);
	private sealed record Receipt(Guid Id, string Digest, long Sequence);
	// This is an explicitly separate, conservative offline allocation ledger, not a query budget.
	// It includes temporary encode/decode overlap and never releases borrowed native accounting.
	private sealed class OfflineEvidence
	{
		internal long ReservedBytes { get; private set; }
		internal void Reserve(long bytes) => ReservedBytes = checked(ReservedBytes + bytes);
		internal byte[] Copy(byte[] bytes)
		{
			Reserve(checked(64L + bytes.Length));
			return bytes.ToArray();
		}
		internal PageSnapshot Own(PageSnapshot page)
		{
			// Bounded measurement precedes the offline encode/decode allocation reservation.
			Reserve(checked(2048L + NativeCodec.MeasurePage(page) * 12L));
			return NativeCodec.DecodePage(NativeCodec.EncodePage(page));
		}
		internal CanonicalFactSet Own(CanonicalFactSet facts)
		{
			Reserve(checked(2048L + NativeCodec.MeasureFacts(facts, NativeFixtureLimits.MaxReadBytes) * 12L));
			return NativeCodec.DecodeFacts(NativeCodec.EncodeFacts(facts));
		}
	}
	private sealed class Measurements
	{
		internal long MaxDelta, MaxWriterWorkingSample, MaxWriterBytes, MaxReadBytes, MaxReadWorkingSample;
		internal int MaxWriterProbes, MaxReadProbes, MaxBatchRows, MaxBatchBytes;
		internal double MaxWriterMilliseconds, MaxCommitMilliseconds, MaxReadMilliseconds;
		internal readonly List<double> Commits = [], Jobs = [], Reads = [];
		internal void Read(NativeReadBudget budget, long started)
		{
			MaxReadProbes = Math.Max(MaxReadProbes, budget.Probes);
			MaxReadBytes = Math.Max(MaxReadBytes, budget.Bytes);
			MaxReadWorkingSample = Math.Max(MaxReadWorkingSample, budget.WorkingBytes);
			var ms = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
			MaxReadMilliseconds = Math.Max(MaxReadMilliseconds, ms); Reads.Add(ms);
		}
		internal void Commit(NativeSemanticFixture fixture, double elapsed)
		{
			MaxDelta = Math.Max(MaxDelta, fixture.LastDeltaBytes);
			MaxWriterProbes = Math.Max(MaxWriterProbes, fixture.LastProbes);
			MaxWriterBytes = Math.Max(MaxWriterBytes, fixture.LastBytes);
			MaxWriterWorkingSample = Math.Max(MaxWriterWorkingSample, fixture.LastWorkingBytes);
			MaxWriterMilliseconds = Math.Max(MaxWriterMilliseconds, fixture.LastJobMilliseconds);
			MaxCommitMilliseconds = Math.Max(MaxCommitMilliseconds, elapsed);
			Commits.Add(elapsed); Jobs.Add(fixture.LastJobMilliseconds);
		}
	}
	private static void Require(bool condition, string detail)
	{
		if (!condition) throw new InvalidOperationException(detail);
	}
	private static PageSnapshot CanonicalPage(PageSnapshot page) => page with
	{
		Visibility = page.Visibility with
		{
			Categories = [.. page.Visibility.Categories.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)],
			RuleIds = [.. page.Visibility.RuleIds.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)]
		}
	};
	private static void SamePage(PageSnapshot expected, PageSnapshot actual)
	{
		Require(expected.Reference == actual.Reference && expected.Key == actual.Key && expected.Title == actual.Title
			&& expected.Body == actual.Body && expected.Collections.SequenceEqual(actual.Collections)
			&& expected.Visibility.Namespace == actual.Visibility.Namespace
			&& expected.Visibility.Categories.SequenceEqual(actual.Visibility.Categories)
			&& expected.Visibility.RuleIds.SequenceEqual(actual.Visibility.RuleIds)
			&& expected.Visibility.Published == actual.Visibility.Published
			&& expected.Visibility.DraftAuthor == actual.Visibility.DraftAuthor && expected.Stamp == actual.Stamp,
			"Full page/source stamp differs: " + expected.Key);
	}
	private static void SameFacts(CanonicalFactSet expected, CanonicalFactSet actual)
		=> Require(expected.SchemaVersion == actual.SchemaVersion && expected.Facts.SequenceEqual(actual.Facts), "Typed canonical facts/provenance differ.");

	[Test]
	[Explicit]
	public async Task VerifiedFullCorpusRoundTripsThroughNativeStore()
	{
		var source = Environment.GetEnvironmentVariable("SEMANTIC_PF2E_SOURCE")
			?? throw new InvalidOperationException("Set SEMANTIC_PF2E_SOURCE; missing source FAILS required native corpus acceptance.");
		var allocatedBefore = GC.GetTotalAllocatedBytes(true);
		var totalStarted = Stopwatch.GetTimestamp();
		var dataset = PinnedDataset.Load(source).Expect<VerifiedDataset>();
		var extraction = Pf2eExtractor.Extract(dataset).Expect<ExtractionSuccess>();
		var expected = extraction.Snapshot;
		Coverage(expected, extraction.Unknowns);
		var offline = new OfflineEvidence();
		// Conservative verified-source/expected graph/container allowance, separately reported offline.
		offline.Reserve(checked(4560161L * 24 + expected.Facts.Facts.Length * 2048L + expected.Pages.Length * 1024L));
		var metrics = new Measurements();
		var receipts = new Dictionary<PageRef, Receipt>();
		var bySubject = expected.Facts.Facts.ToLookup(f => f.Subject);
		var importStarted = Stopwatch.GetTimestamp();
		using (var fixture = new NativeSemanticFixture())
		{
			var store = new NativeFixtureStore(fixture, expected.Schema);
			long sequence = 0;
			foreach (var page in expected.Pages)
			{
				var mutation = Guid.NewGuid();
				var prepared = NativePagePreparation.Prepare(expected.Schema, page, bySubject[page.Reference], 0, mutation, false)
					.Expect<PreparedNativePage>();
				var started = Stopwatch.GetTimestamp();
				var committed = (await store.CommitAsync(prepared)).Expect<NativeCommitted>();
				metrics.Commit(fixture, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
				Require(committed.MutationId == mutation && committed.StateVersion == 1 && committed.CommitSequence == ++sequence,
					"Durable insert receipt differs: " + page.Key);
				offline.Reserve(512);
				receipts.Add(page.Reference, new(mutation, new string(prepared.RequestDigest.AsSpan()), committed.CommitSequence));
			}
			Console.WriteLine($"NATIVE_CORPUS importPages={sequence} importMs={Stopwatch.GetElapsedTime(importStarted).TotalMilliseconds:F3} sync=Full mapBytes={256L << 20}");
			string? priorDigest = null;
			for (var reopen = 1; reopen <= 2; reopen++)
			{
				var started = Stopwatch.GetTimestamp();
				fixture.Reopen();
				store = new(fixture, expected.Schema);
				Console.WriteLine($"NATIVE_CORPUS reopen={reopen} reopenMs={Stopwatch.GetElapsedTime(started).TotalMilliseconds:F3}");
				var actualPages = ImmutableArray.CreateBuilder<PageSnapshot>(expected.Pages.Length);
				var actualFacts = ImmutableArray.CreateBuilder<SemanticFact>(expected.Facts.Facts.Length);
				offline.Reserve(checked(expected.Pages.Length * 64L + expected.Facts.Facts.Length * 64L));
				foreach (var page in expected.Pages)
				{
					var receipt = receipts[page.Reference];
					var budget = NativeSemanticFixture.Budget();
					using (var decoded = budget.ReserveWorking(256))
					{
						var readStarted = Stopwatch.GetTimestamp();
						var borrowed = store.ReadPage(page.Reference, budget, decoded).Expect<NativeStoredPage>();
						metrics.Read(budget, readStarted);
						SamePage(CanonicalPage(page), borrowed.Page);
						SameFacts(new(expected.Schema.Version, [.. bySubject[page.Reference]]), borrowed.Facts);
						var summary = borrowed.Summary;
						Require(summary.Reference == page.Reference && summary.NativeId > 0 && summary.Title == page.Title
							&& summary.Collection == page.Collections.Single() && !summary.Deleted && summary.StateVersion == 1
							&& borrowed.CommitSequence == receipt.Sequence, "Summary/version differs: " + page.Key);
						SameVisibility(borrowed.Page.Visibility, summary.Visibility);
						// Serialization creates entirely owned strings, arrays and records before decoded is released.
						actualPages.Add(offline.Own(borrowed.Page));
						actualFacts.AddRange(offline.Own(borrowed.Facts).Facts);
					}
					Require(budget.WorkingBytes == 0, "Page decoded ownership leaked.");
					budget = NativeSemanticFixture.Budget();
					using (var decoded = budget.ReserveWorking(256))
					{
						var readStarted = Stopwatch.GetTimestamp();
						var status = store.GetMutation(receipt.Id, budget, decoded).Expect<NativeMutationStatus>();
						metrics.Read(budget, readStarted);
						Require(status.MutationId == receipt.Id && status.RequestDigest == receipt.Digest
							&& status.Committed == new NativeCommitted(receipt.Id, 1, receipt.Sequence), "Reopened receipt differs.");
					}
					Require(budget.WorkingBytes == 0, "Receipt decoded ownership leaked.");
				}
				var reopened = new SemanticSnapshot(expected.Schema, actualPages.MoveToImmutable(), new(expected.Schema.Version, actualFacts.MoveToImmutable()), expected.Stamp);
				Coverage(reopened, extraction.Unknowns);
				Goldens(dataset, reopened);
				var tables = ReadAll(store, offline, metrics);
				Symmetry(expected, tables, receipts, offline);
				var digest = Digest(tables);
				Require(priorDigest is null || priorDigest == digest, "Second reopen changed complete table digest.");
				priorDigest = digest;
				Console.WriteLine($"NATIVE_CORPUS reopen={reopen} tablesSha256={digest} logicalKeyValueBytes={tables.Values.Sum(rows => rows.Sum(r => (long)r.Key.Length + r.Value.Length))}");
				if (reopen == 2) await Storage(fixture.Path);
			}
		}
		Require(PinnedDataset.Load(source) is VerifiedDataset, "Immutable source revalidation failed after native acceptance.");
		Console.WriteLine($"NATIVE_CORPUS cleanup=disposed-owned-world sourceReverified=true totalMs={Stopwatch.GetElapsedTime(totalStarted).TotalMilliseconds:F3} offlineConservativeCumulativeReservationBytes={offline.ReservedBytes} managedCumulativeAllocatedBytes={GC.GetTotalAllocatedBytes(true) - allocatedBefore} processWorkingSetCheckpointBytes={Environment.WorkingSet}");
		Console.WriteLine($"NATIVE_CORPUS maxActualDeltaBytes={metrics.MaxDelta} writerMaxProbes={metrics.MaxWriterProbes} writerMaxInspectedBytes={metrics.MaxWriterBytes} writerMaxWorkingCheckpointBytes={metrics.MaxWriterWorkingSample} readMaxProbes={metrics.MaxReadProbes} readMaxInspectedBytes={metrics.MaxReadBytes} readMaxWorkingCheckpointBytes={metrics.MaxReadWorkingSample} maxBatchRows={metrics.MaxBatchRows} maxBatchKeyValueBytes={metrics.MaxBatchBytes}");
		Console.WriteLine($"NATIVE_CORPUS commitMs={Distribution(metrics.Commits)} synchronousDelegateMs={Distribution(metrics.Jobs)} synchronousReadCallMs={Distribution(metrics.Reads)} workingSamplesAreNotExactPeaks=true nativeQueryExecution=false trustedAuthorization=false");
	}

	private static void SameVisibility(VisibilityDescriptor a, VisibilityDescriptor b)
		=> Require(a.Namespace == b.Namespace && a.Categories.SequenceEqual(b.Categories) && a.RuleIds.SequenceEqual(b.RuleIds)
			&& a.Published == b.Published && a.DraftAuthor == b.DraftAuthor, "Visibility descriptor differs.");
	private static Dictionary<string, List<Row>> ReadAll(NativeFixtureStore store, OfflineEvidence offline, Measurements metrics)
	{
		var tables = new Dictionary<string, List<Row>>(StringComparer.Ordinal);
		foreach (var table in store.Tables)
		{
			var rows = new List<Row>(); byte[] after = [];
			while (true)
			{
				var budget = NativeSemanticFixture.Budget();
				bool exhausted;
				using (var inputs = budget.ReserveWorking(checked(128L + after.Length)))
				{
					var started = Stopwatch.GetTimestamp();
					using var batch = store.Inspect(table, after, budget);
					metrics.Read(budget, started);
					metrics.MaxBatchRows = Math.Max(metrics.MaxBatchRows, batch.Entries.Length);
					var size = batch.Entries.Sum(r => r.Key.Length + r.Value.Length);
					metrics.MaxBatchBytes = Math.Max(metrics.MaxBatchBytes, size);
					Require(batch.Entries.Length <= 128 && size <= 128 * 1024, "Native batch cap exceeded.");
					foreach (var row in batch.Entries)
					{
						Require(after.Length == 0 || row.Key.AsSpan().SequenceCompareTo(after) > 0, "Inspection did not strictly resume.");
						offline.Reserve(256);
						rows.Add(new(offline.Copy(row.Key), offline.Copy(row.Value)));
						after = rows[^1].Key; // separately owned evidence, not the borrowed batch key
					}
					exhausted = batch.Exhausted;
					Require(exhausted || batch.Entries.Length > 0, "Nonexhausted empty inspection batch.");
				}
				Require(budget.WorkingBytes == 0, "Inspection owners leaked.");
				if (exhausted) break;
			}
			var suffix = table.Name[NativeTables.Prefix.Length..];
			tables.Add(suffix, rows);
			Console.WriteLine($"NATIVE_TABLE name={suffix} rows={rows.Count} keyValueBytes={rows.Sum(r => (long)r.Key.Length + r.Value.Length)} maxRowKeyValueBytes={rows.Select(r => r.Key.Length + r.Value.Length).DefaultIfEmpty().Max()}");
		}
		return tables;
	}
	private static ulong Number(byte[] bytes)
	{
		Require(bytes.Length == 8, "Malformed UInt64BE.");
		return BinaryPrimitives.ReadUInt64BigEndian(bytes);
	}
	private static byte[] Id(ulong id)
	{
		Require(id > 0, "Zero registry ID.");
		var bytes = new byte[8]; BinaryPrimitives.WriteUInt64BigEndian(bytes, id); return bytes;
	}
	private static string Hex(byte[] bytes) => Convert.ToHexString(bytes);
	private static byte[] Descriptor(VisibilityDescriptor descriptor)
	{
		using var stream = new MemoryStream();
		using (var writer = new BinaryWriter(stream, new UTF8Encoding(false, true), true))
		{
			writer.Write((byte)1); writer.Write(descriptor.Namespace);
			writer.Write(descriptor.Categories.Length); foreach (var value in descriptor.Categories) writer.Write(value);
			writer.Write(descriptor.RuleIds.Length); foreach (var value in descriptor.RuleIds) writer.Write(value);
			writer.Write(descriptor.Published); writer.Write(descriptor.DraftAuthor is not null);
			if (descriptor.DraftAuthor is not null) writer.Write(descriptor.DraftAuthor);
		}
		return stream.ToArray();
	}
	private static void Symmetry(SemanticSnapshot expected, Dictionary<string, List<Row>> tables, Dictionary<PageRef, Receipt> receipts, OfflineEvidence offline)
	{
		// Entire independent reconstruction lives outside native/decoded leases. Reserve containers and
		// worst-case decoder/string/key overlap before constructing the offline oracle structures.
		offline.Reserve(checked(tables.Values.Sum(rows => rows.Sum(r => 4096L + (r.Key.Length + r.Value.Length) * 32L))));
		var ids = new Dictionary<(NativeRegistryKind Kind, string Identity), ulong>();
		var reverse = new Dictionary<(NativeRegistryKind Kind, ulong Id), NativeRegistryEntry>();
		var lookup = new Dictionary<string, Row>(StringComparer.Ordinal);
		foreach (var row in tables["registry"])
		{
			Require(row.Key.Length is 9 or 33, "Malformed registry key length.");
			var kind = (NativeRegistryKind)row.Key[0]; Require(Enum.IsDefined(kind), "Unknown registry kind.");
			var value = NativeStateCodec.Registry(row.Value); Require(value.Id > 0, "Invalid registry value ID.");
			if (row.Key.Length == 9)
			{
				Require(Number(row.Key[1..]) == value.Id, "Registry reverse key/value ID mismatch.");
				reverse.Add((kind, value.Id), value); ids.Add((kind, value.Identity), value.Id);
			}
			else
			{
				var hash = SHA256.HashData(new UTF8Encoding(false, true).GetBytes(value.Identity));
				Require(row.Key.AsSpan(1).SequenceEqual(hash), "Lookup hash differs from exact stored identity.");
				lookup.Add(Hex(row.Key), row);
			}
		}
		Require(lookup.Count == reverse.Count, "Fresh corpus has unexplained reverse-only registry rows.");
		foreach (var ((kind, id), entry) in reverse)
		{
			var key = new byte[33]; key[0] = (byte)kind;
			SHA256.HashData(Encoding.UTF8.GetBytes(entry.Identity)).CopyTo(key, 1);
			Require(lookup.TryGetValue(Hex(key), out var row) && NativeStateCodec.Registry(row.Value) == entry,
				"Missing or disagreeing exact registry lookup/reverse pair: " + kind + "/" + id);
		}
		ulong Resolve(NativeRegistryKind kind, string identity) => ids.TryGetValue((kind, identity), out var id) ? id : throw new InvalidOperationException("Missing registry identity: " + kind + "/" + identity);
		var collection = Resolve(NativeRegistryKind.Collection, Pf2eExtractor.Collection);
		var expectedIdentities = new HashSet<(NativeRegistryKind, string)>();
		foreach (var page in expected.Pages) expectedIdentities.Add((NativeRegistryKind.Page, page.Reference.Id));
		foreach (var property in expected.Schema.Properties)
		{
			expectedIdentities.Add((NativeRegistryKind.Property, property.Id));
			foreach (var keyword in property.AllowedKeywords) expectedIdentities.Add((NativeRegistryKind.Keyword, keyword.Value));
		}
		expectedIdentities.Add((NativeRegistryKind.Collection, Pf2eExtractor.Collection));
		foreach (var fact in expected.Facts.Facts)
		{
			if (fact.Value is PageRef target) expectedIdentities.Add((NativeRegistryKind.Page, target.Id));
			if (fact.Value is Keyword keyword) expectedIdentities.Add((NativeRegistryKind.Keyword, keyword.Value));
		}
		var factsBySubject = expected.Facts.Facts.ToLookup(f => f.Subject);
		var classMembers = new Dictionary<ulong, (byte[] Descriptor, long Members)>();
		var expectedForward = new HashSet<string>(StringComparer.Ordinal);
		var expectedPostings = new HashSet<string>(StringComparer.Ordinal);
		var expectedSubjectKeys = new HashSet<string>(StringComparer.Ordinal);
		var summaries = tables["summaries"].ToDictionary(r => Hex(r.Key), r => NativeStateCodec.Summary(r.Value));
		var pages = tables["pages"].ToDictionary(r => Hex(r.Key), r => r);
		var facts = tables["facts"].ToDictionary(r => Hex(r.Key), r => r);
		foreach (var page in expected.Pages)
		{
			var id = Resolve(NativeRegistryKind.Page, page.Reference.Id); var subjectKey = Hex(Id(id));
			expectedSubjectKeys.Add(subjectKey);
			Require(pages.ContainsKey(subjectKey) && facts.ContainsKey(subjectKey) && summaries.ContainsKey(subjectKey), "Missing aggregate row.");
			SamePage(CanonicalPage(page), NativeCodec.DecodePage(pages[subjectKey].Value));
			SameFacts(new(expected.Schema.Version, [.. factsBySubject[page.Reference]]), NativeCodec.DecodeFacts(facts[subjectKey].Value));
			ulong classId = 0;
			if (factsBySubject[page.Reference].Any())
			{
				var descriptor = Descriptor(CanonicalPage(page).Visibility);
				var identity = collection.ToString("X16", CultureInfo.InvariantCulture) + Convert.ToBase64String(descriptor);
				expectedIdentities.Add((NativeRegistryKind.Class, identity));
				classId = Resolve(NativeRegistryKind.Class, identity);
				classMembers[classId] = (descriptor, classMembers.TryGetValue(classId, out var old) ? old.Members + 1 : 1);
			}
			var state = summaries[subjectKey];
			Require(state.Summary.Reference == page.Reference && state.Summary.NativeId == id && state.CollectionId == collection
				&& state.ClassId == classId && state.CommitSequence == receipts[page.Reference].Sequence && state.Summary.StateVersion == 1
				&& !state.Summary.Deleted && state.Summary.Title == page.Title && state.Summary.Collection == Pf2eExtractor.Collection, "Complete summary envelope differs.");
			SameVisibility(CanonicalPage(page).Visibility, state.Summary.Visibility);
			foreach (var fact in factsBySubject[page.Reference])
			{
				var property = Resolve(NativeRegistryKind.Property, fact.Property);
				(byte Tag, ulong Scalar) scalar = fact.Value switch
				{
					PageRef target => (1, Resolve(NativeRegistryKind.Page, target.Id)),
					long integer => (2, unchecked((ulong)integer) ^ 0x8000000000000000UL),
					bool boolean => (3, boolean ? 1UL : 0UL),
					Keyword keyword => (4, Resolve(NativeRegistryKind.Keyword, keyword.Value)),
					_ => throw new InvalidOperationException("Unexpected scalar case.")
				};
				var forward = new byte[25]; var posting = new byte[41];
				BinaryPrimitives.WriteUInt64BigEndian(forward, id); BinaryPrimitives.WriteUInt64BigEndian(forward.AsSpan(8), property);
				forward[16] = scalar.Tag; BinaryPrimitives.WriteUInt64BigEndian(forward.AsSpan(17), scalar.Scalar);
				BinaryPrimitives.WriteUInt64BigEndian(posting, collection); BinaryPrimitives.WriteUInt64BigEndian(posting.AsSpan(8), classId);
				BinaryPrimitives.WriteUInt64BigEndian(posting.AsSpan(16), property); posting[24] = scalar.Tag;
				BinaryPrimitives.WriteUInt64BigEndian(posting.AsSpan(25), scalar.Scalar); BinaryPrimitives.WriteUInt64BigEndian(posting.AsSpan(33), id);
				Require(forward.AsSpan().SequenceEqual(NativeKeys.Forward(id, property, (NativeScalarTag)scalar.Tag, forward.AsSpan(17))), "Frozen forward helper disagrees.");
				Require(posting.AsSpan().SequenceEqual(NativeKeys.Posting(collection, classId, property, (NativeScalarTag)scalar.Tag, posting.AsSpan(25, 8), id)), "Frozen posting helper disagrees.");
				expectedForward.Add(Hex(forward)); expectedPostings.Add(Hex(posting));
			}
		}
		Require(expectedIdentities.SetEquals(ids.Keys), "Unexpected or missing registry identities.");
		foreach (var role in new[] { "pages", "facts", "summaries" })
			Require(expectedSubjectKeys.SetEquals(tables[role].Select(r => Hex(r.Key))), "Orphan/missing aggregate row: " + role);
		foreach (var (role, keys) in new[] { ("forward", expectedForward), ("postings", expectedPostings) })
			Require(tables[role].All(r => r.Value.Length == 0) && tables[role].Count == keys.Count && keys.SetEquals(tables[role].Select(r => Hex(r.Key))), "Complete index mismatch/orphan: " + role);
		Require(expectedForward.Count == 10605 && expectedPostings.Count == 10605, "Index oracle lost canonical facts.");
		CatalogAndMetadata(expected, tables, reverse, classMembers, collection);
		var statuses = tables["mutations"].ToDictionary(r => new Guid(r.Key), r => NativeStateCodec.Mutation(r.Value));
		Require(statuses.Count == receipts.Count, "Extra/missing mutation status.");
		foreach (var receipt in receipts.Values)
			Require(statuses.TryGetValue(receipt.Id, out var status) && status.MutationId == receipt.Id && status.RequestDigest == receipt.Digest
				&& status.Committed == new NativeCommitted(receipt.Id, 1, receipt.Sequence), "Complete mutation table mismatch.");
		Console.WriteLine($"NATIVE_CORPUS registryIdentities={ids.Count} catalogClasses={classMembers.Count} maxDescriptorBytes={classMembers.Values.Max(c => c.Descriptor.Length)} completeForward={expectedForward.Count} completePostings={expectedPostings.Count}");
	}
	private static void CatalogAndMetadata(SemanticSnapshot expected, Dictionary<string, List<Row>> tables,
		Dictionary<(NativeRegistryKind Kind, ulong Id), NativeRegistryEntry> reverse, Dictionary<ulong, (byte[] Descriptor, long Members)> classes, ulong collection)
	{
		var metadata = tables["metadata"].ToDictionary(r => Hex(r.Key), r => r.Value);
		var expectedKeys = new HashSet<string>(StringComparer.Ordinal) { "01", "02", "03", "04", "05" };
		Require(metadata["01"].AsSpan().SequenceEqual(NativeCodec.SchemaIdentity(expected.Schema)), "Exact schema differs after reopen.");
		Require(Number(metadata["02"]) == 4711 && Number(metadata["03"]) == 1 && Number(metadata["04"]) == 1 && Number(metadata["05"]) == 1, "Fixture metadata/sequence differs.");
		foreach (var kind in Enum.GetValues<NativeRegistryKind>())
		{
			var key = Hex([(byte)(20 + (byte)kind)]); expectedKeys.Add(key);
			var allocated = reverse.Keys.Where(k => k.Kind == kind).Select(k => k.Id).Order().ToArray();
			Require(allocated.Length > 0 && allocated.SequenceEqual(Enumerable.Range(1, allocated.Length).Select(i => (ulong)i))
				&& Number(metadata[key]) == (ulong)allocated.Length + 1, "ID allocation counter/gap differs: " + kind);
		}
		long catalogBytes = 0;
		Require(tables["catalog"].Count == classes.Count && tables["partitions"].Count == classes.Count, "Orphan catalog/partition row.");
		var seen = new HashSet<ulong>();
		foreach (var row in tables["catalog"])
		{
			Require(row.Key.Length == 16 && Number(row.Key[..8]) == collection, "Catalog collection/key differs.");
			var classId = Number(row.Key[8..]); Require(seen.Add(classId) && classes.ContainsKey(classId), "Unknown catalog class.");
			var entry = NativeStateCodec.Catalog(row.Value); var expectedClass = classes[classId];
			Require(entry.References == expectedClass.Members && entry.Descriptor.AsSpan().SequenceEqual(expectedClass.Descriptor), "Catalog descriptor/refcount differs.");
			var identity = reverse[(NativeRegistryKind.Class, classId)].Identity;
			var registryBytes = tables["registry"].Single(r => r.Key.Length == 33 && r.Key[0] == (byte)NativeRegistryKind.Class && NativeStateCodec.Registry(r.Value).Identity == identity).Value.Length;
			catalogBytes += row.Key.Length + row.Value.Length + 33 + registryBytes;
		}
		foreach (var row in tables["partitions"])
		{
			Require(row.Key.Length == 16 && Number(row.Key[..8]) == collection && classes.TryGetValue(Number(row.Key[8..]), out var members)
				&& Number(row.Value) == (ulong)members.Members, "Partition version differs from insert count.");
		}
		foreach (var (tag, value) in new[] { ((byte)30, (ulong)classes.Count), ((byte)31, (ulong)catalogBytes) })
		{
			var key = new byte[9]; key[0] = tag; Id(collection).CopyTo(key, 1); expectedKeys.Add(Hex(key));
			Require(Number(metadata[Hex(key)]) == value, "Catalog durable total differs.");
		}
		Require(expectedKeys.SetEquals(metadata.Keys), "Unexpected metadata row.");
		Require(classes.Count <= 1024 && catalogBytes <= 1 << 20, "Catalog quota exceeded.");
		Console.WriteLine($"NATIVE_CORPUS actualActiveCatalogBytes={catalogBytes} metadataRows={metadata.Count}");
	}
	private static void Coverage(SemanticSnapshot snapshot, ImmutableArray<ExtractionUnknown> unknowns)
	{
		Require(snapshot.Pages.Length == 4711 && snapshot.Facts.Facts.Length == 10605, "Corpus coverage changed.");
		foreach (var (property, subjects, values) in new[] { ("pf2e:level", 3396, 3396), ("pf2e:rank", 659, 659),
			("pf2e:tradition", 408, 894), ("pf2e:class", 904, 977), ("pf2e:ancestry", 99, 99), ("pf2e:source-book", 4580, 4580) })
		{
			var facts = snapshot.Facts.Facts.Where(f => f.Property == property).ToArray();
			Require(facts.Length == values && facts.Select(f => f.Subject).Distinct().Count() == subjects, "Coverage differs: " + property);
		}
		Require(unknowns.Length == 7 && unknowns.All(u => u.Property == "pf2e:ancestry")
			&& string.Join(",", unknowns.Select(u => u.Key.SourceKey).Order(StringComparer.Ordinal)) == "aiuvarin,changeling,dhampir,dragonblood,dromaar,duskwalker,nephilim", "Unknown ancestry evidence differs.");
	}
	private static void Goldens(VerifiedDataset dataset, SemanticSnapshot snapshot)
	{
		var alchemist = snapshot.Pages.Single(p => p.Key.Namespace == "main" && p.Key.SourceKey == "alchemist").Reference;
		var query = new SemanticQuery(Pf2eExtractor.Collection, [new EqualPredicate("pf2e:class", alchemist)], ["pf2e:level"], new IntegerSort("pf2e:level"), 100, IncludeCount: true);
		var result = ReferenceEvaluator.Evaluate(snapshot, query, WikiVisibility.All).Expect<QuerySuccess>();
		var expected = new SortedDictionary<string, long>(StringComparer.Ordinal);
		foreach (var line in dataset.Content["wiki/category/alchemist_feats.md"].Split('\n'))
		{
			var level = Regex.Match(line, @"^- \*\*Level ([0-9]+):\*\* ");
			if (!level.Success) continue;
			foreach (Match link in Regex.Matches(line, @"\[\[[^|]+\|main:([^\]]+)\]\]"))
				expected.Add(link.Groups[1].Value, long.Parse(level.Groups[1].Value, CultureInfo.InvariantCulture));
		}
		var keys = snapshot.Pages.ToDictionary(p => p.Reference, p => p.Key.SourceKey);
		var actual = result.Rows.ToDictionary(r => keys[r.Subject], r => r.Cells.Single().Values.Single().Expect<long>());
		Require(expected.Count == 47 && result.Count == 47 && !result.HasMore && actual.Count == 47
			&& expected.All(pair => actual.TryGetValue(pair.Key, out var value) && value == pair.Value), "Reopened Alchemist authored golden differs.");
		var dwarf = snapshot.Pages.Single(p => p.Key.Namespace == "main" && p.Key.SourceKey == "dwarf").Reference;
		var heritages = ReferenceEvaluator.Evaluate(snapshot, query with { Predicates = [new EqualPredicate("pf2e:ancestry", dwarf)], Columns = [], Sort = new IdSort() }, WikiVisibility.All).Expect<QuerySuccess>();
		Require(heritages.Count == 5 && !heritages.HasMore && string.Join(",", heritages.Rows.Select(r => keys[r.Subject]).Order(StringComparer.Ordinal))
			== "ancient_blooded_dwarf,death_warden_dwarf,forge_dwarf,rock_dwarf,strong_blooded_dwarf", "Reopened Dwarf authored golden differs.");
		foreach (var path in new[] { "NOTICE.md", "README.md", "wiki/help/orc_license_notice.md" })
			Console.WriteLine($"NATIVE_LICENSE path={path} sourceGitBlob={dataset.Blobs[path]} utf8Sha256={Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(dataset.Content[path])))}");
	}
	private static string Digest(Dictionary<string, List<Row>> tables)
	{
		using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
		Span<byte> length = stackalloc byte[8];
		foreach (var (name, rows) in tables.OrderBy(t => t.Key, StringComparer.Ordinal))
		{
			hash.AppendData(Encoding.UTF8.GetBytes(name)); hash.AppendData([0]);
			foreach (var row in rows)
			{
				BinaryPrimitives.WriteInt32BigEndian(length, row.Key.Length); BinaryPrimitives.WriteInt32BigEndian(length[4..], row.Value.Length);
				hash.AppendData(length); hash.AppendData(row.Key); hash.AppendData(row.Value);
			}
		}
		return Convert.ToHexStringLower(hash.GetHashAndReset());
	}
	private static string Distribution(List<double> values)
	{
		var sorted = values.Order().ToArray();
		return string.Create(CultureInfo.InvariantCulture, $"count:{sorted.Length},p50:{sorted[sorted.Length / 2]:F3},p95:{sorted[(int)((sorted.Length - 1) * .95)]:F3},max:{sorted[^1]:F3}");
	}
	private static async Task Storage(string path)
	{
		var files = Directory.GetFiles(path, "*", SearchOption.AllDirectories);
		Console.WriteLine($"NATIVE_STORAGE fileLengthBytes={files.Sum(f => new FileInfo(f).Length)} mapCeilingBytes={256L << 20} fileLengthIsNotAllocatedDisk=true");
		if (!OperatingSystem.IsLinux()) { Console.WriteLine("NATIVE_STORAGE allocatedDiskBytes=unavailable-nonLinux"); return; }
		long allocated = 0;
		foreach (var file in files)
		{
			var start = new ProcessStartInfo("stat") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
			start.ArgumentList.Add("--format=%b %B"); start.ArgumentList.Add("--"); start.ArgumentList.Add(file);
			using var child = Process.Start(start) ?? throw new InvalidOperationException("Could not start read-only storage stat.");
			var output = await child.StandardOutput.ReadToEndAsync(); var error = await child.StandardError.ReadToEndAsync();
			await child.WaitForExitAsync(); Require(child.ExitCode == 0, "Storage stat failed: " + error);
			var fields = output.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
			Require(fields.Length == 2, "Unexpected stat block output.");
			allocated = checked(allocated + long.Parse(fields[0], CultureInfo.InvariantCulture) * long.Parse(fields[1], CultureInfo.InvariantCulture));
		}
		Console.WriteLine($"NATIVE_STORAGE allocatedDiskBytes={allocated} allocatedMethod=stat-st_blocks-times-blockUnit");
	}
}
